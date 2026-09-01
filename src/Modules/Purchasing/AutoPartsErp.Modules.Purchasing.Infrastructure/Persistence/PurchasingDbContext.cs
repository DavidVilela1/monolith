using AutoPartsErp.Modules.Purchasing.Domain;
using AutoPartsErp.Modules.Purchasing.Domain.Orders;
using AutoPartsErp.Modules.Purchasing.Domain.Replenishment;
using AutoPartsErp.Persistence;
using AutoPartsErp.SharedKernel.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace AutoPartsErp.Modules.Purchasing.Infrastructure.Persistence;

/// <summary>
/// The Purchasing module's database context, scoped to the <c>purchasing</c> schema.
/// <para>
/// It maps no Partners table, no Catalog table and no Inventory table, and holds no foreign key
/// into one. A supplier, a part and a warehouse are all bare Guid columns here. The database
/// therefore cannot enforce that they exist — which is the deliberate trade: a cross-schema
/// constraint would tie four modules to one deployment forever.
/// </para>
/// </summary>
public sealed class PurchasingDbContext : ModuleDbContext, IPurchasingUnitOfWork
{
    /// <summary>The PostgreSQL schema this context owns.</summary>
    public const string SchemaName = "purchasing";

    /// <summary>Initializes the context.</summary>
    /// <param name="options">EF Core options, supplied by the container.</param>
    /// <param name="tenantContext">The active tenant, used by the global query filters.</param>
    /// <param name="domainEventDispatcher">Dispatches domain events after a successful commit.</param>
    public PurchasingDbContext(
        DbContextOptions<PurchasingDbContext> options,
        ITenantContext? tenantContext = null,
        IDomainEventDispatcher? domainEventDispatcher = null)
        : base(options, tenantContext, domainEventDispatcher)
    {
    }

    /// <summary>Purchase orders, with their lines.</summary>
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();

    /// <summary>Parts that have run low and probably need buying.</summary>
    public DbSet<ReplenishmentSuggestion> ReplenishmentSuggestions => Set<ReplenishmentSuggestion>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PurchasingDbContext).Assembly);

        modelBuilder.Entity<PurchaseOrder>()
            .HasQueryFilter(order => !order.IsDeleted && order.TenantId == CurrentTenantId);

        modelBuilder.Entity<ReplenishmentSuggestion>()
            .HasQueryFilter(suggestion => suggestion.TenantId == CurrentTenantId);

        base.OnModelCreating(modelBuilder);
    }
}
