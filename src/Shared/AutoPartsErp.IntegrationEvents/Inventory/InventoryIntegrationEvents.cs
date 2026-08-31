using AutoPartsErp.SharedKernel.Messaging;

namespace AutoPartsErp.IntegrationEvents.Inventory;

/// <summary>
/// Available stock for a part in a warehouse dropped to or below its reorder point.
/// Purchasing listens for this to raise a replenishment suggestion.
/// </summary>
/// <param name="PartId">The part.</param>
/// <param name="WarehouseId">Where it ran low.</param>
/// <param name="QuantityAvailable">What is left that is not already spoken for.</param>
/// <param name="ReorderPoint">The level that triggered this.</param>
/// <param name="ReorderQuantity">The suggested order quantity.</param>
/// <param name="TenantId">The owning tenant.</param>
public sealed record StockFellBelowReorderPointIntegrationEvent(
    Guid PartId,
    Guid WarehouseId,
    decimal QuantityAvailable,
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
