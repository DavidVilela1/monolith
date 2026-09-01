namespace AutoPartsErp.Modules.Purchasing.Application.Contracts;

/// <summary>One row in a purchase order list.</summary>
/// <param name="Id">The order.</param>
/// <param name="OrderNumber">Its human-readable number.</param>
/// <param name="SupplierId">Who it is for.</param>
/// <param name="SupplierCode">Their short code, as it was when the order was raised.</param>
/// <param name="Status">Draft, Submitted, Confirmed, PartiallyReceived, Received, Cancelled or ClosedShort.</param>
/// <param name="OrderedOn">The day it went out.</param>
/// <param name="ExpectedOn">The day the goods are expected.</param>
/// <param name="Total">The order value.</param>
/// <param name="OutstandingValue">The value still to be delivered.</param>
/// <param name="CurrencyCode">Currency of both amounts.</param>
/// <param name="LineCount">How many lines it has.</param>
/// <param name="IsOverdue">True when the expected date has passed and something is still outstanding.</param>
public sealed record PurchaseOrderSummary(
    Guid Id,
    string OrderNumber,
    Guid SupplierId,
    string SupplierCode,
    string Status,
    DateOnly? OrderedOn,
    DateOnly? ExpectedOn,
    decimal Total,
    decimal OutstandingValue,
    string CurrencyCode,
    int LineCount,
    bool IsOverdue);

/// <summary>The full picture of one purchase order.</summary>
public sealed record PurchaseOrderDetail
{
    /// <summary>The order.</summary>
    public required Guid Id { get; init; }

    /// <summary>Its human-readable number.</summary>
    public required string OrderNumber { get; init; }

    /// <summary>Who it is for.</summary>
    public required Guid SupplierId { get; init; }

    /// <summary>Their short code, as it was when the order was raised.</summary>
    public required string SupplierCode { get; init; }

    /// <summary>Where the goods are to be delivered.</summary>
    public required Guid DeliverToWarehouseId { get; init; }

    /// <summary>Draft, Submitted, Confirmed, PartiallyReceived, Received, Cancelled or ClosedShort.</summary>
    public required string Status { get; init; }

    /// <summary>Currency the order is priced in.</summary>
    public required string CurrencyCode { get; init; }

    /// <summary>The day it went out.</summary>
    public DateOnly? OrderedOn { get; init; }

    /// <summary>The day the goods are expected.</summary>
    public DateOnly? ExpectedOn { get; init; }

    /// <summary>Their own order number.</summary>
    public string? SupplierReference { get; init; }

    /// <summary>Anything the buyer recorded against it.</summary>
    public string? Notes { get; init; }

    /// <summary>Why it was cancelled or closed short.</summary>
    public string? ClosureReason { get; init; }

    /// <summary>The order value.</summary>
    public required decimal Total { get; init; }

    /// <summary>The value still to be delivered.</summary>
    public required decimal OutstandingValue { get; init; }

    /// <summary>True while the order can still be changed.</summary>
    public required bool IsEditable { get; init; }

    /// <summary>True when goods may be booked in against it.</summary>
    public required bool CanReceive { get; init; }

    /// <summary>Its lines.</summary>
    public required IReadOnlyList<PurchaseOrderLineDto> Lines { get; init; }
}

/// <summary>One line on a purchase order.</summary>
/// <param name="Id">The line.</param>
/// <param name="PartId">The part being bought.</param>
/// <param name="Sku">Its SKU, as it was when the order was raised.</param>
/// <param name="Description">Its description, as it was when the order was raised.</param>
/// <param name="Quantity">How much was ordered.</param>
/// <param name="ReceivedQuantity">How much has arrived so far.</param>
/// <param name="OutstandingQuantity">What is still to come.</param>
/// <param name="UnitCode">The unit all three quantities are in.</param>
/// <param name="UnitPrice">The agreed price per unit.</param>
/// <param name="LineTotal">Unit price times quantity ordered.</param>
/// <param name="IsFullyReceived">True once everything ordered has arrived.</param>
public sealed record PurchaseOrderLineDto(
    Guid Id,
    Guid PartId,
    string Sku,
    string Description,
    decimal Quantity,
    decimal ReceivedQuantity,
    decimal OutstandingQuantity,
    string UnitCode,
    decimal UnitPrice,
    decimal LineTotal,
    bool IsFullyReceived);

/// <summary>A part that has run low somewhere and probably needs buying.</summary>
/// <param name="Id">The suggestion.</param>
/// <param name="PartId">The part that ran low.</param>
/// <param name="WarehouseId">Where it ran low.</param>
/// <param name="QuantityAvailable">What is left that is not already spoken for.</param>
/// <param name="ReorderPoint">The level that triggered it.</param>
/// <param name="SuggestedQuantity">How much to order.</param>
/// <param name="Shortfall">How far below the trigger level it has fallen.</param>
/// <param name="Status">Open, Ordered or Dismissed.</param>
/// <param name="RaisedAtUtc">When it first appeared.</param>
/// <param name="LastSeenAtUtc">When it was last confirmed to still be true.</param>
/// <param name="PurchaseOrderId">The order it was rolled into, once it was.</param>
/// <param name="DismissedReason">Why the buyer decided not to act on it.</param>
public sealed record ReplenishmentSuggestionDto(
    Guid Id,
    Guid PartId,
    Guid WarehouseId,
    decimal QuantityAvailable,
    decimal ReorderPoint,
    decimal SuggestedQuantity,
    decimal Shortfall,
    string Status,
    DateTimeOffset RaisedAtUtc,
    DateTimeOffset LastSeenAtUtc,
    Guid? PurchaseOrderId,
    string? DismissedReason);
