using AutoPartsErp.Modules.Inventory.Domain;
using AutoPartsErp.Modules.Inventory.Domain.Stock;
using AutoPartsErp.Modules.Inventory.Domain.Warehouses;
using AutoPartsErp.Persistence;
using AutoPartsErp.SharedKernel.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace AutoPartsErp.Modules.Inventory.Infrastructure.Persistence;

/// <summary>
/// The Inventory module's database context, scoped to the <c>inventory</c> schema.
/// <para>
/// It maps no Catalog table and holds no foreign key into one. A part is a bare Guid column
/// here. That is the module boundary showing up in the database rather than only in the project
/// graph — and it is what would let Inventory move to its own database without a schema rewrite.
/// </para>
/// </summary>
public sealed class InventoryDbContext : ModuleDbContext, IInventoryUnitOfWork
{
    /// <summary>The PostgreSQL schema this context owns.</summary>
    public const string SchemaName = "inventory";

    /// <summary>Initializes the context.</summary>
    /// <param name="options">EF Core options, supplied by the container.</param>
    /// <param name="tenantContext">The active tenant, used by the global query filters.</param>
    /// <param name="domainEventDispatcher">Dispatches domain events after a successful commit.</param>
    public InventoryDbContext(
        DbContextOptions<InventoryDbContext> options,
        ITenantContext? tenantContext = null,
        IDomainEventDispatcher? domainEventDispatcher = null)
        : base(options, tenantContext, domainEventDispatcher)
    {
    }

    /// <summary>Stock balances, one row per part per warehouse.</summary>
    public DbSet<StockItem> StockItems => Set<StockItem>();

    /// <summary>The append-only stock ledger.</summary>
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();

    /// <summary>Physical locations stock lives in.</summary>
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();

    /// <summary>Named places inside a warehouse.</summary>
    public DbSet<StorageBin> StorageBins => Set<StorageBin>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(InventoryDbContext).Assembly);

        modelBuilder.Entity<StockItem>()
            .HasQueryFilter(item => item.TenantId == CurrentTenantId);

        modelBuilder.Entity<StockMovement>()
            .HasQueryFilter(movement => movement.TenantId == CurrentTenantId);

        modelBuilder.Entity<Warehouse>()
            .HasQueryFilter(warehouse => !warehouse.IsDeleted && warehouse.TenantId == CurrentTenantId);

        modelBuilder.Entity<StorageBin>()
            .HasQueryFilter(bin => !bin.IsDeleted && bin.TenantId == CurrentTenantId);

        base.OnModelCreating(modelBuilder);
    }
}
