using AutoPartsErp.Modules.Inventory.Domain.Stock;
using AutoPartsErp.Modules.Inventory.Domain.Warehouses;
using AutoPartsErp.SharedKernel.Abstractions;

namespace AutoPartsErp.Modules.Inventory.Domain;

/// <summary>Write-side access to stock balances.</summary>
public interface IStockItemRepository : IRepository<StockItem, StockItemId>
{
    /// <summary>Loads the balance for one part in one warehouse, or null when none exists.</summary>
    Task<StockItem?> GetAsync(PartRef part, WarehouseId warehouseId, CancellationToken cancellationToken = default);

    /// <summary>True when a balance already exists for this part and warehouse.</summary>
    Task<bool> ExistsAsync(PartRef part, WarehouseId warehouseId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads every balance for a part across all warehouses.
    /// Bounded by the number of sites, so loading whole aggregates here is reasonable.
    /// </summary>
    Task<IReadOnlyList<StockItem>> GetAllForPartAsync(PartRef part, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads balances holding reservations that have passed their expiry.
    /// Used by the sweep that returns abandoned quote stock to available.
    /// </summary>
    Task<IReadOnlyList<StockItem>> GetWithExpiredReservationsAsync(
        DateTimeOffset now,
        int maxItems,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads every balance still holding an active claim for one document.
    /// <para>
    /// A cancelled sales order names itself and nothing else — it does not carry its lines — so
    /// releasing what it was holding means finding the claims by their reference. Bounded by the
    /// number of lines that order had.
    /// </para>
    /// </summary>
    /// <param name="referenceNumber">The document number, e.g. "SO-2026-01188".</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<StockItem>> GetWithActiveReservationForAsync(
        string referenceNumber,
        CancellationToken cancellationToken = default);
}

/// <summary>Write-side access to warehouses.</summary>
public interface IWarehouseRepository : IRepository<Warehouse, WarehouseId>
{
    /// <summary>Loads a warehouse by code, or null when there is no such warehouse.</summary>
    Task<Warehouse?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>True when the code is already taken.</summary>
    Task<bool> CodeExistsAsync(
        string code,
        WarehouseId? excludingWarehouseId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Every warehouse currently open to movements.</summary>
    Task<IReadOnlyList<Warehouse>> GetActiveAsync(CancellationToken cancellationToken = default);
}

/// <summary>Write-side access to storage bins.</summary>
public interface IStorageBinRepository : IRepository<StorageBin, BinId>
{
    /// <summary>Loads a bin by its code within a warehouse.</summary>
    Task<StorageBin?> GetByCodeAsync(
        WarehouseId warehouseId,
        string code,
        CancellationToken cancellationToken = default);

    /// <summary>True when the code is already used in that warehouse.</summary>
    Task<bool> CodeExistsAsync(
        WarehouseId warehouseId,
        string code,
        CancellationToken cancellationToken = default);
}

/// <summary>Append-only access to the stock ledger.</summary>
public interface IStockMovementRepository
{
    /// <summary>Stages a movement for insertion. There is no update and no delete, by design.</summary>
    void Add(StockMovement movement);

    /// <summary>Stages several movements, for operations that move more than one line at once.</summary>
    void AddRange(IEnumerable<StockMovement> movements);
}

/// <summary>The Inventory module's unit of work.</summary>
public interface IInventoryUnitOfWork : IUnitOfWork;
