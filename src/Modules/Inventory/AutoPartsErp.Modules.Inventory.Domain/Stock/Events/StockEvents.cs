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
public sealed record StockReceivedDomainEvent(
    StockItemId StockItemId,
    PartRef Part,
    WarehouseId WarehouseId,
    decimal Quantity,
    string Reference) : DomainEvent;

/// <summary>Raised when stock is issued.</summary>
/// <param name="StockItemId">The balance affected.</param>
/// <param name="Part">The part.</param>
/// <param name="WarehouseId">The warehouse.</param>
/// <param name="Quantity">How much went out, as a positive number.</param>
/// <param name="Reference">The document number behind it.</param>
public sealed record StockIssuedDomainEvent(
    StockItemId StockItemId,
    PartRef Part,
    WarehouseId WarehouseId,
    decimal Quantity,
    string Reference) : DomainEvent;

/// <summary>Raised when a count corrects the balance.</summary>
/// <param name="StockItemId">The balance affected.</param>
/// <param name="Part">The part.</param>
/// <param name="WarehouseId">The warehouse.</param>
/// <param name="Delta">The signed difference applied.</param>
/// <param name="Reference">The count or adjustment document.</param>
public sealed record StockAdjustedDomainEvent(
    StockItemId StockItemId,
    PartRef Part,
    WarehouseId WarehouseId,
    decimal Delta,
    string Reference) : DomainEvent;

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
/// Raised when available stock reaches the reorder point. Purchasing turns this into a
/// replenishment suggestion.
/// </summary>
/// <param name="StockItemId">The balance affected.</param>
/// <param name="Part">The part.</param>
/// <param name="WarehouseId">The warehouse.</param>
/// <param name="Available">What is left that is not spoken for.</param>
/// <param name="ReorderPoint">The level that triggered this.</param>
/// <param name="ReorderQuantity">The suggested order quantity.</param>
public sealed record StockFellBelowReorderPointDomainEvent(
    StockItemId StockItemId,
    PartRef Part,
    WarehouseId WarehouseId,
    decimal Available,
    decimal ReorderPoint,
    decimal ReorderQuantity) : DomainEvent;
