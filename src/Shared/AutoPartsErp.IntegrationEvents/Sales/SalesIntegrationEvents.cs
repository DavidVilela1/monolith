using AutoPartsErp.SharedKernel.Messaging;

namespace AutoPartsErp.IntegrationEvents.Sales;

/// <summary>
/// An order was agreed with a customer. Finance recognises the commitment; anything reporting on
/// demand has its first real signal.
/// </summary>
/// <param name="SalesOrderId">The order.</param>
/// <param name="OrderNumber">Its human-readable number, e.g. "SO-2026-01188".</param>
/// <param name="CustomerId">Who it is for.</param>
/// <param name="WarehouseId">Where the goods come from.</param>
/// <param name="NetTotal">The value before VAT.</param>
/// <param name="VatTotal">The VAT.</param>
/// <param name="GrossTotal">What they will be invoiced.</param>
/// <param name="CurrencyCode">Currency of all three.</param>
/// <param name="RequiredBy">When the customer wants it.</param>
/// <param name="TenantId">The owning tenant.</param>
public sealed record SalesOrderConfirmedIntegrationEvent(
    Guid SalesOrderId,
    string OrderNumber,
    Guid CustomerId,
    Guid WarehouseId,
    decimal NetTotal,
    decimal VatTotal,
    decimal GrossTotal,
    string CurrencyCode,
    DateOnly? RequiredBy,
    Guid TenantId) : IntegrationEvent;

/// <summary>
/// A confirmed order line needs stock held back for it.
/// <para>
/// One per line, so a part with no stock record fails and retries on its own instead of taking
/// the rest of the order with it. Inventory already has everything needed to act on this: it
/// reserves against the order number, and the sweeper that returns lapsed reservations has been
/// waiting for a producer since the module was written.
/// </para>
/// </summary>
/// <param name="SalesOrderId">The order.</param>
/// <param name="OrderNumber">Its number, which becomes the reservation's reference.</param>
/// <param name="SalesOrderLineId">The line.</param>
/// <param name="PartId">The part to hold.</param>
/// <param name="WarehouseId">Where to hold it.</param>
/// <param name="Quantity">How much.</param>
/// <param name="UnitCode">The unit the quantity is expressed in.</param>
/// <param name="TenantId">The owning tenant.</param>
public sealed record StockReservationRequestedIntegrationEvent(
    Guid SalesOrderId,
    string OrderNumber,
    Guid SalesOrderLineId,
    Guid PartId,
    Guid WarehouseId,
    decimal Quantity,
    string UnitCode,
    Guid TenantId) : IntegrationEvent;

/// <summary>
/// Goods left the building against a sales order line. Inventory takes them off the balance and
/// writes the ledger entry; Finance recognises the cost of sale.
/// </summary>
/// <param name="SalesOrderId">The order.</param>
/// <param name="OrderNumber">Its number, which becomes the stock movement reference.</param>
/// <param name="SalesOrderLineId">The line.</param>
/// <param name="PartId">The part that went out.</param>
/// <param name="WarehouseId">Where it left from.</param>
/// <param name="Quantity">How much went on this dispatch, not the running total.</param>
/// <param name="UnitCode">The unit the quantity is expressed in.</param>
/// <param name="TenantId">The owning tenant.</param>
public sealed record GoodsDispatchedIntegrationEvent(
    Guid SalesOrderId,
    string OrderNumber,
    Guid SalesOrderLineId,
    Guid PartId,
    Guid WarehouseId,
    decimal Quantity,
    string UnitCode,
    Guid TenantId) : IntegrationEvent;

/// <summary>
/// An order was called off before anything went out. Inventory gives back whatever it was
/// holding for it.
/// </summary>
/// <param name="SalesOrderId">The order.</param>
/// <param name="OrderNumber">Its number, which identifies the reservations to release.</param>
/// <param name="CustomerId">Who it was for.</param>
/// <param name="Reason">Why.</param>
/// <param name="TenantId">The owning tenant.</param>
public sealed record SalesOrderCancelledIntegrationEvent(
    Guid SalesOrderId,
    string OrderNumber,
    Guid CustomerId,
    string Reason,
    Guid TenantId) : IntegrationEvent;
