using AutoPartsErp.Modules.Invoicing.Application.Abstractions;
using AutoPartsErp.Modules.Invoicing.Application.Contracts;
using AutoPartsErp.Modules.Invoicing.Domain;
using AutoPartsErp.Modules.Invoicing.Domain.Invoices;
using AutoPartsErp.Modules.Invoicing.Domain.Series;
using AutoPartsErp.SharedKernel.Paging;
using Microsoft.EntityFrameworkCore;

namespace AutoPartsErp.Modules.Invoicing.Infrastructure.Persistence.ReadStore;

/// <summary>Serves the Invoicing module's queries.</summary>
public sealed class InvoicingReadStore : IInvoicingReadStore
{
    private readonly InvoicingDbContext _context;

    /// <summary>Initializes the read store.</summary>
    public InvoicingReadStore(InvoicingDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<DocumentSeriesDto?> GetSeriesAsync(
        Guid seriesId,
        CancellationToken cancellationToken = default)
    {
        var id = new DocumentSeriesId(seriesId);

        DocumentSeries? series = await _context.DocumentSeries
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken)
            .ConfigureAwait(false);

        return series is null ? null : MapSeries(series);
    }

    /// <inheritdoc />
    public async Task<PagedResult<DocumentSeriesDto>> ListSeriesAsync(
        string? type,
        int? year,
        PageRequest page,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);

        IQueryable<DocumentSeries> query = _context.DocumentSeries.AsNoTracking();

        // An unrecognised type filters nothing away rather than returning nothing. A caller who
        // sends "FX" has made a typo, and an empty list is a worse way to find that out than a
        // list that visibly contains no FX.
        if (!string.IsNullOrWhiteSpace(type)
            && DocumentTypeCodes.TryFromCode(type, out DocumentType parsed))
        {
            query = query.Where(series => series.Type == parsed);
        }

        if (year is { } requestedYear)
        {
            query = query.Where(series => series.Year == requestedYear);
        }

        int total = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        if (total == 0)
        {
            return PagedResult<DocumentSeriesDto>.Empty(page.Page, page.PageSize);
        }

        List<DocumentSeries> rows = await query
            .OrderByDescending(series => series.Year)
            .ThenBy(series => series.Type)
            .ThenBy(series => series.Code)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<DocumentSeriesDto> items = [.. rows.Select(MapSeries)];

