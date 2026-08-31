using AutoPartsErp.SharedKernel.Abstractions;
using AutoPartsErp.SharedKernel.Primitives;
using Microsoft.EntityFrameworkCore;

namespace AutoPartsErp.Persistence;

/// <summary>
/// The base every module's database context derives from.
/// <para>
/// It carries the two things all of them need and none of them should reimplement: the tenant
/// the query filters scope to, and the collect-then-dispatch of domain events around a commit.
/// That second part is subtle enough to be worth writing once — events must be gathered
/// <i>before</i> the write and dispatched <i>after</i> it, so a handler can never observe state
/// that later rolls back, and the aggregates must be cleared regardless of what the handlers do.
/// </para>
/// <para>
/// What stays in each module: its <c>DbSet</c>s, its schema name, its mappings and its query
/// filters. This class deliberately knows about no entity type at all.
/// </para>
/// </summary>
public abstract class ModuleDbContext : DbContext
{
    private readonly ITenantContext? _tenantContext;
    private readonly IDomainEventDispatcher? _domainEventDispatcher;

    /// <summary>Initializes the context.</summary>
    /// <param name="options">EF Core options, supplied by the container.</param>
    /// <param name="tenantContext">
    /// The active tenant. Nullable so design-time tooling can build the model without a request.
    /// </param>
    /// <param name="domainEventDispatcher">Dispatches domain events after a successful commit.</param>
    protected ModuleDbContext(
        DbContextOptions options,
        ITenantContext? tenantContext,
        IDomainEventDispatcher? domainEventDispatcher)
        : base(options)
    {
        _tenantContext = tenantContext;
        _domainEventDispatcher = domainEventDispatcher;
    }

    /// <summary>
    /// The tenant every query is scoped to. Referenced by each module's global query filters.
    /// Falls back to <see cref="Guid.Empty"/> at design time, which matches no data — the safe
    /// direction for a filter to fail in.
    /// </summary>
    protected Guid CurrentTenantId => _tenantContext?.TenantId ?? Guid.Empty;

    /// <inheritdoc />
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        IHasDomainEvents[] aggregates = [.. ChangeTracker
            .Entries()
            .Select(entry => entry.Entity)
            .OfType<IHasDomainEvents>()
            .Where(aggregate => aggregate.DomainEvents.Count > 0)];

        IDomainEvent[] domainEvents = [.. aggregates.SelectMany(aggregate => aggregate.DomainEvents)];

        // Cleared before the write, not after: if dispatch throws, the events must not be sitting
        // on the aggregates waiting to fire a second time on the next SaveChanges.
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
