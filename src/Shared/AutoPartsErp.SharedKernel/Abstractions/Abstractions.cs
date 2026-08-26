using AutoPartsErp.SharedKernel.Primitives;

namespace AutoPartsErp.SharedKernel.Abstractions;

/// <summary>
/// Commits every change made during one logical operation as a single transaction,
/// then dispatches the domain events raised along the way.
/// One request equals one unit of work.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>Persists all pending changes and dispatches domain events.</summary>
    /// <returns>The number of state entries written.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Read and write access to a single aggregate type. Repositories are defined per
/// aggregate root; child entities are reached through their root, never directly.
/// </summary>
/// <typeparam name="TAggregate">The aggregate root type.</typeparam>
/// <typeparam name="TId">The root's identifier type.</typeparam>
public interface IRepository<TAggregate, in TId>
    where TAggregate : AggregateRoot<TId>
    where TId : notnull
{
    /// <summary>Loads an aggregate by identity, or null when it does not exist.</summary>
    Task<TAggregate?> GetByIdAsync(TId id, CancellationToken cancellationToken = default);

    /// <summary>True when an aggregate with this identity exists.</summary>
    Task<bool> ExistsAsync(TId id, CancellationToken cancellationToken = default);

    /// <summary>Stages a new aggregate for insertion on the next commit.</summary>
    void Add(TAggregate aggregate);

    /// <summary>Stages an aggregate for removal on the next commit.</summary>
    void Remove(TAggregate aggregate);
}

/// <summary>
/// Supplies the current time. Injected rather than calling <see cref="DateTimeOffset.UtcNow"/>
/// directly so that period-end, ageing and expiry logic can be tested deterministically.
/// </summary>
public interface IDateTimeProvider
{
    /// <summary>The current instant in UTC.</summary>
    DateTimeOffset UtcNow { get; }

    /// <summary>Today's date in UTC.</summary>
    DateOnly TodayUtc { get; }
}

/// <summary>Default implementation backed by the system clock.</summary>
public sealed class SystemDateTimeProvider : IDateTimeProvider
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    /// <inheritdoc />
    public DateOnly TodayUtc => DateOnly.FromDateTime(DateTime.UtcNow);
}

/// <summary>
/// The tenant (legal entity / operating company) the current request belongs to.
/// Resolved once per request from the authenticated principal or a header, then used by
/// global query filters so no query can accidentally span companies.
/// </summary>
public interface ITenantContext
{
    /// <summary>The active tenant.</summary>
    Guid TenantId { get; }

    /// <summary>Human-readable tenant code, for logs and document numbering.</summary>
    string TenantCode { get; }
}

/// <summary>The identity performing the current operation, used for auditing and permissions.</summary>
public interface ICurrentUser
{
    /// <summary>Stable user identifier, or "system" for background work.</summary>
    string UserId { get; }

    /// <summary>Display name for audit trails.</summary>
    string UserName { get; }

    /// <summary>True when a real user is authenticated, false for background or anonymous work.</summary>
    bool IsAuthenticated { get; }
}

/// <summary>Dispatches domain events collected from aggregates after a successful commit.</summary>
public interface IDomainEventDispatcher
{
    /// <summary>Dispatches the supplied events to their handlers.</summary>
    Task DispatchAsync(IReadOnlyCollection<IDomainEvent> domainEvents, CancellationToken cancellationToken = default);
}
