using AutoPartsErp.Modules.Inventory.Application.Abstractions;
using AutoPartsErp.Modules.Inventory.Application.Contracts;
using AutoPartsErp.Modules.Inventory.Domain;
using AutoPartsErp.Modules.Inventory.Domain.Stock;
using AutoPartsErp.Modules.Inventory.Domain.Warehouses;
using AutoPartsErp.SharedKernel.Paging;
using Microsoft.EntityFrameworkCore;

namespace AutoPartsErp.Modules.Inventory.Infrastructure.Persistence.ReadStore;

/// <summary>
/// Serves the Inventory module's queries.
/// <para>
/// As in Catalog, value-converted properties are selected whole and unwrapped once the rows are
/// in memory; reaching inside one in a LINQ expression is not translatable.
/// </para>
/// </summary>
public sealed class InventoryReadStore : IInventoryReadStore
{
    private readonly InventoryDbContext _context;

    /// <summary>Initializes the read store.</summary>
    public InventoryReadStore(InventoryDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<PartStockPosition?> GetPartStockAsync(
        Guid partId,
        CancellationToken cancellationToken = default)
    {
        var part = new PartRef(partId);

        List<StockBalance> balances = await QueryBalances(
            _context.StockItems.Where(item => item.Part == part), cancellationToken)
            .ConfigureAwait(false);

        if (balances.Count == 0)
        {
            return null;
        }

        return new PartStockPosition(
            partId,
            balances[0].Unit,
            balances.Sum(balance => balance.OnHand),
            balances.Sum(balance => balance.Available),
            balances);
    }

    /// <inheritdoc />
    public async Task<StockBalance?> GetBalanceAsync(
        Guid partId,
        Guid warehouseId,
        CancellationToken cancellationToken = default)
    {
        var part = new PartRef(partId);
        var warehouse = new WarehouseId(warehouseId);

        List<StockBalance> balances = await QueryBalances(
            _context.StockItems.Where(item => item.Part == part && item.WarehouseId == warehouse),
            cancellationToken)
            .ConfigureAwait(false);

        return balances.Count == 0 ? null : balances[0];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReservationDto>> GetReservationsAsync(
        Guid partId,
        Guid warehouseId,
        bool activeOnly,
        CancellationToken cancellationToken = default)
    {
        var part = new PartRef(partId);
        var warehouse = new WarehouseId(warehouseId);

        StockItem? item = await _context.StockItems
            .AsNoTracking()
            .FirstOrDefaultAsync(
                stock => stock.Part == part && stock.WarehouseId == warehouse,
                cancellationToken)
            .ConfigureAwait(false);

        if (item is null)
        {
            return [];
        }

        IEnumerable<StockReservation> reservations = item.Reservations;

        if (activeOnly)
        {
            reservations = reservations.Where(reservation => reservation.IsActive);
        }

        return [.. reservations
            .OrderByDescending(reservation => reservation.CreatedAtUtc)
            .Select(reservation => new ReservationDto(
                reservation.Id.Value,
                reservation.Quantity.Value,
                reservation.Reference.Type.ToString(),
                reservation.Reference.Number,
                reservation.Status.ToString(),
                reservation.CreatedAtUtc,
                reservation.ExpiresAtUtc))];
    }

    /// <inheritdoc />
    public async Task<PagedResult<StockBalance>> GetReplenishmentListAsync(
        Guid? warehouseId,
        PageRequest page,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);

        // The comparison is against available, not on-hand. Stock that is present but promised
        // cannot fill the next order, so replenishment has to see the same number a salesperson does.
        IQueryable<StockItem> query = _context.StockItems
            .AsNoTracking()
            .Where(item => item.ReorderPoint != null
                && (item.OnHand.Value - item.Reserved.Value) <= item.ReorderPoint!.Value);

        if (warehouseId is { } id)
        {
            var warehouse = new WarehouseId(id);
            query = query.Where(item => item.WarehouseId == warehouse);
        }

        int total = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        if (total == 0)
        {
            return PagedResult<StockBalance>.Empty(page.Page, page.PageSize);
        }

        // Deepest shortfall first: what a buyer should look at before anything else.
        IQueryable<StockItem> ordered = query
            .OrderBy(item => (item.OnHand.Value - item.Reserved.Value) - item.ReorderPoint!.Value)
            .Skip(page.Skip)
            .Take(page.PageSize);

        List<StockBalance> rows = await QueryBalances(ordered, cancellationToken).ConfigureAwait(false);

        return PagedResult<StockBalance>.Create(rows, page.Page, page.PageSize, total);
    }

    /// <inheritdoc />
    public async Task<PagedResult<StockMovementDto>> GetMovementsAsync(
        Guid partId,
        Guid? warehouseId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        PageRequest page,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);

        var part = new PartRef(partId);

        IQueryable<StockMovement> query = _context.StockMovements
            .AsNoTracking()
            .Where(movement => movement.Part == part);

        if (warehouseId is { } id)
        {
            var warehouse = new WarehouseId(id);
            query = query.Where(movement => movement.WarehouseId == warehouse);
        }

        if (from is { } start)
        {
            query = query.Where(movement => movement.OccurredAtUtc >= start);
        }

        if (to is { } end)
        {
            query = query.Where(movement => movement.OccurredAtUtc <= end);
        }

        int total = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        if (total == 0)
        {
            return PagedResult<StockMovementDto>.Empty(page.Page, page.PageSize);
        }

        var rows = await query
            .OrderByDescending(movement => movement.OccurredAtUtc)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(movement => new
            {
                movement.Id,
                movement.Part,
                WarehouseCode = _context.Warehouses
                    .Where(warehouse => warehouse.Id == movement.WarehouseId)
                    .Select(warehouse => warehouse.Code)
                    .FirstOrDefault(),
                movement.Type,
                QuantityValue = movement.Quantity.Value,
                BalanceValue = movement.BalanceAfter.Value,
                ReferenceType = movement.Reference.Type,
                ReferenceNumber = movement.Reference.Number,
                movement.OccurredAtUtc,
                movement.CreatedBy,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<StockMovementDto> items = [.. rows.Select(row => new StockMovementDto(
            row.Id.Value,
            row.Part.Value,
            row.WarehouseCode ?? string.Empty,
            row.Type.ToString(),
            row.QuantityValue,
            row.BalanceValue,
            row.ReferenceType.ToString(),
            row.ReferenceNumber,
            row.OccurredAtUtc,
            row.CreatedBy))];

        return PagedResult<StockMovementDto>.Create(items, page.Page, page.PageSize, total);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WarehouseDto>> ListWarehousesAsync(
        bool activeOnly,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Warehouse> query = _context.Warehouses.AsNoTracking();

        if (activeOnly)
        {
            query = query.Where(warehouse => warehouse.IsActive);
        }

        var rows = await query
            .OrderBy(warehouse => warehouse.Code)
            .Select(warehouse => new
            {
                warehouse.Id,
                warehouse.Code,
                warehouse.Name,
                warehouse.Kind,
                warehouse.IsActive,
                warehouse.AllowsNegativeStock,
                warehouse.RequiresBinTracking,
                StockedPartCount = _context.StockItems
                    .Count(item => item.WarehouseId == warehouse.Id && item.OnHand.Value != 0m),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. rows.Select(row => new WarehouseDto(
            row.Id.Value,
            row.Code,
            row.Name,
            row.Kind.ToString(),
            row.IsActive,
            row.AllowsNegativeStock,
            row.RequiresBinTracking,
            row.StockedPartCount))];
    }

    private async Task<List<StockBalance>> QueryBalances(
        IQueryable<StockItem> query,
        CancellationToken cancellationToken)
    {
        var rows = await query
            .AsNoTracking()
            .Select(item => new
            {
                item.Id,
                item.Part,
                item.WarehouseId,
                WarehouseCode = _context.Warehouses
                    .Where(warehouse => warehouse.Id == item.WarehouseId)
                    .Select(warehouse => warehouse.Code)
                    .FirstOrDefault(),
                WarehouseName = _context.Warehouses
                    .Where(warehouse => warehouse.Id == item.WarehouseId)
                    .Select(warehouse => warehouse.Name)
                    .FirstOrDefault(),
                item.Unit,
                OnHand = item.OnHand.Value,
                Reserved = item.Reserved.Value,
                OnOrder = item.OnOrder.Value,
                ReorderPoint = item.ReorderPoint != null ? item.ReorderPoint.Value : (decimal?)null,
                ReorderQuantity = item.ReorderQuantity != null ? item.ReorderQuantity.Value : (decimal?)null,
                item.LastCountedAtUtc,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. rows.Select(row =>
        {
            decimal available = row.OnHand - row.Reserved;

            return new StockBalance
            {
                StockItemId = row.Id.Value,
                PartId = row.Part.Value,
                WarehouseId = row.WarehouseId.Value,
                WarehouseCode = row.WarehouseCode ?? string.Empty,
                WarehouseName = row.WarehouseName ?? string.Empty,
                Unit = row.Unit.Code,
                OnHand = row.OnHand,
                Reserved = row.Reserved,
                Available = available,
                OnOrder = row.OnOrder,
                ReorderPoint = row.ReorderPoint,
                ReorderQuantity = row.ReorderQuantity,
                NeedsReplenishment = row.ReorderPoint is { } point && available <= point,
                LastCountedAtUtc = row.LastCountedAtUtc,
            };
        })];
    }
}
