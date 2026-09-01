using AutoPartsErp.SharedKernel.Messaging;

namespace AutoPartsErp.IntegrationEvents.Purchasing;

/// <summary>
/// A purchase order was sent to a supplier. Inventory can show the quantity as on order, so the
/// counter stops seeing "none in stock" for a part that is already three days into a two-week
/// lead time; Finance can accrue the commitment.
/// </summary>
/// <param name="PurchaseOrderId">The order.</param>
/// <param name="OrderNumber">Its human-readable number, e.g. "PO-2026-00042".</param>
/// <param name="SupplierId">Who it went to.</param>
/// <param name="WarehouseId">Where the goods are expected.</param>
/// <param name="Total">The order value.</param>
/// <param name="CurrencyCode">Currency of the total.</param>
/// <param name="ExpectedOn">When the goods are expected, when the supplier has confirmed a date.</param>
/// <param name="TenantId">The owning tenant.</param>
public sealed record PurchaseOrderSubmittedIntegrationEvent(
    Guid PurchaseOrderId,
    string OrderNumber,
    Guid SupplierId,
    Guid WarehouseId,
    decimal Total,
    string CurrencyCode,
    DateOnly? ExpectedOn,
    Guid TenantId) : IntegrationEvent;

/// <summary>
/// Goods arrived against a purchase order line.
/// <para>
/// This is the event that puts stock on a shelf. Purchasing records what the delivery note said;
/// Inventory decides what that means for the balance. Purchasing does not — and must not — write
/// to the inventory schema itself, which is why the receipt travels as a fact rather than as a
/// call into another module's repository.
/// </para>
/// <para>
/// The unit code travels with it for the same reason Catalog sends one on activation: a consumer
/// that has to call back to the publisher to interpret the payload is not decoupled from it.
/// </para>
/// </summary>
/// <param name="PurchaseOrderId">The order received against.</param>
/// <param name="OrderNumber">Its human-readable number, used as the stock movement reference.</param>
/// <param name="PurchaseOrderLineId">The line received against.</param>
/// <param name="PartId">The part received.</param>
/// <param name="WarehouseId">Where it landed.</param>
/// <param name="Quantity">How much arrived on this receipt, not the running total.</param>
/// <param name="UnitCode">The unit the quantity is expressed in, e.g. EA, SET, L.</param>
/// <param name="UnitPrice">What we are paying per unit, for stock valuation.</param>
/// <param name="CurrencyCode">Currency of the unit price.</param>
/// <param name="TenantId">The owning tenant.</param>
public sealed record GoodsReceivedIntegrationEvent(
    Guid PurchaseOrderId,
    string OrderNumber,
    Guid PurchaseOrderLineId,
    Guid PartId,
    Guid WarehouseId,
    decimal Quantity,
    string UnitCode,
    decimal UnitPrice,
    string CurrencyCode,
    Guid TenantId) : IntegrationEvent;

/// <summary>
/// A purchase order was cancelled before it was fully received. Inventory drops the on-order
/// quantity; anything that was counting on those goods needs to know they are not coming.
/// </summary>
/// <param name="PurchaseOrderId">The order.</param>
/// <param name="OrderNumber">Its human-readable number.</param>
/// <param name="Reason">Why it was cancelled.</param>
/// <param name="TenantId">The owning tenant.</param>
public sealed record PurchaseOrderCancelledIntegrationEvent(
    Guid PurchaseOrderId,
    string OrderNumber,
    string Reason,
    Guid TenantId) : IntegrationEvent;
