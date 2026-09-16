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
/// <param name="ReferenceType">
/// What kind of document it was, which is what tells a customer's return from a supplier's
/// delivery. A consumer that could not tell them apart would reverse a cost of sale every time
/// goods arrived from a supplier.
/// </param>
/// <param name="MovementId">
/// The movement in Inventory's own ledger. The identity of the fact: one document can move the
/// same part twice — an order dispatched in two vans — and anything keyed on the document number
/// alone would take the second for a repeat of the first and lose it.
/// </param>
/// <param name="Value">What joined the shelf, when the shelf carries a value.</param>
/// <param name="CurrencyCode">The currency that value is in.</param>
public sealed record StockReceivedIntegrationEvent(
    Guid PartId,
    Guid WarehouseId,
    decimal Quantity,
    string Reference,
    string ReferenceType,
    Guid MovementId,
    decimal? Value,
    string CurrencyCode,
    Guid TenantId) : IntegrationEvent;

/// <summary>
/// Stock left a warehouse. Sales marks the line as picked; Finance recognises cost of sale.
/// </summary>
/// <param name="PartId">The part issued.</param>
/// <param name="WarehouseId">Where it left from.</param>
/// <param name="Quantity">How much.</param>
/// <param name="Reference">The document that caused it, e.g. "SO-2026-01188".</param>
/// <param name="TenantId">The owning tenant.</param>
/// <param name="ReferenceType">
/// What kind of document it was. "Stock left" is not one fact: against a sales order it is a cost
/// of sale, and against a stock transfer it is a move between two shelves the company still owns.
/// Without this a consumer would book a cost of sale every time a van went to the other branch.
/// </param>
/// <param name="MovementId">
/// The movement in Inventory's own ledger. The identity of the fact: one document can move the
/// same part twice — an order dispatched in two vans — and anything keyed on the document number
/// alone would take the second for a repeat of the first and lose it.
/// </param>
/// <param name="CostValue">What left the shelf, at what it had cost, when the shelf carries a value.</param>
/// <param name="CurrencyCode">The currency that value is in.</param>
public sealed record StockIssuedIntegrationEvent(
    Guid PartId,
    Guid WarehouseId,
    decimal Quantity,
    string Reference,
    string ReferenceType,
    Guid MovementId,
    decimal? CostValue,
    string CurrencyCode,
    Guid TenantId) : IntegrationEvent;

/// <summary>
/// A count corrected the balance, and the value of the shelf moved with it. Finance posts the
/// difference.
/// </summary>
/// <param name="PartId">The part.</param>
/// <param name="WarehouseId">The warehouse.</param>
/// <param name="Delta">The signed difference in quantity.</param>
/// <param name="Value">
/// The signed difference in value, matching the delta: negative when a count found less than the
/// shelf said. Null when the shelf carries no value.
/// </param>
/// <param name="CurrencyCode">The currency.</param>
/// <param name="MovementId">The movement in Inventory's own ledger: the identity of the fact.</param>
/// <param name="Reference">The count document.</param>
/// <param name="TenantId">The owning tenant.</param>
public sealed record StockAdjustedIntegrationEvent(
    Guid PartId,
    Guid WarehouseId,
    decimal Delta,
    decimal? Value,
    string CurrencyCode,
    string Reference,
    Guid MovementId,
    Guid TenantId) : IntegrationEvent;

/// <summary>
/// The value of stock changed without any of it moving. Finance posts the difference.
/// <para>
/// The only fact this module publishes with no quantity on it: a supplier invoiced a delivery at
/// a price the receipt was not booked at, so what is on the shelf is suddenly worth more or less
/// than it was, with nothing having arrived or left.
/// </para>
/// </summary>
/// <param name="PartId">The part.</param>
/// <param name="WarehouseId">The warehouse.</param>
/// <param name="Difference">
/// Signed: positive when the supplier charged more than the receipt was booked at.
/// </param>
/// <param name="CurrencyCode">The currency.</param>
/// <param name="Reference">The document behind it.</param>
/// <param name="MovementId">The movement in Inventory's own ledger: the identity of the fact.</param>
/// <param name="TenantId">The owning tenant.</param>
public sealed record StockRevaluedIntegrationEvent(
    Guid PartId,
    Guid WarehouseId,
    decimal Difference,
    string CurrencyCode,
    string Reference,
    Guid MovementId,
    Guid TenantId) : IntegrationEvent;

/// <summary>
/// A transfer arrived short and the difference was written off. Finance posts the shrinkage.
/// </summary>
/// <param name="StockTransferId">The transfer.</param>
/// <param name="Number">Our number for it.</param>
/// <param name="FromWarehouseId">Where it left.</param>
/// <param name="ToWarehouseId">Where it was going.</param>
/// <param name="Reason">What was said about it.</param>
/// <param name="LostValue">What the company stopped owning, positive.</param>
/// <param name="CurrencyCode">The currency.</param>
/// <param name="TenantId">The owning tenant.</param>
public sealed record StockTransferClosedShortIntegrationEvent(
    Guid StockTransferId,
    string Number,
    Guid FromWarehouseId,
    Guid ToWarehouseId,
    string Reason,
    decimal LostValue,
    string CurrencyCode,
    Guid TenantId) : IntegrationEvent;
