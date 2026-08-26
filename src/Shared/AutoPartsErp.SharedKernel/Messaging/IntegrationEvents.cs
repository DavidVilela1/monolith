namespace AutoPartsErp.SharedKernel.Messaging;

/// <summary>
/// A fact published by one module for other modules to react to.
/// <para>
/// Integration events are the <b>only</b> way modules talk to each other about things that
/// have happened. Catalog does not reference Inventory; it publishes
/// <c>PartActivatedIntegrationEvent</c> and Inventory decides what that means for stock records.
/// Today the bus is in-process; swapping it for RabbitMQ or Azure Service Bus later is a
/// change of one registration, not a change of every module.
/// </para>
/// </summary>
public interface IIntegrationEvent
{
    /// <summary>Unique identity of this occurrence, used for idempotent consumption.</summary>
    Guid EventId { get; }

    /// <summary>The instant the event was published, in UTC.</summary>
    DateTimeOffset OccurredAtUtc { get; }
}

/// <summary>Convenience base record for integration events.</summary>
public abstract record IntegrationEvent : IIntegrationEvent
{
    /// <inheritdoc />
    public Guid EventId { get; init; } = Guid.NewGuid();

    /// <inheritdoc />
    public DateTimeOffset OccurredAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>Consumes an integration event published by another module.</summary>
/// <typeparam name="TEvent">The event consumed.</typeparam>
public interface IIntegrationEventHandler<in TEvent>
    where TEvent : IIntegrationEvent
{
    /// <summary>Reacts to the event. Must be idempotent: it may be delivered more than once.</summary>
    Task HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken = default);
}

/// <summary>Publishes integration events to every interested module.</summary>
public interface IEventBus
{
    /// <summary>Publishes an event to all registered handlers.</summary>
    Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
        where TEvent : IIntegrationEvent;
}
