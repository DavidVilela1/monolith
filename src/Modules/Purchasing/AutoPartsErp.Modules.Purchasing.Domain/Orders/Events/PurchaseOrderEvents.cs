using AutoPartsErp.SharedKernel.Primitives;

namespace AutoPartsErp.Modules.Purchasing.Domain.Orders.Events;

/// <summary>Raised when a draft order is started.</summary>
/// <param name="PurchaseOrderId">The new order.</param>
/// <param name="OrderNumber">Its human-readable number.</param>
/// <param name="SupplierId">Who it is for.</param>
public sealed record PurchaseOrderDraftedDomainEvent(
    PurchaseOrderId PurchaseOrderId,
    string OrderNumber,
    SupplierRef SupplierId) : DomainEvent;

/// <summary>
/// Raised when the order goes to the supplier. This is the point of commitment: before it, the
/// document is a shopping list; after it, somebody is expecting to be paid.
/// </summary>
/// <param name="PurchaseOrderId">The order.</param>
/// <param name="OrderNumber">Its human-readable number.</param>
/// <param name="SupplierId">Who it went to.</param>
/// <param name="WarehouseId">Where the goods are expected.</param>
/// <param name="Total">The order value.</param>
/// <param name="CurrencyCode">Currency of the total.</param>
/// <param name="ExpectedOn">The expected delivery date, when one is known.</param>
public sealed record PurchaseOrderSubmittedDomainEvent(
    PurchaseOrderId PurchaseOrderId,
    string OrderNumber,
    SupplierRef SupplierId,
    WarehouseRef WarehouseId,
    decimal Total,
    string CurrencyCode,
    DateOnly? ExpectedOn) : DomainEvent;

/// <summary>Raised when the supplier acknowledges the order and commits to a date.</summary>
/// <param name="PurchaseOrderId">The order.</param>
/// <param name="OrderNumber">Its human-readable number.</param>
/// <param name="ExpectedOn">The date they promised.</param>
/// <param name="SupplierReference">Their own order number, for when you have to ring them about it.</param>
public sealed record PurchaseOrderConfirmedDomainEvent(
    PurchaseOrderId PurchaseOrderId,
    string OrderNumber,
    DateOnly ExpectedOn,
    string? SupplierReference) : DomainEvent;

/// <summary>
/// Raised when goods arrive against a line. Carries the quantity on this receipt, not the
/// running total, because that is what Inventory has to add to the shelf.
/// </summary>
/// <param name="PurchaseOrderId">The order received against.</param>
/// <param name="OrderNumber">Its human-readable number, used as the stock movement reference.</param>
/// <param name="LineId">The line received against.</param>
/// <param name="PartId">The part received.</param>
/// <param name="WarehouseId">Where it landed.</param>
/// <param name="Quantity">How much arrived on this receipt.</param>
/// <param name="UnitCode">The unit the quantity is expressed in.</param>
/// <param name="UnitPrice">What we are paying per unit.</param>
/// <param name="CurrencyCode">Currency of the unit price.</param>
public sealed record GoodsReceivedDomainEvent(
    PurchaseOrderId PurchaseOrderId,
    string OrderNumber,
    PurchaseOrderLineId LineId,
    PartRef PartId,
    WarehouseRef WarehouseId,
    decimal Quantity,
    string UnitCode,
    decimal UnitPrice,
    string CurrencyCode) : DomainEvent;

/// <summary>Raised when the last outstanding line is satisfied.</summary>
/// <param name="PurchaseOrderId">The order.</param>
/// <param name="OrderNumber">Its human-readable number.</param>
public sealed record PurchaseOrderCompletedDomainEvent(
    PurchaseOrderId PurchaseOrderId,
    string OrderNumber) : DomainEvent;

/// <summary>Raised when an order is cancelled before anything arrived.</summary>
/// <param name="PurchaseOrderId">The order.</param>
/// <param name="OrderNumber">Its human-readable number.</param>
/// <param name="Reason">Why it was cancelled.</param>
public sealed record PurchaseOrderCancelledDomainEvent(
    PurchaseOrderId PurchaseOrderId,
    string OrderNumber,
    string Reason) : DomainEvent;

/// <summary>
/// Raised when an order is closed with lines still outstanding — the short delivery that both
/// sides have agreed to stop chasing.
/// </summary>
/// <param name="PurchaseOrderId">The order.</param>
/// <param name="OrderNumber">Its human-readable number.</param>
/// <param name="Reason">Why the shortfall was accepted.</param>
public sealed record PurchaseOrderClosedShortDomainEvent(
    PurchaseOrderId PurchaseOrderId,
    string OrderNumber,
    string Reason) : DomainEvent;
