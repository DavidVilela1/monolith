using AutoPartsErp.SharedKernel.Primitives;

namespace AutoPartsErp.Modules.Inventory.Domain.Counting.Events;

/// <summary>Raised when a count sheet is opened against a warehouse.</summary>
/// <param name="StockCountId">The sheet.</param>
/// <param name="Number">Its number.</param>
/// <param name="WarehouseId">The warehouse being counted.</param>
public sealed record StockCountOpenedDomainEvent(
    StockCountId StockCountId,
    string Number,
    WarehouseId WarehouseId) : DomainEvent;

/// <summary>
/// Raised when counting finishes and the sheet is waiting for somebody to accept it.
/// <para>
/// Carries how many lines were left uncounted, because that is the number a reviewer needs and
/// the one nobody thinks to ask for. A sheet with four hundred lines counted and sixty not is not
/// a finished count of the warehouse, however complete the totals look.
/// </para>
/// </summary>
/// <param name="StockCountId">The sheet.</param>
/// <param name="Number">Its number.</param>
/// <param name="WarehouseId">The warehouse counted.</param>
/// <param name="CountedLines">How many lines somebody went to.</param>
/// <param name="UncountedLines">How many nobody reached.</param>
public sealed record StockCountSubmittedDomainEvent(
    StockCountId StockCountId,
    string Number,
    WarehouseId WarehouseId,
    int CountedLines,
    int UncountedLines) : DomainEvent;

/// <summary>Raised when the differences on a sheet have been applied to stock.</summary>
/// <param name="StockCountId">The sheet.</param>
/// <param name="Number">Its number.</param>
/// <param name="WarehouseId">The warehouse counted.</param>
/// <param name="CountedLines">How many lines it covered.</param>
public sealed record StockCountPostedDomainEvent(
    StockCountId StockCountId,
    string Number,
    WarehouseId WarehouseId,
    int CountedLines) : DomainEvent;