        return PagedResult<DocumentSeriesDto>.Create(items, page.Page, page.PageSize, total);
    }

    /// <inheritdoc />
    public async Task<InvoiceDetail?> GetDocumentAsync(
        Guid invoiceId,
        CancellationToken cancellationToken = default)
    {
        var id = new InvoiceId(invoiceId);

        Invoice? invoice = await _context.Invoices
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken)
            .ConfigureAwait(false);

        return invoice is null ? null : MapDetail(invoice);
    }

    /// <inheritdoc />
    public async Task<InvoiceDetail?> GetDocumentByNumberAsync(
        string documentNumber,
        CancellationToken cancellationToken = default)
    {
        string normalized = documentNumber?.Trim().ToUpperInvariant() ?? string.Empty;

        // A draft has an empty number and there can be many of them, so an empty search term must
        // not resolve to "the first draft in the table".
        if (normalized.Length == 0)
        {
            return null;
        }

        Invoice? invoice = await _context.Invoices
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.DocumentNumber == normalized, cancellationToken)
            .ConfigureAwait(false);

        return invoice is null ? null : MapDetail(invoice);
    }

    /// <inheritdoc />
    public async Task<PagedResult<InvoiceSummary>> SearchDocumentsAsync(
        InvoiceSearchCriteria criteria,
        PageRequest page,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        ArgumentNullException.ThrowIfNull(page);

        IQueryable<Invoice> query = _context.Invoices.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(criteria.Term))
        {
            string term = criteria.Term.Trim();
            string upper = term.ToUpperInvariant();

            // The number is stored uppercase, so it matches on the uppercased term; the customer
            // name is stored as it was written, so it matches case-insensitively.
            query = query.Where(invoice =>
                EF.Functions.Like(invoice.DocumentNumber, $"%{upper}%")
                || EF.Functions.ILike(invoice.CustomerName, $"%{term}%")
                || (invoice.CustomerTaxNumber != null
                    && EF.Functions.Like(invoice.CustomerTaxNumber, $"{upper}%")));
        }

        if (criteria.CustomerId is { } customerId)
        {
            var customer = new CustomerRef(customerId);
            query = query.Where(invoice => invoice.CustomerId == customer);
        }

        if (!string.IsNullOrWhiteSpace(criteria.Type)
            && DocumentTypeCodes.TryFromCode(criteria.Type, out DocumentType type))
        {
            query = query.Where(invoice => invoice.Type == type);
        }

        if (!string.IsNullOrWhiteSpace(criteria.Status)
            && Enum.TryParse(criteria.Status, ignoreCase: true, out InvoiceStatus status))
        {
            query = query.Where(invoice => invoice.Status == status);
        }

        if (criteria.From is { } from)
        {
            query = query.Where(invoice => invoice.DocumentDate >= from);
        }

        if (criteria.To is { } to)
        {
            query = query.Where(invoice => invoice.DocumentDate <= to);
        }

        // Draft-ness is asked of the series number rather than of the signature, because an int
        // comparison indexes and a test on an owned type's nullable column does not. They say the
        // same thing: a document has both or neither.
        query = criteria.DraftsOnly
            ? query.Where(invoice => invoice.SeriesNumber == 0)
            : query.Where(invoice => invoice.SeriesNumber > 0);

        int total = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        if (total == 0)
        {
            return PagedResult<InvoiceSummary>.Empty(page.Page, page.PageSize);
        }

        // Newest first, and within a day the highest number first — which is the order they were
        // issued in, and the order the person looking for "the one I just did" expects.
        List<Invoice> rows = await query
            .OrderByDescending(invoice => invoice.DocumentDate)
            .ThenByDescending(invoice => invoice.SeriesNumber)
            .ThenByDescending(invoice => invoice.CreatedAtUtc)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<InvoiceSummary> items = [.. rows.Select(MapSummary)];

        return PagedResult<InvoiceSummary>.Create(items, page.Page, page.PageSize, total);
    }

    private static DocumentSeriesDto MapSeries(DocumentSeries series) => new(
        series.Id.Value,
        series.Type.Code(),
        series.Code,
        series.Year,
        series.Status.ToString(),
        series.ValidationCode,
        series.NextNumber,
        series.IssuedCount);

    private static InvoiceSummary MapSummary(Invoice invoice)
    {
        TaxSummary taxes = invoice.Taxes;

        return new InvoiceSummary(
            invoice.Id.Value,
            invoice.Type.Code(),
            invoice.DocumentNumber,
            invoice.Atcud?.Value,
            invoice.Status.ToString(),
            invoice.DocumentDate,
            invoice.CustomerId.Value,
            invoice.CustomerName,
            invoice.CustomerTaxNumber,
            taxes.NetTotal,
            taxes.VatTotal,
            taxes.GrossTotal,
            invoice.CurrencyCode);
    }

    private static InvoiceDetail MapDetail(Invoice invoice)
    {
        TaxSummary taxes = invoice.Taxes;

        return new InvoiceDetail
        {
            Id = invoice.Id.Value,
            Type = invoice.Type.Code(),
            DocumentNumber = invoice.DocumentNumber,
            Atcud = invoice.Atcud?.Value,
            Status = invoice.Status.ToString(),
            DocumentDate = invoice.DocumentDate,
            SystemEntryDateUtc = invoice.SystemEntryDateUtc,
            CustomerId = invoice.CustomerId.Value,
            CustomerName = invoice.CustomerName,
            CustomerTaxNumber = invoice.CustomerTaxNumber,
            CustomerCountry = invoice.CustomerCountry,
            TaxRegion = invoice.TaxRegion.Code(),
            CurrencyCode = invoice.CurrencyCode,
            SalesOrderId = invoice.SalesOrderId?.Value,
            ExemptBase = taxes.ExemptBase,
            ReducedBase = taxes.ReducedBase,
            ReducedVat = taxes.ReducedVat,
            IntermediateBase = taxes.IntermediateBase,
            IntermediateVat = taxes.IntermediateVat,
            StandardBase = taxes.StandardBase,
            StandardVat = taxes.StandardVat,
            NetTotal = taxes.NetTotal,
            VatTotal = taxes.VatTotal,
            GrossTotal = taxes.GrossTotal,

            // The four characters, never the signature itself. What goes on the page is the
            // extract; the full signature is a chain link that has no business leaving the
            // module through a read model.
            SignatureCharacters = invoice.Signature?.Printed,
            QrCode = invoice.QrCode,
            VoidReason = invoice.VoidReason,
            IsDraft = invoice.IsDraft,
            Lines = [.. invoice.Lines.Select(line => new InvoiceLineDto(
                line.Id.Value,
                line.Number,
                line.PartId.Value,
                line.Sku,
                line.Description,
                line.Quantity.Value,
                line.Quantity.Unit.Code,
                line.UnitPrice.Amount,
                line.DiscountPercent,
                line.NetAmount.Amount,
                line.VatRate.TaxCode,
                line.VatRate.Percent,
                line.VatAmount.Amount,
                line.GrossAmount.Amount,
                line.VatRate.ExemptionCode,
                line.VatRate.ExemptionReason))],
        };
    }
}
