using System.Globalization;
using AutoPartsErp.ModuleContracts.Catalog;
using AutoPartsErp.Modules.Inventory.Application.Counting;
using AutoPartsErp.Modules.Inventory.Domain;
using AutoPartsErp.Modules.Inventory.Domain.Counting;
using AutoPartsErp.Modules.Inventory.Application.Transfers;
using AutoPartsErp.Modules.Inventory.Domain.Stock;
using AutoPartsErp.Modules.Inventory.Domain.Transfers;
using AutoPartsErp.Modules.Inventory.Domain.Warehouses;
using AutoPartsErp.SharedKernel.ValueObjects;
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
    public async Task<IReadOnlyList<StockItem>> GetWithActiveReservationForAsync(
        string referenceNumber,
        CancellationToken cancellationToken = default)
    {
        // Uppercased because MovementReference normalises document numbers on the way in, and a
        // reference that only matches in one case is a reference that silently matches nothing.
        string normalized = referenceNumber?.Trim().ToUpperInvariant() ?? string.Empty;

        List<StockItem> items = await _context.StockItems
            .Where(item => item.Reservations.Any(reservation =>
                reservation.Status == ReservationStatus.Active
                && reservation.Reference.Number == normalized))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return items;
    }

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

/// <summary>Write-side access to count sheets.</summary>
public sealed class StockCountRepository : IStockCountRepository
{
    private const string NumberKey = "stock-count";
    private const string NumberPrefix = "SC";

    private readonly InventoryDbContext _context;

