using AutoPartsErp.SharedKernel.Messaging;

namespace AutoPartsErp.Modules.Abstractions.Events;

/// <summary>
/// Records the intent to publish an integration event, without publishing it.
/// <para>
/// The event goes into a queue that the module's <c>DbContext</c> drains into outbox rows during
/// the same <c>SaveChanges</c> as the change that raised it. Delivery happens afterwards, from
/// the table, in a background sweep.
/// </para>
/// <para>
/// This replaces a bus that called handlers directly. That version was simpler and wrong in a
/// specific way: a handler that threw was logged and forgotten, and the publisher's transaction
/// had already committed, so the two modules were left disagreeing with only a log line between
/// them. Nothing changes for callers — they still see <see cref="IEventBus"/> and still publish
/// one line — which is the entire reason the interface was there.
/// </para>
/// </summary>
public sealed class OutboxEventBus : IEventBus
{
    private readonly IIntegrationEventQueue _queue;

    /// <summary>Initializes the bus.</summary>
    public OutboxEventBus(IIntegrationEventQueue queue)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
    }

    /// <inheritdoc />
    public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
        where TEvent : IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        _queue.Enqueue(integrationEvent);

        return Task.CompletedTask;
    }
}
