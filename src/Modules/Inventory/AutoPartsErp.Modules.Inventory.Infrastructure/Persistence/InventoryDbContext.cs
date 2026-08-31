using AutoPartsErp.Modules.Inventory.Domain;
using AutoPartsErp.Modules.Inventory.Domain.Stock;
using AutoPartsErp.Modules.Inventory.Domain.Warehouses;
using AutoPartsErp.SharedKernel.Abstractions;
using AutoPartsErp.SharedKernel.Primitives;
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
public sealed class InventoryDbContext : DbContext, IInventoryUnitOfWork
{
    /// <summary>The PostgreSQL schema this context owns.</summary>
    public const string SchemaName = "inventory";

    private readonly ITenantContext? _tenantContext;
    private readonly IDomainEventDispatcher? _domainEventDispatcher;

    /// <summary>Initializes the context.</summary>
    /// <param name="options">EF Core options, supplied by the container.</param>
    /// <param name="tenantContext">The active tenant, used by the global query filters.</param>
    /// <param name="domainEventDispatcher">Dispatches domain events after a successful commit.</param>
    public InventoryDbContext(
        DbContextOptions<InventoryDbContext> options,
        ITenantContext? tenantContext = null,
        IDomainEventDispatcher? domainEventDispatcher = null)
        : base(options)
    {
        _tenantContext = tenantContext;
        _domainEventDispatcher = domainEventDispatcher;
    }

    /// <summary>Stock balances, one row per part per warehouse.</summary>
    public DbSet<StockItem> StockItems => Set<StockItem>();

    /// <summary>The append-only stock ledger.</summary>
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();

    /// <summary>Physical locations stock lives in.</summary>
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();

    /// <summary>Named places inside a warehouse.</summary>
    public DbSet<StorageBin> StorageBins => Set<StorageBin>();

    /// <summary>The tenant every query is scoped to.</summary>
    internal Guid CurrentTenantId => _tenantContext?.TenantId ?? Guid.Empty;

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

    /// <inheritdoc />
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        IHasDomainEvents[] aggregates = [.. ChangeTracker
            .Entries()
            .Select(entry => entry.Entity)
            .OfType<IHasDomainEvents>()
            .Where(aggregate => aggregate.DomainEvents.Count > 0)];

        IDomainEvent[] domainEvents = [.. aggregates.SelectMany(aggregate => aggregate.DomainEvents)];

        foreach (IHasDomainEvents aggregate in aggregates)
        {
            aggregate.ClearDomainEvents();
        }

        int written = await base.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (domainEvents.Length > 0 && _domainEventDispatcher is not null)
        {
            await _domainEventDispatcher.DispatchAsync(domainEvents, cancellationToken).ConfigureAwait(false);
        }

        return written;
    }
}
