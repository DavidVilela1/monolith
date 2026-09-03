using AutoPartsErp.ModuleContracts.Inventory;
using AutoPartsErp.Modules.Inventory.Domain;
using AutoPartsErp.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AutoPartsErp.Modules.Inventory.Infrastructure.Contracts;

/// <summary>
/// Inventory's answer to "how much can I still promise?".
/// <para>
/// Reads columns, not aggregates. A caller asking about ten parts does not need ten StockItems
/// with their reservation collections loaded — it needs three numbers each, and projecting them
/// keeps a counter screen quick enough to be used.
/// </para>
/// <para>
/// The tenant filter applies automatically, so a caller can only ever be told about its own
/// company's stock. That is worth stating because this is the one place another module reaches
/// into this one synchronously, and it would be an unpleasant place to leak from.
/// </para>
/// </summary>
public sealed class InventoryAvailability : IInventoryAvailability
{
    private readonly InventoryDbContext _context;

    /// <summary>Initializes the adapter.</summary>
    public InventoryAvailability(InventoryDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<StockAvailability?> GetAsync(
        Guid partId,
        Guid warehouseId,
        CancellationToken cancellationToken = default)
    {
        var part = new PartRef(partId);
        var warehouse = new WarehouseId(warehouseId);

        var row = await _context.StockItems
            .AsNoTracking()
            .Where(item => item.Part == part && item.WarehouseId == warehouse)
            .Select(item => new
            {
                OnHand = item.OnHand.Value,
                Reserved = item.Reserved.Value,
                item.Unit,
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return row is null
            ? null
            : new StockAvailability(
                partId,
                warehouseId,
                row.OnHand,
                row.Reserved,
                row.OnHand - row.Reserved,
                row.Unit.Code);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, StockAvailability>> GetManyAsync(
        IReadOnlyCollection<Guid> partIds,
        Guid warehouseId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(partIds);

        if (partIds.Count == 0)
        {
            return new Dictionary<Guid, StockAvailability>();
        }

        var warehouse = new WarehouseId(warehouseId);
        List<PartRef> parts = [.. partIds.Distinct().Select(id => new PartRef(id))];

        var rows = await _context.StockItems
            .AsNoTracking()
            .Where(item => parts.Contains(item.Part) && item.WarehouseId == warehouse)
            .Select(item => new
            {
                item.Part,
                OnHand = item.OnHand.Value,
                Reserved = item.Reserved.Value,
                item.Unit,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.ToDictionary(
            row => row.Part.Value,
            row => new StockAvailability(
                row.Part.Value,
                warehouseId,
                row.OnHand,
                row.Reserved,
                row.OnHand - row.Reserved,
                row.Unit.Code));
    }
}
