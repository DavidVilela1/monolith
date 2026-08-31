using AutoPartsErp.Modules.Inventory.Domain.Warehouses;
using AutoPartsErp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AutoPartsErp.Modules.Inventory.Infrastructure.Persistence.Seed;

/// <summary>
/// Creates a couple of warehouses in an empty database.
/// <para>
/// Order matters here: the warehouses must exist before Catalog activates any parts, because
/// <c>OpenStockRecordOnPartActivated</c> opens a balance in every <i>active</i> warehouse. Seed
/// them after the parts and the new stock records would have nowhere to go — which is why the
/// Inventory module registers with a lower Order than Catalog.
/// </para>
/// </summary>
public sealed class InventorySeeder
{
    private readonly InventoryDbContext _context;
    private readonly ILogger<InventorySeeder> _logger;

    /// <summary>Initializes the seeder.</summary>
    public InventorySeeder(InventoryDbContext context, ILogger<InventorySeeder> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>Seeds warehouses if, and only if, none exist.</summary>
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (await _context.Warehouses.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            _logger.LogInformation("Warehouses already exist; skipping seed.");
            return;
        }

        Warehouse main = Require(Warehouse.Create(
            "MAIN", "Main depot", WarehouseKind.Depot, requiresBinTracking: true));

        Warehouse counter = Require(Warehouse.Create(
            "BR01", "Trade counter", WarehouseKind.Branch));

        Warehouse quarantine = Require(Warehouse.Create(
            "QUAR", "Quarantine and returns", WarehouseKind.Quarantine));

        _context.Warehouses.AddRange(main, counter, quarantine);

        _context.StorageBins.AddRange(
            Require(StorageBin.Create(main.Id, "A-01-1", BinKind.Picking, 10)),
            Require(StorageBin.Create(main.Id, "A-01-2", BinKind.Picking, 20)),
            Require(StorageBin.Create(main.Id, "B-04-3", BinKind.Bulk, 500)),
            Require(StorageBin.Create(main.Id, "GR-IN", BinKind.Receiving, 1)));

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Seeded 3 warehouses and 4 storage bins.");
    }

    private static T Require<T>(Result<T> result) =>
        result.IsSuccess
            ? result.Value
            : throw new InvalidOperationException($"Seed data is invalid: {result.Error}");
}
