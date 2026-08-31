using AutoPartsErp.Modules.Partners.Domain;
using AutoPartsErp.Modules.Partners.Domain.Partners;
using AutoPartsErp.SharedKernel.Abstractions;
using AutoPartsErp.SharedKernel.Primitives;
using Microsoft.EntityFrameworkCore;

namespace AutoPartsErp.Modules.Partners.Infrastructure.Persistence;

/// <summary>The Partners module's database context, scoped to the <c>partners</c> schema.</summary>
public sealed class PartnersDbContext : DbContext, IPartnersUnitOfWork
{
    /// <summary>The PostgreSQL schema this context owns.</summary>
    public const string SchemaName = "partners";

    private readonly ITenantContext? _tenantContext;
    private readonly IDomainEventDispatcher? _domainEventDispatcher;

    /// <summary>Initializes the context.</summary>
    /// <param name="options">EF Core options, supplied by the container.</param>
    /// <param name="tenantContext">The active tenant, used by the global query filters.</param>
    /// <param name="domainEventDispatcher">Dispatches domain events after a successful commit.</param>
    public PartnersDbContext(
        DbContextOptions<PartnersDbContext> options,
        ITenantContext? tenantContext = null,
        IDomainEventDispatcher? domainEventDispatcher = null)
        : base(options)
    {
        _tenantContext = tenantContext;
        _domainEventDispatcher = domainEventDispatcher;
    }

    /// <summary>Customers and suppliers.</summary>
    public DbSet<Partner> Partners => Set<Partner>();

    /// <summary>The tenant every query is scoped to.</summary>
    internal Guid CurrentTenantId => _tenantContext?.TenantId ?? Guid.Empty;

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
