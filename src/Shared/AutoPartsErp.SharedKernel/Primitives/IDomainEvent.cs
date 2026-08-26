namespace AutoPartsErp.SharedKernel.Primitives;

/// <summary>
/// Something that happened inside a single aggregate, expressed in the language of the domain.
/// Domain events are raised while the aggregate is mutated and dispatched by the unit of work
/// once the surrounding transaction commits, so handlers never observe uncommitted state.
/// </summary>
public interface IDomainEvent
{
    /// <summary>Unique identity of this occurrence, used for idempotency and tracing.</summary>
    Guid EventId { get; }

    /// <summary>The instant the event was raised, in UTC.</summary>
    DateTimeOffset OccurredAtUtc { get; }
}

/// <summary>
/// Convenience base record for domain events. Derive with a positional record, for example:
/// <c>public sealed record PartActivatedDomainEvent(PartId PartId) : DomainEvent;</c>
/// </summary>
public abstract record DomainEvent : IDomainEvent
{
    /// <inheritdoc />
    public Guid EventId { get; init; } = Guid.NewGuid();

    /// <inheritdoc />
    public DateTimeOffset OccurredAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>Handles a domain event after the transaction that produced it has committed.</summary>
/// <typeparam name="TEvent">The event type handled.</typeparam>
public interface IDomainEventHandler<in TEvent>
    where TEvent : IDomainEvent
{
    /// <summary>Reacts to the event.</summary>
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken = default);
}
