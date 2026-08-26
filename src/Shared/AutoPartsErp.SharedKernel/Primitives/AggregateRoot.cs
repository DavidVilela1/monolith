namespace AutoPartsErp.SharedKernel.Primitives;

/// <summary>
/// Non-generic view of an aggregate's pending domain events.
/// The persistence layer needs to collect events from every changed aggregate without knowing
/// what identifier type each one uses, and this is the seam that lets it.
/// </summary>
public interface IHasDomainEvents
{
    /// <summary>Events raised since the aggregate was loaded, in the order they occurred.</summary>
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }

    /// <summary>Clears the pending events. Called by the unit of work after dispatch.</summary>
    void ClearDomainEvents();
}

/// <summary>
/// The entry point to a consistency boundary. Everything inside an aggregate is loaded,
/// changed and saved together; anything outside it is referenced by identity only.
/// Repositories are defined per aggregate root, never per child entity.
/// </summary>
/// <typeparam name="TId">The strongly typed identifier of the root.</typeparam>
public abstract class AggregateRoot<TId> : Entity<TId>, IHasDomainEvents
    where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];

    /// <summary>Initializes a new aggregate root with the supplied identity.</summary>
    protected AggregateRoot(TId id)
        : base(id)
    {
    }

    /// <summary>Required by EF Core materialization.</summary>
    protected AggregateRoot()
    {
    }

    /// <summary>
    /// Optimistic concurrency token. Mapped to <c>xmin</c> on PostgreSQL, so two users editing
    /// the same part cannot silently overwrite each other.
    /// </summary>
    public uint Version { get; protected set; }

    /// <summary>Events raised since the aggregate was loaded, in the order they occurred.</summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    /// <summary>Records that something domain-meaningful happened. Call from inside behaviour methods.</summary>
    protected void Raise(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _domainEvents.Add(domainEvent);
    }

    /// <summary>Clears the pending events. Called by the unit of work after dispatch.</summary>
    public void ClearDomainEvents() => _domainEvents.Clear();
}
