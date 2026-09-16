using AutoPartsErp.SharedKernel.Primitives;

namespace AutoPartsErp.Modules.Inventory.Domain.Stock.Events;

/// <summary>Raised when a stock record is opened for a part in a warehouse.</summary>
/// <param name="StockItemId">The new balance record.</param>
/// <param name="Part">The part.</param>
/// <param name="WarehouseId">The warehouse.</param>
/// <param name="UnitCode">The unit quantities are counted in.</param>
public sealed record StockRecordOpenedDomainEvent(
    StockItemId StockItemId,
    PartRef Part,
    WarehouseId WarehouseId,
    string UnitCode) : DomainEvent;

/// <summary>Raised when stock is received.</summary>
/// <param name="StockItemId">The balance affected.</param>
/// <param name="Part">The part.</param>
/// <param name="WarehouseId">The warehouse.</param>
/// <param name="Quantity">How much came in.</param>
/// <param name="Reference">The document number behind it.</param>
/// <param name="ReferenceType">What kind of document it was, which is what tells a return from a
/// delivery. A consumer that cannot tell them apart would reverse a cost of sale on every
/// purchase.</param>
/// <param name="MovementId">
/// The movement in this module's own ledger. The identity of the fact: one document can move the
/// same part twice — an order dispatched in two vans — and anything keyed on the document number
/// alone would take the second for a repeat of the first and lose it.
/// </param>
/// <param name="Value">What joined the shelf, when the shelf carries a value.</param>
/// <param name="CurrencyCode">The currency that value is in.</param>
public sealed record StockReceivedDomainEvent(
    StockItemId StockItemId,
    PartRef Part,
    WarehouseId WarehouseId,
    decimal Quantity,
    string Reference,
    string ReferenceType,
    MovementId MovementId,
    decimal? Value,
    string CurrencyCode) : DomainEvent;

/// <summary>Raised when stock is issued.</summary>
/// <param name="StockItemId">The balance affected.</param>
/// <param name="Part">The part.</param>
/// <param name="WarehouseId">The warehouse.</param>
/// <param name="Quantity">How much went out, as a positive number.</param>
/// <param name="Reference">The document number behind it.</param>
/// <param name="ReferenceType">
/// What kind of document it was. Carried because "stock left" is not one fact: against a sales
/// order it is a cost of sale, and against a stock transfer it is a move between two shelves the
/// company still owns. A consumer that could not tell them apart would book a cost of sale every
/// time a van went to the other branch.
/// </param>
/// <param name="MovementId">
/// The movement in this module's own ledger. The identity of the fact: one document can move the
/// same part twice — an order dispatched in two vans — and anything keyed on the document number
/// alone would take the second for a repeat of the first and lose it.
/// </param>
/// <param name="CostValue">What left the shelf, when the shelf carries a value.</param>
/// <param name="CurrencyCode">The currency that value is in.</param>
public sealed record StockIssuedDomainEvent(
    StockItemId StockItemId,
    PartRef Part,
    WarehouseId WarehouseId,
    decimal Quantity,
    string Reference,
    string ReferenceType,
    MovementId MovementId,
    decimal? CostValue,
    string CurrencyCode) : DomainEvent;

/// <summary>Raised when a count corrects the balance.</summary>
/// <param name="StockItemId">The balance affected.</param>
/// <param name="Part">The part.</param>
/// <param name="WarehouseId">The warehouse.</param>
/// <param name="Delta">The signed difference applied.</param>
/// <param name="Reference">The count or adjustment document.</param>
/// <param name="MovementId">The movement in this module's own ledger: the identity of the fact.</param>
/// <param name="Value">
/// The signed difference in value, matching the direction of <paramref name="Delta"/>: negative
/// when a count found less than the shelf said. Null when the shelf carries no value.
/// </param>
/// <param name="CurrencyCode">The currency that value is in.</param>
public sealed record StockAdjustedDomainEvent(
    StockItemId StockItemId,
    PartRef Part,
    WarehouseId WarehouseId,
    decimal Delta,
    string Reference,
    MovementId MovementId,
    decimal? Value,
    string CurrencyCode) : DomainEvent;

/// <summary>
/// Raised when the value of stock changes without any of it moving.
/// <para>
/// The only fact in this module with no quantity on it. A supplier invoiced a delivery at a price
/// the receipt was not booked at, and what is on the shelf is suddenly worth more or less than it
/// was a moment ago — with nothing having arrived or left.
/// </para>
/// </summary>
/// <param name="StockItemId">The balance affected.</param>
/// <param name="Part">The part.</param>
/// <param name="WarehouseId">The warehouse.</param>
/// <param name="Difference">
/// Signed: positive when the supplier charged more than the receipt was booked at.
/// </param>
/// <param name="CurrencyCode">The currency.</param>
/// <param name="Reference">The document behind it.</param>
/// <param name="MovementId">The movement in this module's own ledger: the identity of the fact.</param>
public sealed record StockRevaluedDomainEvent(
    StockItemId StockItemId,
    PartRef Part,
    WarehouseId WarehouseId,
    decimal Difference,
    string CurrencyCode,
    string Reference,
    MovementId MovementId) : DomainEvent;

/// <summary>Raised when stock is held back for a document.</summary>
/// <param name="StockItemId">The balance affected.</param>
/// <param name="Part">The part.</param>
/// <param name="WarehouseId">The warehouse.</param>
/// <param name="Quantity">How much was held.</param>
/// <param name="Reference">What claimed it.</param>
public sealed record StockReservedDomainEvent(
    StockItemId StockItemId,
    PartRef Part,
    WarehouseId WarehouseId,
    decimal Quantity,
    string Reference) : DomainEvent;

/// <summary>Raised when a claim is given back.</summary>
/// <param name="StockItemId">The balance affected.</param>
/// <param name="ReservationId">The claim released.</param>
/// <param name="Quantity">How much returned to available.</param>
public sealed record StockReservationReleasedDomainEvent(
    StockItemId StockItemId,
    ReservationId ReservationId,
    decimal Quantity) : DomainEvent;

/// <summary>Raised when a claim lapses because nobody acted on it.</summary>
/// <param name="StockItemId">The balance affected.</param>
/// <param name="ReservationId">The claim that expired.</param>
/// <param name="Quantity">How much returned to available.</param>
public sealed record StockReservationExpiredDomainEvent(
    StockItemId StockItemId,
    ReservationId ReservationId,
    decimal Quantity) : DomainEvent;

/// <summary>
/// Raised when the projected position reaches the reorder point. Purchasing turns this into a
/// replenishment suggestion.
/// <para>
/// The trigger counts stock already on order, so an order placed last week suppresses this while
/// it is in transit. Both figures travel, because the buyer deciding how much to order needs to
/// see them apart: two on the shelf with twenty arriving on Thursday is a different decision from
/// two on the shelf with nothing coming, and a single netted number cannot tell them apart.
/// </para>
/// </summary>
/// <param name="StockItemId">The balance affected.</param>
/// <param name="Part">The part.</param>
/// <param name="WarehouseId">The warehouse.</param>
/// <param name="Available">What is on the shelf and not spoken for.</param>
/// <param name="OnOrder">What is on a purchase order and has not arrived.</param>
/// <param name="ReorderPoint">The level that triggered this.</param>
/// <param name="ReorderQuantity">The suggested order quantity.</param>
public sealed record StockFellBelowReorderPointDomainEvent(
    StockItemId StockItemId,
    PartRef Part,
    WarehouseId WarehouseId,
    decimal Available,
    decimal OnOrder,
    decimal ReorderPoint,
    decimal ReorderQuantity) : DomainEvent;
