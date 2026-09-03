namespace AutoPartsErp.ModuleContracts.Inventory;

/// <summary>
/// What Inventory will tell another module about a stock balance, on demand.
/// <para>
/// Every other cross-module link in this system is an event: a fact one module announces and
/// others react to, eventually. That is right for anything that has happened. It is wrong for
/// "is there enough on the shelf right now?", because the answer has to arrive before the
/// decision, not after it — and a customer standing at a counter is not going to wait for a
/// background sweep.
/// </para>
/// <para>
/// So this is a synchronous call, and it costs something: the caller is coupled to Inventory
/// being reachable. That is the honest price. What it is not is coupled to Inventory's
/// <i>internals</i> — no schema, no aggregate, no repository, just this interface and the flat
/// record below. If Inventory moves out to its own service, this becomes an HTTP call behind
/// the same interface and nothing that consumes it changes.
/// </para>
/// </summary>
public interface IInventoryAvailability
{
    /// <summary>
    /// What can still be promised for a part in a warehouse, or null when Inventory has no
    /// record of that combination — which usually means the part was never activated.
    /// </summary>
    /// <param name="partId">The part.</param>
    /// <param name="warehouseId">The warehouse.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<StockAvailability?> GetAsync(
        Guid partId,
        Guid warehouseId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The same question for several parts at once.
    /// <para>
    /// One round trip rather than one per line. A ten-line order asking ten times is the kind of
    /// thing that looks fine on a laptop and falls over on a counter.
    /// </para>
    /// </summary>
    /// <param name="partIds">The parts to ask about.</param>
    /// <param name="warehouseId">The warehouse.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One entry per part that Inventory knows about; parts it does not are absent.</returns>
    Task<IReadOnlyDictionary<Guid, StockAvailability>> GetManyAsync(
        IReadOnlyCollection<Guid> partIds,
        Guid warehouseId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A stock balance, flattened.
/// <para>
/// <paramref name="Available"/> is the number that matters to a caller: on hand less whatever is
/// already promised to somebody else. On hand on its own has sold the same part twice.
/// </para>
/// </summary>
/// <param name="PartId">The part.</param>
/// <param name="WarehouseId">The warehouse.</param>
/// <param name="OnHand">What is physically there.</param>
/// <param name="Reserved">How much of it is already spoken for.</param>
/// <param name="Available">On hand less reserved. Can be negative if the warehouse allows it.</param>
/// <param name="UnitCode">The unit all three are counted in, e.g. EA, SET, L.</param>
public sealed record StockAvailability(
    Guid PartId,
    Guid WarehouseId,
    decimal OnHand,
    decimal Reserved,
    decimal Available,
    string UnitCode);
