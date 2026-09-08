using AutoPartsErp.SharedKernel.Primitives;

namespace AutoPartsErp.Modules.Inventory.Domain.Transfers.Events;

/// <summary>Raised when the goods leave the sending warehouse.</summary>
/// <param name="StockTransferId">The transfer.</param>
/// <param name="Number">Its number.</param>
/// <param name="FromWarehouseId">Where the stock left.</param>
/// <param name="ToWarehouseId">Where it is going.</param>
/// <param name="LineCount">How many parts are on the van.</param>
public sealed record StockTransferDispatchedDomainEvent(
    StockTransferId StockTransferId,
    string Number,
    WarehouseId FromWarehouseId,
    WarehouseId ToWarehouseId,
    int LineCount) : DomainEvent;

/// <summary>Raised when the last of a transfer is booked in.</summary>
/// <param name="StockTransferId">The transfer.</param>
/// <param name="Number">Its number.</param>
/// <param name="FromWarehouseId">Where the stock left.</param>
/// <param name="ToWarehouseId">Where it arrived.</param>
public sealed record StockTransferReceivedDomainEvent(
    StockTransferId StockTransferId,
    string Number,
    WarehouseId FromWarehouseId,
    WarehouseId ToWarehouseId) : DomainEvent;

/// <summary>
/// Raised when a shortfall in transit is accepted and written off.
/// <para>
/// The value travels on it because this is the one event in the module that reports a real loss
/// rather than a movement. Whatever eventually posts shrinkage to a general ledger will read this,
/// and it should not have to reconstruct the figure by pairing movements.
/// </para>
/// </summary>
/// <param name="StockTransferId">The transfer.</param>
/// <param name="Number">Its number.</param>
/// <param name="FromWarehouseId">Where the stock left.</param>
/// <param name="ToWarehouseId">Where it never arrived.</param>
/// <param name="Reason">Why the shortfall was accepted.</param>
/// <param name="LostValue">What the goods that never arrived were worth.</param>
/// <param name="CurrencyCode">The currency of that value.</param>
public sealed record StockTransferClosedShortDomainEvent(
    StockTransferId StockTransferId,
    string Number,
    WarehouseId FromWarehouseId,
    WarehouseId ToWarehouseId,
    string Reason,
    decimal LostValue,
    string CurrencyCode) : DomainEvent;
