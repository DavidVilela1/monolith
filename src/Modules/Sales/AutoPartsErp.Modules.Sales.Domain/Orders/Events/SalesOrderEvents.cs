using AutoPartsErp.SharedKernel.Primitives;

namespace AutoPartsErp.Modules.Sales.Domain.Orders.Events;

/// <summary>Raised when an order is started.</summary>
/// <param name="SalesOrderId">The new order.</param>
/// <param name="OrderNumber">Its human-readable number.</param>
/// <param name="CustomerId">Who it is for.</param>
public sealed record SalesOrderDraftedDomainEvent(
    SalesOrderId SalesOrderId,
    string OrderNumber,
    CustomerRef CustomerId) : DomainEvent;

/// <summary>
/// Raised when an order is confirmed: the customer has been quoted a figure and the stock is
/// now spoken for.
/// </summary>
/// <param name="SalesOrderId">The order.</param>
/// <param name="OrderNumber">Its human-readable number.</param>
/// <param name="CustomerId">Who it is for.</param>
/// <param name="WarehouseId">Where the goods come from.</param>
/// <param name="NetTotal">The value before VAT.</param>
/// <param name="VatTotal">The VAT.</param>
/// <param name="GrossTotal">What they will be invoiced.</param>
/// <param name="CurrencyCode">Currency of all three.</param>
/// <param name="RequiredBy">When the customer wants it.</param>
public sealed record SalesOrderConfirmedDomainEvent(
    SalesOrderId SalesOrderId,
    string OrderNumber,
    CustomerRef CustomerId,
    WarehouseRef WarehouseId,
    decimal NetTotal,
    decimal VatTotal,
    decimal GrossTotal,
    string CurrencyCode,
    DateOnly? RequiredBy) : DomainEvent;

/// <summary>
/// Raised per line when an order is confirmed, so stock can be held back for it.
/// <para>
/// One event per line rather than one carrying them all. With an outbox behind it that matters:
/// a line whose part has no stock record fails and retries on its own, instead of taking the
/// other five lines of the order down with it every time.
/// </para>
/// </summary>
/// <param name="SalesOrderId">The order.</param>
/// <param name="OrderNumber">Its number, which becomes the stock movement reference.</param>
/// <param name="LineId">The line.</param>
/// <param name="PartId">The part to hold.</param>
/// <param name="WarehouseId">Where to hold it.</param>
/// <param name="Quantity">How much.</param>
/// <param name="UnitCode">The unit the quantity is in.</param>
/// <param name="AllowPartial">
/// True when the order was confirmed as a back-order, so Inventory should hold whatever it can
/// rather than refusing. Without this, a deliberate back-order would fail its reservation ten
/// times and dead-letter — a 204 at the counter and a broken row nobody looks at.
/// </param>
public sealed record StockReservationRequestedDomainEvent(
    SalesOrderId SalesOrderId,
    string OrderNumber,
    SalesOrderLineId LineId,
    PartRef PartId,
    WarehouseRef WarehouseId,
    decimal Quantity,
    string UnitCode,
    bool AllowPartial) : DomainEvent;

/// <summary>Raised when goods leave against a line.</summary>
/// <param name="SalesOrderId">The order.</param>
/// <param name="OrderNumber">Its number, which becomes the stock movement reference.</param>
/// <param name="LineId">The line.</param>
/// <param name="PartId">The part that went out.</param>
/// <param name="WarehouseId">Where it left from.</param>
/// <param name="Quantity">How much went on this dispatch, not the running total.</param>
/// <param name="UnitCode">The unit the quantity is in.</param>
public sealed record GoodsDispatchedDomainEvent(
    SalesOrderId SalesOrderId,
    string OrderNumber,
    SalesOrderLineId LineId,
    PartRef PartId,
    WarehouseRef WarehouseId,
    decimal Quantity,
    string UnitCode) : DomainEvent;

/// <summary>Raised when the last outstanding line goes out.</summary>
/// <param name="SalesOrderId">The order.</param>
/// <param name="OrderNumber">Its human-readable number.</param>
/// <param name="CustomerId">Who it was for.</param>
/// <param name="GrossTotal">What they will be invoiced.</param>
/// <param name="CurrencyCode">Currency of the total.</param>
public sealed record SalesOrderCompletedDomainEvent(
    SalesOrderId SalesOrderId,
    string OrderNumber,
    CustomerRef CustomerId,
    decimal GrossTotal,
    string CurrencyCode) : DomainEvent;

/// <summary>Raised when an order is called off before anything went out.</summary>
/// <param name="SalesOrderId">The order.</param>
/// <param name="OrderNumber">Its human-readable number.</param>
/// <param name="CustomerId">Who it was for.</param>
/// <param name="Reason">Why.</param>
public sealed record SalesOrderCancelledDomainEvent(
    SalesOrderId SalesOrderId,
    string OrderNumber,
    CustomerRef CustomerId,
    string Reason) : DomainEvent;