    /// <summary>Initializes the repository.</summary>
    public StockCountRepository(InventoryDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public Task<StockCount?> GetByIdAsync(
        StockCountId id,
        CancellationToken cancellationToken = default) =>
        _context.StockCounts.FirstOrDefaultAsync(count => count.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsAsync(StockCountId id, CancellationToken cancellationToken = default) =>
        _context.StockCounts.AnyAsync(count => count.Id == id, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// The lines are an owned collection, so EF loads them with the sheet whether asked to or
    /// not. This method exists to say so at the call site rather than to change the query — a
    /// reader who sees <c>GetByIdAsync</c> before posting a count has every right to wonder
    /// whether the lines came with it.
    /// </remarks>
    public Task<StockCount?> GetWithLinesAsync(
        StockCountId id,
        CancellationToken cancellationToken = default) =>
        GetByIdAsync(id, cancellationToken);

    /// <inheritdoc />
    public async Task<string> NextCountNumberAsync(
        int year,
        CancellationToken cancellationToken = default)
    {
        int next = await _context
            .TakeNextNumberAsync(NumberKey, year, cancellationToken)
            .ConfigureAwait(false);

        return string.Create(CultureInfo.InvariantCulture, $"{NumberPrefix}-{year}-{next:D5}");
    }

    /// <inheritdoc />
    public void Add(StockCount aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.StockCounts.Add(aggregate);
    }

    /// <inheritdoc />
    public void Remove(StockCount aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.StockCounts.Remove(aggregate);
    }
}

/// <summary>
/// Reads what is in a warehouse, flat, for putting on a count sheet.
/// <para>
/// Four columns and no aggregates. A warehouse holding forty thousand parts would otherwise mean
/// forty thousand <c>StockItem</c> graphs, each dragging its reservations and its expected
/// deliveries along, to write one number per part onto a sheet.
/// </para>
/// </summary>
public sealed class StockCountScope : IStockCountScope
{
    private readonly InventoryDbContext _context;
    private readonly ICatalogDirectory _catalog;

    /// <summary>Initializes the reader.</summary>
    public StockCountScope(InventoryDbContext context, ICatalogDirectory catalog)
    {
        _context = context;
        _catalog = catalog;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// The SKU and the name come from Catalog, in one call for the whole sheet rather than one
    /// per part. They are snapshotted onto the lines and never refreshed: a sheet is a document
    /// somebody may be holding on paper in an aisle, and a description that changes underneath it
    /// makes the paper and the screen disagree about what was counted.
    /// </para>
    /// <para>
    /// A part the catalogue no longer recognizes still goes on the sheet, with its identifier in
    /// place of a SKU. It is on the shelf either way, and leaving it off would mean the one part
    /// nobody can identify is also the one part nobody counts.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<CountableStock>> ListAsync(
        WarehouseId warehouseId,
        bool includeZeroBalances,
        CancellationToken cancellationToken = default)
    {
        IQueryable<StockItem> query = _context.StockItems
            .AsNoTracking()
            .Where(item => item.WarehouseId == warehouseId);

        if (!includeZeroBalances)
        {
            query = query.Where(item => item.OnHand.Value != 0m);
        }

        var rows = await query
            .OrderBy(item => item.Part)
            .Select(item => new
            {
                item.Part,
                OnHand = item.OnHand.Value,
                item.Unit,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (rows.Count == 0)
        {
            return [];
        }

        IReadOnlyDictionary<Guid, PartDescriptor> parts = await _catalog
            .GetManyAsync([.. rows.Select(row => row.Part.Value)], cancellationToken)
            .ConfigureAwait(false);

        return [.. rows.Select(row =>
        {
            bool known = parts.TryGetValue(row.Part.Value, out PartDescriptor? part);

            return new CountableStock(
                row.Part,
                known ? part!.Sku : row.Part.Value.ToString("D", CultureInfo.InvariantCulture),
                known ? part!.Name : string.Empty,
                Quantity.Create(row.OnHand, row.Unit).Value);
        })];
    }
}

/// <summary>Write-side access to stock transfers.</summary>
public sealed class StockTransferRepository : IStockTransferRepository
{
    private const string NumberKey = "stock-transfer";
    private const string NumberPrefix = "TR";

    private readonly InventoryDbContext _context;

    /// <summary>Initializes the repository.</summary>
    public StockTransferRepository(InventoryDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public Task<StockTransfer?> GetByIdAsync(
        StockTransferId id,
        CancellationToken cancellationToken = default) =>
        _context.StockTransfers.FirstOrDefaultAsync(transfer => transfer.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsAsync(StockTransferId id, CancellationToken cancellationToken = default) =>
        _context.StockTransfers.AnyAsync(transfer => transfer.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<StockTransfer>> GetInTransitAsync(
        CancellationToken cancellationToken = default)
    {
        List<StockTransfer> transfers = await _context.StockTransfers
            .Where(transfer => transfer.Status == StockTransferStatus.InTransit
                || transfer.Status == StockTransferStatus.PartiallyReceived)
            .OrderByDescending(transfer => transfer.DispatchedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return transfers;
    }

    /// <inheritdoc />
    public async Task<string> NextTransferNumberAsync(
        int year,
        CancellationToken cancellationToken = default)
    {
        int next = await _context
            .TakeNextNumberAsync(NumberKey, year, cancellationToken)
            .ConfigureAwait(false);

        return string.Create(CultureInfo.InvariantCulture, $"{NumberPrefix}-{year}-{next:D5}");
    }

    /// <inheritdoc />
    public void Add(StockTransfer aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.StockTransfers.Add(aggregate);
    }

    /// <inheritdoc />
    public void Remove(StockTransfer aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.StockTransfers.Remove(aggregate);
    }
}

/// <summary>
/// Names a part for a transfer note, from the catalogue.
/// <para>
/// A part the catalogue does not recognize still goes on the note, with its identifier in place
/// of a SKU. Something physically on a shelf has to be movable whatever the catalogue currently
/// thinks of it — a discontinued part is exactly the kind of thing a branch sends back to the
/// depot.
/// </para>
/// </summary>
public sealed class TransferPartNaming : ITransferPartNaming
{
    private readonly ICatalogDirectory _catalog;

    /// <summary>Initializes the reader.</summary>
    public TransferPartNaming(ICatalogDirectory catalog)
    {
        _catalog = catalog;
    }

    /// <inheritdoc />
    public async Task<(string Sku, string Description)> DescribeAsync(
        Guid partId,
        CancellationToken cancellationToken = default)
    {
        PartDescriptor? part = await _catalog
            .GetAsync(partId, cancellationToken)
            .ConfigureAwait(false);

        return part is null
            ? (partId.ToString("D", CultureInfo.InvariantCulture), string.Empty)
            : (part.Sku, part.Name);
    }
}
