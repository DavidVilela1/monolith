using AutoPartsErp.Modules.Pricing.Domain;
using AutoPartsErp.Modules.Pricing.Domain.Customers;
using AutoPartsErp.Modules.Pricing.Domain.PriceLists;
using AutoPartsErp.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AutoPartsErp.Modules.Pricing.Infrastructure.Persistence;

/// <summary>
/// The Pricing module's database context, scoped to the <c>pricing</c> schema.
/// <para>
/// It maps no Catalog table and no Sales table. A part and a customer are bare Guid columns here,
/// so the database cannot enforce that they exist — the deliberate trade every module in this
/// system makes, because a cross-schema constraint would tie them to one deployment forever.
/// </para>
/// <para>
/// No split-query default, unlike Catalog and Sales. The only owned collection in this schema is
/// a handful of quantity breaks per price, and splitting that into a second round trip would cost
/// more than the row multiplication it avoids.
/// </para>
/// </summary>
public sealed class PricingDbContext : ModuleDbContext, IPricingUnitOfWork
{
    /// <summary>The PostgreSQL schema this context owns.</summary>
    public const string SchemaName = "pricing";

    /// <summary>Initializes the context.</summary>
    /// <param name="options">EF Core options, supplied by the container.</param>
    /// <param name="dependencies">
    /// Shared plumbing: the tenant, the domain event dispatcher and the outbox. Optional so the
    /// design-time tooling can build the model with no container behind it.
    /// </param>
    public PricingDbContext(
        DbContextOptions<PricingDbContext> options,
        ModuleDbContextDependencies? dependencies = null)
        : base(options, dependencies)
    {
    }

    /// <summary>The lists themselves.</summary>
    public DbSet<PriceList> PriceLists => Set<PriceList>();

    /// <summary>What each list says each part costs.</summary>
    public DbSet<PriceListEntry> PriceListEntries => Set<PriceListEntry>();

    /// <summary>What was agreed with each customer.</summary>
    public DbSet<CustomerPricing> CustomerAgreements => Set<CustomerPricing>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PricingDbContext).Assembly);

        modelBuilder.Entity<PriceList>()
            .HasQueryFilter(list => !list.IsDeleted && list.TenantId == CurrentTenantId);

        modelBuilder.Entity<PriceListEntry>()
            .HasQueryFilter(entry => !entry.IsDeleted && entry.TenantId == CurrentTenantId);

        modelBuilder.Entity<CustomerPricing>()
            .HasQueryFilter(agreement => !agreement.IsDeleted && agreement.TenantId == CurrentTenantId);

        base.OnModelCreating(modelBuilder);
    }
}
