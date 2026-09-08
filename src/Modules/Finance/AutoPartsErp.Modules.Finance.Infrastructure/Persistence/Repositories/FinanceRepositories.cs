using System.Globalization;
using AutoPartsErp.Modules.Finance.Domain;
using AutoPartsErp.Modules.Finance.Domain.Customers;
using AutoPartsErp.Modules.Finance.Domain.Receipts;
using AutoPartsErp.Modules.Finance.Domain.Receivables;
using Microsoft.EntityFrameworkCore;

namespace AutoPartsErp.Modules.Finance.Infrastructure.Persistence.Repositories;

/// <summary>Write-side access to the items on customers' accounts.</summary>
public sealed class OpenItemRepository : IOpenItemRepository
{
    private readonly FinanceDbContext _context;

    /// <summary>Initializes the repository.</summary>
    public OpenItemRepository(FinanceDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public Task<OpenItem?> GetByIdAsync(
        OpenItemId id,
        CancellationToken cancellationToken = default) =>
        _context.OpenItems.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsAsync(OpenItemId id, CancellationToken cancellationToken = default) =>
        _context.OpenItems.AnyAsync(item => item.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<OpenItem?> GetByDocumentAsync(
        DocumentRef documentId,
        CancellationToken cancellationToken = default) =>
        _context.OpenItems.FirstOrDefaultAsync(
            item => item.DocumentId == documentId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<OpenItem>> GetManyAsync(
        IReadOnlyCollection<OpenItemId> ids,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        if (ids.Count == 0)
        {
            return [];
        }

        // One query rather than one per identifier. An allocation screen sends a dozen of these
        // at a time, and a loop of round trips is how a settlement that should take milliseconds
        // takes a second.
        List<OpenItem> items = await _context.OpenItems
            .Where(item => ids.Contains(item.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return items;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OpenItem>> GetOutstandingForCustomerAsync(
        CustomerRef customerId,
        CancellationToken cancellationToken = default)
    {
        List<OpenItem> items = await _context.OpenItems
            .Where(item => item.CustomerId == customerId)
            .Where(item => item.Status == OpenItemStatus.Open
                || item.Status == OpenItemStatus.PartiallySettled)
            .OrderBy(item => item.DueDate)
            .ThenBy(item => item.DocumentDate)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return items;
    }

    /// <inheritdoc />
    public void Add(OpenItem aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.OpenItems.Add(aggregate);
    }

    /// <inheritdoc />
    public void Remove(OpenItem aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.OpenItems.Remove(aggregate);
    }
}

/// <summary>
/// Write-side access to receipts.
/// <para>
/// No <c>Include</c> for the allocations: they are an owned collection and EF loads them with
/// their receipt. There is no way to load half a receipt, which here is not a convenience but a
/// requirement — the allocated total is checked against them on every match.
/// </para>
/// </summary>
public sealed class ReceiptRepository : IReceiptRepository
{
    private const string NumberPrefix = "RC";

    /// <summary>Identifies this run of numbers in the module's counter table.</summary>
    private const string NumberKey = "receipt";

    private readonly FinanceDbContext _context;

    /// <summary>Initializes the repository.</summary>
    public ReceiptRepository(FinanceDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public Task<Receipt?> GetByIdAsync(
        ReceiptId id,
        CancellationToken cancellationToken = default) =>
        _context.Receipts.FirstOrDefaultAsync(receipt => receipt.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsAsync(ReceiptId id, CancellationToken cancellationToken = default) =>
        _context.Receipts.AnyAsync(receipt => receipt.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<Receipt?> GetByNumberAsync(
        string number,
        CancellationToken cancellationToken = default)
    {
        string normalized = number?.Trim().ToUpperInvariant() ?? string.Empty;

        return _context.Receipts.FirstOrDefaultAsync(
            receipt => receipt.Number == normalized, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<string> NextReceiptNumberAsync(
        int year,
        CancellationToken cancellationToken = default)
    {
        int next = await _context
            .TakeNextNumberAsync(NumberKey, year, cancellationToken)
            .ConfigureAwait(false);

        return string.Create(CultureInfo.InvariantCulture, $"{NumberPrefix}-{year}-{next:D5}");
    }

    /// <inheritdoc />
    public void Add(Receipt aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.Receipts.Add(aggregate);
    }

    /// <inheritdoc />
    public void Remove(Receipt aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.Receipts.Remove(aggregate);
    }
}

/// <summary>Write-side access to customers' payment terms.</summary>
public sealed class CustomerTermsRepository : ICustomerTermsRepository
{
    private readonly FinanceDbContext _context;

    /// <summary>Initializes the repository.</summary>
    public CustomerTermsRepository(FinanceDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public Task<CustomerTerms?> GetByIdAsync(
        CustomerRef id,
        CancellationToken cancellationToken = default) =>
        _context.CustomerTerms.FirstOrDefaultAsync(terms => terms.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsAsync(CustomerRef id, CancellationToken cancellationToken = default) =>
        _context.CustomerTerms.AnyAsync(terms => terms.Id == id, cancellationToken);

    /// <inheritdoc />
    public void Add(CustomerTerms aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.CustomerTerms.Add(aggregate);
    }

    /// <inheritdoc />
    public void Remove(CustomerTerms aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.CustomerTerms.Remove(aggregate);
    }
}
