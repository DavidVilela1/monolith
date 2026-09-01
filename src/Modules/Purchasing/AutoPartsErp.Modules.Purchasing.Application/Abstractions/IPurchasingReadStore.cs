using AutoPartsErp.Modules.Purchasing.Application.Contracts;
using AutoPartsErp.SharedKernel.Paging;

namespace AutoPartsErp.Modules.Purchasing.Application.Abstractions;

/// <summary>The read side of the Purchasing module.</summary>
public interface IPurchasingReadStore
{
    /// <summary>Loads one order in full, or null when it does not exist.</summary>
    Task<PurchaseOrderDetail?> GetOrderAsync(Guid purchaseOrderId, CancellationToken cancellationToken = default);

    /// <summary>Loads one order by its number, the way somebody reads it off a delivery note.</summary>
    Task<PurchaseOrderDetail?> GetOrderByNumberAsync(string orderNumber, CancellationToken cancellationToken = default);

    /// <summary>Searches orders by number, supplier code or supplier reference.</summary>
    /// <param name="criteria">What to look for.</param>
    /// <param name="page">Which page to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<PagedResult<PurchaseOrderSummary>> SearchOrdersAsync(
        PurchaseOrderSearchCriteria criteria,
        PageRequest page,
        CancellationToken cancellationToken = default);

    /// <summary>Lists replenishment suggestions, newest shortfall first.</summary>
    /// <param name="criteria">What to look for.</param>
    /// <param name="page">Which page to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<PagedResult<ReplenishmentSuggestionDto>> ListSuggestionsAsync(
        SuggestionSearchCriteria criteria,
        PageRequest page,
        CancellationToken cancellationToken = default);
}

/// <summary>What to look for when searching purchase orders.</summary>
public sealed record PurchaseOrderSearchCriteria
{
    /// <summary>Free text, matched against order number, supplier code and supplier reference.</summary>
    public string? Term { get; init; }

    /// <summary>Restrict to one supplier.</summary>
    public Guid? SupplierId { get; init; }

    /// <summary>Restrict to orders delivering into one warehouse.</summary>
    public Guid? WarehouseId { get; init; }

    /// <summary>Restrict to one status.</summary>
    public string? Status { get; init; }

    /// <summary>
    /// True to return only orders with something still to come — the buyer's working list,
    /// and by far the most common way this screen is opened.
    /// </summary>
    public bool OutstandingOnly { get; init; }

    /// <summary>True to return only orders whose expected date has passed with goods outstanding.</summary>
    public bool OverdueOnly { get; init; }
}

/// <summary>What to look for when listing replenishment suggestions.</summary>
public sealed record SuggestionSearchCriteria
{
    /// <summary>Restrict to one warehouse.</summary>
    public Guid? WarehouseId { get; init; }

    /// <summary>Restrict to one part.</summary>
    public Guid? PartId { get; init; }

    /// <summary>Restrict to one status. Defaults to Open when not supplied.</summary>
    public string? Status { get; init; }
}
