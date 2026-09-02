using AutoPartsErp.Modules.Sales.Domain;
using AutoPartsErp.Modules.Sales.Domain.Customers;
using AutoPartsErp.Modules.Sales.Domain.Orders;
using AutoPartsErp.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AutoPartsErp.Modules.Sales.Infrastructure.Persistence;

/// <summary>
/// The Sales module's database context, scoped to the <c>sales</c> schema.
/// <para>
/// The customer accounts table is the interesting one. It looks like a copy of
/// <c>partners.partners</c> and is not: it holds only what the counter needs to answer "may this
/// account order, and for how much", it is written solely by event handlers, and it carries a
/// credit exposure figure that exists nowhere else in the system. Partners could be extracted
/// into its own service tomorrow and this table would not change.
/// </para>
/// </summary>
public sealed class SalesDbContext : ModuleDbContext, ISalesUnitOfWork
{
    /// <summary>The PostgreSQL schema this context owns.</summary>
    public const string SchemaName = "sales";

    /// <summary>Initializes the context.</summary>
    /// <param name="options">EF Core options, supplied by the container.</param>
    /// <param name="dependencies">
    /// Shared plumbing: the tenant, the domain event dispatcher and the outbox. Optional so the
    /// design-time tooling can build the model with no container behind it.
    /// </param>
    public SalesDbContext(
        DbContextOptions<SalesDbContext> options,
        ModuleDbContextDependencies? dependencies = null)
        : base(options, dependencies)
    {
    }

    /// <summary>Sales orders, with their lines.</summary>
    public DbSet<SalesOrder> SalesOrders => Set<SalesOrder>();

    /// <summary>What Sales knows about each customer.</summary>
    public DbSet<CustomerAccount> CustomerAccounts => Set<CustomerAccount>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SalesDbContext).Assembly);

        modelBuilder.Entity<SalesOrder>()
            .HasQueryFilter(order => !order.IsDeleted && order.TenantId == CurrentTenantId);

        modelBuilder.Entity<CustomerAccount>()
            .HasQueryFilter(account => account.TenantId == CurrentTenantId);

        base.OnModelCreating(modelBuilder);
    }
}
