using AutoPartsErp.Modules.Pricing.Domain;
using AutoPartsErp.Modules.Pricing.Domain.Customers;
using AutoPartsErp.Modules.Pricing.Domain.PriceLists;
using AutoPartsErp.Modules.Pricing.Domain.Quotes;
using Microsoft.EntityFrameworkCore;

namespace AutoPartsErp.Modules.Pricing.Infrastructure.Persistence.Repositories;

/// <summary>Write-side access to price lists.</summary>
public sealed class PriceListRepository : IPriceListRepository
{
    private readonly PricingDbContext _context;

    /// <summary>Initializes the repository.</summary>
    public PriceListRepository(PricingDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public Task<PriceList?> GetByIdAsync(
        PriceListId id,
        CancellationToken cancellationToken = default) =>
        _context.PriceLists.FirstOrDefaultAsync(list => list.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsAsync(PriceListId id, CancellationToken cancellationToken = default) =>
        _context.PriceLists.AnyAsync(list => list.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<PriceList?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        string normalized = code?.Trim().ToUpperInvariant() ?? string.Empty;

        return _context.PriceLists.FirstOrDefaultAsync(
            list => list.Code == normalized, cancellationToken);
    }

    /// <inheritdoc />
    public Task<PriceList?> GetDefaultAsync(CancellationToken cancellationToken = default) =>
        _context.PriceLists.FirstOrDefaultAsync(list => list.IsDefault, cancellationToken);

    /// <inheritdoc />
    public Task<bool> CodeExistsAsync(
        string code,
        PriceListId? excluding = null,
        CancellationToken cancellationToken = default)
    {
        string normalized = code?.Trim().ToUpperInvariant() ?? string.Empty;

        IQueryable<PriceList> query = _context.PriceLists.Where(list => list.Code == normalized);

        if (excluding is { } excluded)
        {
            query = query.Where(list => list.Id != excluded);
        }

        return query.AnyAsync(cancellationToken);
    }

    /// <inheritdoc />
    public void Add(PriceList aggregate) => _context.PriceLists.Add(aggregate);

    /// <inheritdoc />
    public void Remove(PriceList aggregate) => _context.PriceLists.Remove(aggregate);
}

/// <summary>
/// Write-side access to the prices inside a list.
/// <para>
/// No <c>Include</c> for the breaks anywhere in here: they are an owned collection, so EF loads
/// them with their entry automatically.
/// </para>
/// </summary>
public sealed class PriceListEntryRepository : IPriceListEntryRepository
{
    private readonly PricingDbContext _context;

    /// <summary>Initializes the repository.</summary>
    public PriceListEntryRepository(PricingDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public Task<PriceListEntry?> GetByIdAsync(
        PriceListEntryId id,
        CancellationToken cancellationToken = default) =>
        _context.PriceListEntries.FirstOrDefaultAsync(entry => entry.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsAsync(PriceListEntryId id, CancellationToken cancellationToken = default) =>
        _context.PriceListEntries.AnyAsync(entry => entry.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<PriceListEntry?> GetForAsync(
        PriceListId priceListId,
        PartRef partId,
        CancellationToken cancellationToken = default) =>
        _context.PriceListEntries.FirstOrDefaultAsync(
            entry => entry.PriceListId == priceListId && entry.PartId == partId,
            cancellationToken);

    /// <inheritdoc />
    public Task<bool> AnyInAsync(PriceListId priceListId, CancellationToken cancellationToken = default) =>
        _context.PriceListEntries.AnyAsync(
            entry => entry.PriceListId == priceListId, cancellationToken);

    /// <inheritdoc />
    public void Add(PriceListEntry aggregate) => _context.PriceListEntries.Add(aggregate);

    /// <inheritdoc />
    public void Remove(PriceListEntry aggregate) => _context.PriceListEntries.Remove(aggregate);
}

/// <summary>Write-side access to customer agreements.</summary>
public sealed class CustomerPricingRepository : ICustomerPricingRepository
{
    private readonly PricingDbContext _context;

    /// <summary>Initializes the repository.</summary>
    public CustomerPricingRepository(PricingDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public Task<CustomerPricing?> GetByIdAsync(
        CustomerPricingId id,
        CancellationToken cancellationToken = default) =>
        _context.CustomerAgreements.FirstOrDefaultAsync(
            agreement => agreement.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsAsync(CustomerPricingId id, CancellationToken cancellationToken = default) =>
        _context.CustomerAgreements.AnyAsync(agreement => agreement.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<CustomerPricing?> GetForCustomerAsync(
        CustomerRef customerId,
        CancellationToken cancellationToken = default) =>
        _context.CustomerAgreements.FirstOrDefaultAsync(
            agreement => agreement.CustomerId == customerId, cancellationToken);

    /// <inheritdoc />
    public void Add(CustomerPricing aggregate) => _context.CustomerAgreements.Add(aggregate);

    /// <inheritdoc />
    public void Remove(CustomerPricing aggregate) => _context.CustomerAgreements.Remove(aggregate);
}

/// <summary>
/// The one query the resolver runs on: every live list that prices this part today, with its entry.
/// <para>
/// Written as a single round trip on purpose. This runs once per line on every counter sale, and
/// the difference between one query and one-per-list is the difference between a screen that
/// feels instant and one that does not.
/// </para>
/// <para>
/// It deliberately does not take the customer. Filtering other customers' lists out in SQL would
/// mean fetching the agreement first and turning one query into two — and the resolver has to
/// apply that rule anyway, because it is a domain rule and not an index. So the query stays
/// index-shaped and hands back a little more than is needed.
/// </para>
/// </summary>
public sealed class PriceCandidateSource : IPriceCandidateSource
{
    private readonly PricingDbContext _context;

    /// <summary>Initializes the source.</summary>
    public PriceCandidateSource(PricingDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PriceCandidate>> GetCandidatesAsync(
        PartRef partId,
        DateOnly on,
        CancellationToken cancellationToken = default)
    {
        // The date window is applied here as well as in PriceList.IsEffectiveOn. Not a duplicated
        // rule so much as the same rule twice over: in SQL so an expired promotion never leaves
        // the database, and in the aggregate so the resolver is correct when it is handed a list
        // by a test. The aggregate is the one that decides; this is an index hint that agrees
        // with it.
        var rows = await _context.PriceListEntries
            .AsNoTracking()
            .Where(entry => entry.PartId == partId)
            .Join(
                _context.PriceLists.AsNoTracking().Where(list =>
                    list.Status == PriceListStatus.Active
                    && (list.EffectiveFrom == null || list.EffectiveFrom <= on)
                    && (list.EffectiveTo == null || list.EffectiveTo >= on)),
                entry => entry.PriceListId,
                list => list.Id,
                (entry, list) => new { Entry = entry, List = list })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. rows.Select(row => new PriceCandidate(row.List, row.Entry))];
    }
}
