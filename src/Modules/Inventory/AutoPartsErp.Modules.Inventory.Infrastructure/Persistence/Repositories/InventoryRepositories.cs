using AutoPartsErp.Modules.Inventory.Domain;
using AutoPartsErp.Modules.Inventory.Domain.Stock;
using AutoPartsErp.Modules.Inventory.Domain.Warehouses;
using Microsoft.EntityFrameworkCore;

namespace AutoPartsErp.Modules.Inventory.Infrastructure.Persistence.Repositories;

/// <summary>Write-side access to stock balances.</summary>
public sealed class StockItemRepository : IStockItemRepository
{
    private readonly InventoryDbContext _context;

    /// <summary>Initializes the repository.</summary>
    public StockItemRepository(InventoryDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public Task<StockItem?> GetByIdAsync(StockItemId id, CancellationToken cancellationToken = default) =>
        _context.StockItems.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsAsync(StockItemId id, CancellationToken cancellationToken = default) =>
        _context.StockItems.AnyAsync(item => item.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<StockItem?> GetAsync(
        PartRef part,
        WarehouseId warehouseId,
        CancellationToken cancellationToken = default) =>
        _context.StockItems.FirstOrDefaultAsync(
            item => item.Part == part && item.WarehouseId == warehouseId,
            cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsAsync(
        PartRef part,
        WarehouseId warehouseId,
        CancellationToken cancellationToken = default) =>
        _context.StockItems.AnyAsync(
            item => item.Part == part && item.WarehouseId == warehouseId,
            cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<StockItem>> GetAllForPartAsync(
        PartRef part,
        CancellationToken cancellationToken = default) =>
        await _context.StockItems
            .Where(item => item.Part == part)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<StockItem>> GetWithExpiredReservationsAsync(
        DateTimeOffset now,
        int maxItems,
        CancellationToken cancellationToken = default) =>
        await _context.StockItems
            .Where(item => item.Reservations.Any(reservation =>
                reservation.Status == ReservationStatus.Active
                && reservation.ExpiresAtUtc != null
                && reservation.ExpiresAtUtc <= now))
            .OrderBy(item => item.Id)
            .Take(maxItems)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void Add(StockItem aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.StockItems.Add(aggregate);
    }

    /// <inheritdoc />
    public void Remove(StockItem aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.StockItems.Remove(aggregate);
    }
}

/// <summary>
/// Append-only access to the ledger.
/// <para>
/// There is no Update and no Remove, and that is the whole point: a movement is a historical
/// fact. Corrections are new compensating rows, the way an accountant would do it.
/// </para>
/// </summary>
public sealed class StockMovementRepository : IStockMovementRepository
{
    private readonly InventoryDbContext _context;

    /// <summary>Initializes the repository.</summary>
    public StockMovementRepository(InventoryDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public void Add(StockMovement movement)
    {
        ArgumentNullException.ThrowIfNull(movement);
        _context.StockMovements.Add(movement);
    }

    /// <inheritdoc />
    public void AddRange(IEnumerable<StockMovement> movements)
    {
        ArgumentNullException.ThrowIfNull(movements);
        _context.StockMovements.AddRange(movements);
    }
}

/// <summary>Write-side access to warehouses.</summary>
public sealed class WarehouseRepository : IWarehouseRepository
{
    private readonly InventoryDbContext _context;

    /// <summary>Initializes the repository.</summary>
    public WarehouseRepository(InventoryDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public Task<Warehouse?> GetByIdAsync(WarehouseId id, CancellationToken cancellationToken = default) =>
        _context.Warehouses.FirstOrDefaultAsync(warehouse => warehouse.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsAsync(WarehouseId id, CancellationToken cancellationToken = default) =>
        _context.Warehouses.AnyAsync(warehouse => warehouse.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<Warehouse?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        string normalized = code?.Trim().ToUpperInvariant() ?? string.Empty;
        return _context.Warehouses.FirstOrDefaultAsync(
            warehouse => warehouse.Code == normalized, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> CodeExistsAsync(
        string code,
        WarehouseId? excludingWarehouseId = null,
        CancellationToken cancellationToken = default)
    {
        string normalized = code?.Trim().ToUpperInvariant() ?? string.Empty;

        IQueryable<Warehouse> query = _context.Warehouses.Where(warehouse => warehouse.Code == normalized);

        if (excludingWarehouseId is { } excluded)
        {
            query = query.Where(warehouse => warehouse.Id != excluded);
        }

        return query.AnyAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Warehouse>> GetActiveAsync(CancellationToken cancellationToken = default) =>
        await _context.Warehouses
            .Where(warehouse => warehouse.IsActive)
            .OrderBy(warehouse => warehouse.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void Add(Warehouse aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.Warehouses.Add(aggregate);
    }

    /// <inheritdoc />
    public void Remove(Warehouse aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.Warehouses.Remove(aggregate);
    }
}

/// <summary>Write-side access to storage bins.</summary>
public sealed class StorageBinRepository : IStorageBinRepository
{
    private readonly InventoryDbContext _context;

    /// <summary>Initializes the repository.</summary>
    public StorageBinRepository(InventoryDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public Task<StorageBin?> GetByIdAsync(BinId id, CancellationToken cancellationToken = default) =>
        _context.StorageBins.FirstOrDefaultAsync(bin => bin.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsAsync(BinId id, CancellationToken cancellationToken = default) =>
        _context.StorageBins.AnyAsync(bin => bin.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<StorageBin?> GetByCodeAsync(
        WarehouseId warehouseId,
        string code,
        CancellationToken cancellationToken = default)
    {
        string normalized = code?.Trim().ToUpperInvariant() ?? string.Empty;
        return _context.StorageBins.FirstOrDefaultAsync(
            bin => bin.WarehouseId == warehouseId && bin.Code == normalized,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> CodeExistsAsync(
        WarehouseId warehouseId,
        string code,
        CancellationToken cancellationToken = default)
    {
        string normalized = code?.Trim().ToUpperInvariant() ?? string.Empty;
        return _context.StorageBins.AnyAsync(
            bin => bin.WarehouseId == warehouseId && bin.Code == normalized,
            cancellationToken);
    }

    /// <inheritdoc />
    public void Add(StorageBin aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.StorageBins.Add(aggregate);
    }

    /// <inheritdoc />
    public void Remove(StorageBin aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.StorageBins.Remove(aggregate);
    }
}
