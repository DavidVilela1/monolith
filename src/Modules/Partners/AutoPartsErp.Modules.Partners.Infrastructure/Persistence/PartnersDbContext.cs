using AutoPartsErp.Modules.Partners.Domain;
using AutoPartsErp.Modules.Partners.Domain.Partners;
using AutoPartsErp.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AutoPartsErp.Modules.Partners.Infrastructure.Persistence;

/// <summary>The Partners module's database context, scoped to the <c>partners</c> schema.</summary>
public sealed class PartnersDbContext : ModuleDbContext, IPartnersUnitOfWork
{
    /// <summary>The PostgreSQL schema this context owns.</summary>
    public const string SchemaName = "partners";

    /// <summary>Initializes the context.</summary>
    /// <param name="options">EF Core options, supplied by the container.</param>
    /// <param name="dependencies">
    /// Shared plumbing: the tenant, the domain event dispatcher and the outbox. Optional so the
    /// design-time tooling can build the model with no container behind it.
    /// </param>
    public PartnersDbContext(
        DbContextOptions<PartnersDbContext> options,
        ModuleDbContextDependencies? dependencies = null)
        : base(options, dependencies)
    {
    }

    /// <summary>Customers and suppliers.</summary>
    public DbSet<Partner> Partners => Set<Partner>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PartnersDbContext).Assembly);

        modelBuilder.Entity<Partner>()
            .HasQueryFilter(partner => !partner.IsDeleted && partner.TenantId == CurrentTenantId);

        base.OnModelCreating(modelBuilder);
    }
}
