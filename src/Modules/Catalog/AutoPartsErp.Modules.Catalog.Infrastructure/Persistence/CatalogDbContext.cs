using AutoPartsErp.Modules.Catalog.Domain;
using AutoPartsErp.Modules.Catalog.Domain.Brands;
using AutoPartsErp.Modules.Catalog.Domain.Categories;
using AutoPartsErp.Modules.Catalog.Domain.Parts;
using AutoPartsErp.SharedKernel.Abstractions;
using AutoPartsErp.SharedKernel.Primitives;
using Microsoft.EntityFrameworkCore;

namespace AutoPartsErp.Modules.Catalog.Infrastructure.Persistence;

/// <summary>
/// The Catalog module's database context, scoped to the <c>catalog</c> schema.
/// <para>
/// Each module owns exactly one context and exactly one schema. Nothing outside this project
/// may reference it, and it maps no other module's tables. That single rule is what makes the
/// module boundary real at the database level as well as in the project graph, and it is what
/// makes extracting a module into its own service later a mechanical job.
/// </para>
/// </summary>
public sealed class CatalogDbContext : DbContext, ICatalogUnitOfWork
{
    /// <summary>The PostgreSQL schema this context owns.</summary>
    public const string SchemaName = "catalog";

    private readonly ITenantContext? _tenantContext;
    private readonly IDomainEventDispatcher? _domainEventDispatcher;

    /// <summary>Initializes the context.</summary>
    /// <param name="options">EF Core options, supplied by the container.</param>
    /// <param name="tenantContext">The active tenant, used by the global query filters.</param>
    /// <param name="domainEventDispatcher">Dispatches domain events after a successful commit.</param>
    public CatalogDbContext(
        DbContextOptions<CatalogDbContext> options,
        ITenantContext? tenantContext = null,
        IDomainEventDispatcher? domainEventDispatcher = null)
        : base(options)
    {
        _tenantContext = tenantContext;
        _domainEventDispatcher = domainEventDispatcher;
    }

    /// <summary>Parts in the catalogue.</summary>
    public DbSet<Part> Parts => Set<Part>();

    /// <summary>Brands.</summary>
    public DbSet<Brand> Brands => Set<Brand>();

    /// <summary>Categories in the product hierarchy.</summary>
    public DbSet<PartCategory> Categories => Set<PartCategory>();

    /// <summary>The tenant every query is scoped to.</summary>
    internal Guid CurrentTenantId => _tenantContext?.TenantId ?? Guid.Empty;

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CatalogDbContext).Assembly);

        ApplyGlobalFilters(modelBuilder);

        base.OnModelCreating(modelBuilder);
    }

    /// <summary>
    /// Scopes every query to the current tenant and hides archived rows.
    /// <para>
    /// These filters are the difference between multi-company support that works and multi-company
    /// support that leaks. A developer who forgets a tenant predicate in a new query still gets
    /// the right rows, because the predicate is part of the model rather than part of each query.
    /// Use <c>IgnoreQueryFilters()</c> deliberately when an administrative screen genuinely needs
    /// to see archived records.
    /// </para>
    /// </summary>
    private void ApplyGlobalFilters(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Part>()
            .HasQueryFilter(part => !part.IsDeleted && part.TenantId == CurrentTenantId);

        modelBuilder.Entity<Brand>()
            .HasQueryFilter(brand => !brand.IsDeleted && brand.TenantId == CurrentTenantId);

        modelBuilder.Entity<PartCategory>()
            .HasQueryFilter(category => !category.IsDeleted && category.TenantId == CurrentTenantId);
    }

    /// <summary>
    /// Commits the transaction, then dispatches the domain events raised along the way.
    /// Events are collected before the write and dispatched after it, so a handler can never
    /// observe state that later rolls back.
    /// </summary>
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
