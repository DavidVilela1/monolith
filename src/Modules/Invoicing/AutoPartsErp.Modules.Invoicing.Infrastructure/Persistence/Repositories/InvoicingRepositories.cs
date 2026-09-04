using AutoPartsErp.Modules.Invoicing.Domain;
using AutoPartsErp.Modules.Invoicing.Domain.Invoices;
using AutoPartsErp.Modules.Invoicing.Domain.Series;
using AutoPartsErp.SharedKernel.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace AutoPartsErp.Modules.Invoicing.Infrastructure.Persistence.Repositories;

/// <summary>Write-side access to document series.</summary>
public sealed class DocumentSeriesRepository : IDocumentSeriesRepository
{
    // The lock statement, kept as a constant so nothing can concatenate a value into it. The
    // placeholders become real parameters; the table name is the only thing interpolated, and it
    // comes from a compile-time constant rather than from anything a caller supplies.
    private const string LockSql =
        "SELECT 1 FROM \"" + InvoicingDbContext.SchemaName + "\".\"document_series\" "
        + "WHERE id = {0} AND tenant_id = {1} FOR UPDATE";

    private readonly InvoicingDbContext _context;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initializes the repository.</summary>
    public DocumentSeriesRepository(InvoicingDbContext context, ITenantContext tenantContext)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public Task<DocumentSeries?> GetByIdAsync(
        DocumentSeriesId id,
        CancellationToken cancellationToken = default) =>
        _context.DocumentSeries.FirstOrDefaultAsync(series => series.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsAsync(DocumentSeriesId id, CancellationToken cancellationToken = default) =>
        _context.DocumentSeries.AnyAsync(series => series.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task<DocumentSeries?> GetForIssuingAsync(
        DocumentSeriesId id,
        CancellationToken cancellationToken = default)
    {
        // A lock only lasts as long as the transaction holding it, so being called without one is
        // not a situation to work around — it is a caller that would silently get no lock at all
        // and gapless numbering that quietly stops being gapless under load. That is a bug in the
        // calling code, so it throws.
        if (_context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "GetForIssuingAsync must be called inside a transaction, or the row lock it takes "
                + "is released before the document that uses the number is written. Start one with "
                + "IInvoicingUnitOfWork.BeginAsync.");
        }

        // Two statements rather than one, deliberately. Composing FOR UPDATE into an EF query means
        // hoping EF does not wrap it in a subquery when it adds the tenant filter; locking first
        // and then loading normally is the same two round trips with none of the hoping. The lock
        // is held by the ambient transaction until it commits, which is exactly as long as needed.
        //
        // If the row is already locked by another issue in flight, this waits. That is the point:
        // waiting four milliseconds is a better outcome for the person at the till than a failed
        // request they have to repeat.
        await _context.Database
            .ExecuteSqlRawAsync(LockSql, [id.Value, _tenantContext.TenantId], cancellationToken)
            .ConfigureAwait(false);

        // Loaded after the lock, never before. A series read first and locked second would be a
        // snapshot from before whoever held the lock incremented it, and the number taken from it
        // would be one already issued.
        //
        // And if this scope has already read the series — which the "find the active one, then
        // lock it" path does — a plain query would hand back the tracked instance with its stale
        // NextNumber, because EF does not overwrite a tracked entity from a later query. That is
        // the whole lock defeated by an identity map, silently, and only under contention. So a
        // tracked instance is reloaded rather than reused.
        DocumentSeries? tracked = _context.ChangeTracker
            .Entries<DocumentSeries>()
            .Select(entry => entry.Entity)
            .FirstOrDefault(series => series.Id == id);

        if (tracked is not null)
        {
            await _context.Entry(tracked).ReloadAsync(cancellationToken).ConfigureAwait(false);
            return tracked;
        }

        return await _context.DocumentSeries
            .FirstOrDefaultAsync(series => series.Id == id, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<DocumentSeries?> GetActiveAsync(
        DocumentType type,
        int year,
        CancellationToken cancellationToken = default) =>

        // Untracked, because the only caller that matters uses this to find *which* series and
        // then reloads it under a lock. Tracking it here would put a pre-lock copy in the change
        // tracker, which is exactly the stale read the lock exists to prevent.
        _context.DocumentSeries
            .AsNoTracking()
            .Where(series => series.Type == type && series.Year == year)
            .Where(series => series.Status == SeriesStatus.Active)
            .OrderBy(series => series.Code)
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public Task<bool> CodeExistsAsync(
        DocumentType type,
        string code,
        int year,
        CancellationToken cancellationToken = default)
    {
        string normalized = code?.Trim().ToUpperInvariant() ?? string.Empty;

        // Query filters ignored and the tenant applied by hand, the same way Purchasing does it
        // for order numbers: a closed series still owns its code, and reusing it would produce two
        // different runs of numbers that the AT has on file as one.
        return _context.DocumentSeries
            .IgnoreQueryFilters()
            .Where(series => series.TenantId == _tenantContext.TenantId)
            .AnyAsync(
                series => series.Type == type && series.Code == normalized && series.Year == year,
                cancellationToken);
    }

    /// <inheritdoc />
    public void Add(DocumentSeries aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.DocumentSeries.Add(aggregate);
    }

    /// <inheritdoc />
    public void Remove(DocumentSeries aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.DocumentSeries.Remove(aggregate);
    }
}

/// <summary>
/// Write-side access to documents.
/// <para>
/// No <c>Include</c> for the lines: they are an owned collection and EF loads them with their
/// document. There is no way to load half an invoice, which for this aggregate is not a
/// convenience but a requirement — the totals are computed from the lines and the signature covers
/// the totals.
/// </para>
/// </summary>
public sealed class InvoiceRepository : IInvoiceRepository
{
    private readonly InvoicingDbContext _context;

    /// <summary>Initializes the repository.</summary>
    public InvoiceRepository(InvoicingDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public Task<Invoice?> GetByIdAsync(InvoiceId id, CancellationToken cancellationToken = default) =>
        _context.Invoices.FirstOrDefaultAsync(invoice => invoice.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsAsync(InvoiceId id, CancellationToken cancellationToken = default) =>
        _context.Invoices.AnyAsync(invoice => invoice.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<Invoice?> GetByNumberAsync(
        string documentNumber,
        CancellationToken cancellationToken = default)
    {
        string normalized = documentNumber?.Trim().ToUpperInvariant() ?? string.Empty;

        return _context.Invoices.FirstOrDefaultAsync(
            invoice => invoice.DocumentNumber == normalized, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<string?> GetLastSignatureAsync(
        DocumentSeriesId seriesId,
        CancellationToken cancellationToken = default)
    {
        // Ordered by the series number, not by the document number and not by a timestamp. The
        // document number sorts as text, which puts /9 after /10; a timestamp is only monotonic
        // because the series lock happens to serialise issues, which is true today and is not a
        // property worth depending on. The series number is the sequence itself.
        //
        // Projected to the signature rather than loading the document, because the document before
        // this one carries lines this call has no use for.
        string? signature = await _context.Invoices
            .AsNoTracking()
            .Where(invoice => invoice.SeriesId == seriesId && invoice.SeriesNumber > 0)
            .OrderByDescending(invoice => invoice.SeriesNumber)
            .Select(invoice => invoice.Signature!.Value)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        // Voided documents are included on purpose. A void changes a status; it does not remove
        // the document from the chain, and the next signature still chains onto it. Skipping them
        // would produce a chain the tax authority's validator walks straight off the end of.
        return signature;
    }

    /// <inheritdoc />
    public void Add(Invoice aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.Invoices.Add(aggregate);
    }

    /// <inheritdoc />
    public void Remove(Invoice aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.Invoices.Remove(aggregate);
    }
}
