using AutoPartsErp.Modules.Pricing.Application.Contracts;
using AutoPartsErp.SharedKernel.Paging;

namespace AutoPartsErp.Modules.Pricing.Application.Abstractions;

/// <summary>The read side of the Pricing module.</summary>
public interface IPricingReadStore
{
    /// <summary>Loads one list, or null when it does not exist.</summary>
    Task<PriceListSummary?> GetListAsync(Guid priceListId, CancellationToken cancellationToken = default);

    /// <summary>Loads one list by its code, the way a buyer refers to one.</summary>
    Task<PriceListSummary?> GetListByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>Lists price lists.</summary>
    /// <param name="criteria">What to look for.</param>
    /// <param name="page">Which page to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<PagedResult<PriceListSummary>> SearchListsAsync(
        PriceListSearchCriteria criteria,
        PageRequest page,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The prices inside one list.
    /// <para>
    /// Paged without an option not to be. A standard list is tens of thousands of parts, and the
    /// convenient overload that returns all of them is the one somebody eventually calls from a
    /// loop.
    /// </para>
    /// </summary>
    /// <param name="priceListId">The list.</param>
    /// <param name="page">Which page to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<PagedResult<PriceListEntryDto>> ListPricesAsync(
        Guid priceListId,
        PageRequest page,
        CancellationToken cancellationToken = default);

    /// <summary>What one list says one part costs, or null when it does not price it.</summary>
    Task<PriceListEntryDto?> GetPriceAsync(
        Guid priceListId,
        Guid partId,
        CancellationToken cancellationToken = default);

    /// <summary>The terms agreed with a customer, or null when they have none.</summary>
    Task<CustomerPricingDto?> GetAgreementAsync(
        Guid customerId,
        CancellationToken cancellationToken = default);

    /// <summary>Every customer agreement pointing at one list — who a price change would reach.</summary>
    /// <param name="priceListId">The list.</param>
    /// <param name="page">Which page to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<PagedResult<CustomerPricingDto>> ListAgreementsForListAsync(
        Guid priceListId,
        PageRequest page,
        CancellationToken cancellationToken = default);
}
