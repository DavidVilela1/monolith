using AutoPartsErp.Modules.Partners.Domain;
using AutoPartsErp.Modules.Partners.Domain.Partners;
using AutoPartsErp.Persistence;
using AutoPartsErp.SharedKernel.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace AutoPartsErp.Modules.Partners.Infrastructure.Persistence;

/// <summary>The Partners module's database context, scoped to the <c>partners</c> schema.</summary>
public sealed class PartnersDbContext : ModuleDbContext, IPartnersUnitOfWork
{
    /// <summary>The PostgreSQL schema this context owns.</summary>
    public const string SchemaName = "partners";

    /// <summary>Initializes the context.</summary>
    /// <param name="options">EF Core options, supplied by the container.</param>
    /// <param name="tenantContext">The active tenant, used by the global query filters.</param>
    /// <param name="domainEventDispatcher">Dispatches domain events after a successful commit.</param>
    public PartnersDbContext(
        DbContextOptions<PartnersDbContext> options,
        ITenantContext? tenantContext = null,
        IDomainEventDispatcher? domainEventDispatcher = null)
        : base(options, tenantContext, domainEventDispatcher)
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
