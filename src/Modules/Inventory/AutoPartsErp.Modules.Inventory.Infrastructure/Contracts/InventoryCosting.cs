using AutoPartsErp.ModuleContracts.Inventory;
using AutoPartsErp.Modules.Inventory.Domain;
using AutoPartsErp.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AutoPartsErp.Modules.Inventory.Infrastructure.Contracts;

/// <summary>
/// Inventory's answer to "what is this worth on the shelf?".
/// <para>
/// Columns rather than aggregates, like its sibling next door, and for the same reason: a caller
/// costing a ten-line order needs two numbers per part, not ten balances with their reservation
/// collections loaded.
/// </para>
/// <para>
/// The division happens here rather than in the caller because the shape of it matters. Inventory
/// stores the shelf's <i>value</i> and derives a unit cost from it, never the reverse — a stored
/// per-unit figure rounds on every receipt and stops adding up to the balance sheet it is meant
/// to explain. Handing a caller the value and the quantity and letting them divide would export
/// that decision along with the numbers.
/// </para>
/// <para>
/// An empty shelf has no cost. Dividing by nothing is not a cost of zero, and a caller told zero
/// would compute a margin of one hundred per cent on a part nobody knows the cost of.
/// </para>
/// </summary>
public sealed class InventoryCosting : IInventoryCosting
{
    private readonly InventoryDbContext _context;

    /// <summary>Initializes the adapter.</summary>
    public InventoryCosting(InventoryDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<StockUnitCost?> GetUnitCostAsync(
        Guid partId,
        Guid warehouseId,
        CancellationToken cancellationToken = default)
    {
        var part = new PartRef(partId);
        var warehouse = new WarehouseId(warehouseId);

        var row = await _context.StockItems
            .AsNoTracking()
            .Where(item => item.Part == part && item.WarehouseId == warehouse)
            .Where(item => item.OnHand.Value > 0m)
            .Select(item => new
            {
                Value = item.StockValue.Amount,
                Currency = item.StockValue.Currency,
                OnHand = item.OnHand.Value,
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return row is null || row.Value == 0m
            ? null
            : new StockUnitCost(
                partId,
                warehouseId,
                decimal.Round(row.Value / row.OnHand, row.Currency.DecimalPlaces, MidpointRounding.ToEven),
                row.Currency.Code);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, StockUnitCost>> GetUnitCostsAsync(
        IReadOnlyCollection<Guid> partIds,
        Guid warehouseId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(partIds);

        if (partIds.Count == 0)
        {
            return new Dictionary<Guid, StockUnitCost>();
        }

        var warehouse = new WarehouseId(warehouseId);
        List<PartRef> parts = [.. partIds.Distinct().Select(id => new PartRef(id))];

        var rows = await _context.StockItems
            .AsNoTracking()
            .Where(item => parts.Contains(item.Part) && item.WarehouseId == warehouse)
            .Where(item => item.OnHand.Value > 0m && item.StockValue.Amount != 0m)
            .Select(item => new
            {
                item.Part,
                Value = item.StockValue.Amount,
                Currency = item.StockValue.Currency,
                OnHand = item.OnHand.Value,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.ToDictionary(
            row => row.Part.Value,
            row => new StockUnitCost(
                row.Part.Value,
                warehouseId,
                decimal.Round(row.Value / row.OnHand, row.Currency.DecimalPlaces, MidpointRounding.ToEven),
                row.Currency.Code));
    }
}
