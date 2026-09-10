namespace AutoPartsErp.ModuleContracts.Inventory;

/// <summary>
/// What Inventory will tell another module about what stock cost.
/// <para>
/// Separate from <see cref="IInventoryAvailability"/> rather than a field on
/// <see cref="StockAvailability"/>, and the separation is the point. Availability is asked on
/// every counter screen by anybody serving a customer; cost is a figure a distributor does not
/// put in front of everyone who can look up a part. Two questions, two audiences, two interfaces
/// — and the day this needs its own permission, there is something to put one on.
/// </para>
/// <para>
/// It answers what the shelf averages <i>today</i>. That is the right number for deciding whether
/// a price is worth taking, and it is not the cost of any particular sale: what a dispatch
/// actually took off the balance is stamped on the ledger row at the moment it happens, and the
/// two will differ whenever the shelf moves in between. Anything reconciling margin to the
/// accounts wants the ledger; anything deciding a price wants this.
/// </para>
/// </summary>
public interface IInventoryCosting
{
    /// <summary>
    /// What one unit of a part is worth on a shelf, or null when Inventory cannot say.
    /// <para>
    /// Null covers two real cases that are not errors: a part with no stock record in that
    /// warehouse, and one whose shelf is empty or has never been through a priced receipt. Null
    /// is not zero — a part whose cost is unknown has no margin, and reporting one of a hundred
    /// per cent would be worse than reporting none.
    /// </para>
    /// </summary>
    /// <param name="partId">The part.</param>
    /// <param name="warehouseId">The warehouse the goods would come out of.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<StockUnitCost?> GetUnitCostAsync(
        Guid partId,
        Guid warehouseId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The same question for several parts at once, for a screen costing a whole order.
    /// </summary>
    /// <param name="partIds">The parts to ask about.</param>
    /// <param name="warehouseId">The warehouse.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One entry per part Inventory can cost; parts it cannot are absent.</returns>
    Task<IReadOnlyDictionary<Guid, StockUnitCost>> GetUnitCostsAsync(
        IReadOnlyCollection<Guid> partIds,
        Guid warehouseId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// What a unit of stock is worth, flattened.
/// </summary>
/// <param name="PartId">The part.</param>
/// <param name="WarehouseId">The warehouse.</param>
/// <param name="UnitCost">
/// The shelf's value divided by what is on it. A derived figure, rounded for display — the value
/// is what Inventory stores, for the reason its own ledger explains at length.
/// </param>
/// <param name="CurrencyCode">The currency the cost is in.</param>
public sealed record StockUnitCost(
    Guid PartId,
    Guid WarehouseId,
    decimal UnitCost,
    string CurrencyCode);
