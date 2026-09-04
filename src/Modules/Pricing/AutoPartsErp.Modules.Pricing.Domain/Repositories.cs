using AutoPartsErp.Modules.Pricing.Domain.Customers;
using AutoPartsErp.Modules.Pricing.Domain.PriceLists;
using AutoPartsErp.Modules.Pricing.Domain.Quotes;
using AutoPartsErp.SharedKernel.Abstractions;

namespace AutoPartsErp.Modules.Pricing.Domain;

/// <summary>Write-side access to price lists.</summary>
public interface IPriceListRepository : IRepository<PriceList, PriceListId>
{
    /// <summary>Loads a list by its code, the way a buyer refers to one.</summary>
    Task<PriceList?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>The list customers with no agreement fall back to, or null when none is set.</summary>
    Task<PriceList?> GetDefaultAsync(CancellationToken cancellationToken = default);

    /// <summary>True when a list already uses that code.</summary>
    Task<bool> CodeExistsAsync(
        string code,
        PriceListId? excluding = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Write-side access to the prices inside a list.</summary>
public interface IPriceListEntryRepository : IRepository<PriceListEntry, PriceListEntryId>
{
    /// <summary>What a list says one part costs, or null when it does not price it.</summary>
    Task<PriceListEntry?> GetForAsync(
        PriceListId priceListId,
        PartRef partId,
        CancellationToken cancellationToken = default);

    /// <summary>True when the list prices at least one part. What <c>Activate</c> needs to know.</summary>
    Task<bool> AnyInAsync(PriceListId priceListId, CancellationToken cancellationToken = default);
}

/// <summary>Write-side access to customer agreements.</summary>
public interface ICustomerPricingRepository : IRepository<CustomerPricing, CustomerPricingId>
{
    /// <summary>The terms agreed with a customer, or null when they have none.</summary>
    Task<CustomerPricing?> GetForCustomerAsync(
        CustomerRef customerId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Everything needed to price one part for one customer, fetched in one go.
/// <para>
/// Its own port rather than three repository calls in a handler, because the fetch is the
/// performance-sensitive half of pricing and the resolver is the correct half. Keeping them apart
/// means the rules can be tested with hand-built objects while the query stays free to be as
/// clever as it has to be.
/// </para>
/// </summary>
public interface IPriceCandidateSource
{
    /// <summary>
    /// Every live list that prices this part on this day, each with its entry.
    /// <para>
    /// Customer lists other than the caller's own may come back; the resolver filters them, so
    /// this can stay one index-friendly query rather than a query that needs the agreement first.
    /// </para>
    /// </summary>
    /// <param name="partId">The part.</param>
    /// <param name="on">The day being priced for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<PriceCandidate>> GetCandidatesAsync(
        PartRef partId,
        DateOnly on,
        CancellationToken cancellationToken = default);
}

/// <summary>The Pricing module's unit of work.</summary>
public interface IPricingUnitOfWork : IUnitOfWork;
