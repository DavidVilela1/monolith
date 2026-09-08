using AutoPartsErp.SharedKernel.Messaging;

namespace AutoPartsErp.IntegrationEvents.Inventory;

/// <summary>
/// The projected position for a part in a warehouse dropped to or below its reorder point.
/// Purchasing listens for this to raise a replenishment suggestion.
/// <para>
/// Projected, not available: what is already on a purchase order counts towards the trigger.
/// Without that, an order with a two-week lead time produces this signal every day until it
/// arrives, the buyer sees the same line every morning, and the list stops being read — which is
/// worse than having no list, because it also hides the parts that genuinely need ordering.
/// </para>
/// </summary>
/// <param name="PartId">The part.</param>
/// <param name="WarehouseId">Where it ran low.</param>
/// <param name="QuantityAvailable">What is on the shelf and not already spoken for.</param>
/// <param name="QuantityOnOrder">What is on a purchase order and has not arrived yet.</param>
/// <param name="ReorderPoint">The level that triggered this.</param>
/// <param name="ReorderQuantity">The suggested order quantity.</param>
/// <param name="TenantId">The owning tenant.</param>
public sealed record StockFellBelowReorderPointIntegrationEvent(
    Guid PartId,
    Guid WarehouseId,
    decimal QuantityAvailable,
    decimal QuantityOnOrder,
    decimal ReorderPoint,
    decimal ReorderQuantity,
    Guid TenantId) : IntegrationEvent;

/// <summary>
/// Stock was physically received into a warehouse. Purchasing matches it against the
/// purchase order; Finance values it.
/// </summary>
/// <param name="PartId">The part received.</param>
/// <param name="WarehouseId">Where it landed.</param>
/// <param name="Quantity">How much.</param>
/// <param name="Reference">The document that caused it, e.g. "GRN-2026-00042".</param>
/// <param name="TenantId">The owning tenant.</param>
public sealed record StockReceivedIntegrationEvent(
    Guid PartId,
    Guid WarehouseId,
    decimal Quantity,
    string Reference,
    Guid TenantId) : IntegrationEvent;

/// <summary>
/// Stock left a warehouse. Sales marks the line as picked; Finance recognises cost of sale.
/// </summary>
/// <param name="PartId">The part issued.</param>
/// <param name="WarehouseId">Where it left from.</param>
/// <param name="Quantity">How much.</param>
/// <param name="Reference">The document that caused it, e.g. "SO-2026-01188".</param>
/// <param name="TenantId">The owning tenant.</param>
public sealed record StockIssuedIntegrationEvent(
    Guid PartId,
    Guid WarehouseId,
    decimal Quantity,
    string Reference,
    Guid TenantId) : IntegrationEvent;
