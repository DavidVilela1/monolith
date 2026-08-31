using AutoPartsErp.Modules.Inventory.Application.Contracts;
using AutoPartsErp.SharedKernel.Paging;

namespace AutoPartsErp.Modules.Inventory.Application.Abstractions;

/// <summary>The read side of the Inventory module.</summary>
public interface IInventoryReadStore
{
    /// <summary>A part's position across every warehouse, or null when it has no stock records.</summary>
    Task<PartStockPosition?> GetPartStockAsync(Guid partId, CancellationToken cancellationToken = default);

    /// <summary>The balance for one part in one warehouse, or null when none exists.</summary>
    Task<StockBalance?> GetBalanceAsync(
        Guid partId,
        Guid warehouseId,
        CancellationToken cancellationToken = default);

    /// <summary>The claims currently held against a balance.</summary>
    Task<IReadOnlyList<ReservationDto>> GetReservationsAsync(
        Guid partId,
        Guid warehouseId,
        bool activeOnly,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Everything at or below its reorder point. The purchasing screen's whole job, so it is
    /// ordered by how far under the line each part has fallen.
    /// </summary>
    Task<PagedResult<StockBalance>> GetReplenishmentListAsync(
        Guid? warehouseId,
        PageRequest page,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The ledger for a part, newest first. This is the answer to "why do we think we have three?"
    /// </summary>
    Task<PagedResult<StockMovementDto>> GetMovementsAsync(
        Guid partId,
        Guid? warehouseId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        PageRequest page,
        CancellationToken cancellationToken = default);

    /// <summary>Lists warehouses.</summary>
    Task<IReadOnlyList<WarehouseDto>> ListWarehousesAsync(
        bool activeOnly,
        CancellationToken cancellationToken = default);
}
