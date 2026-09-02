namespace AutoPartsErp.SharedKernel.Messaging;

/// <summary>
/// Collects the integration events raised during one unit of work so they can be written to the
/// outbox in the same transaction as the change that caused them.
/// <para>
/// This is the whole point of an outbox. Publishing after a commit means a crash in between
/// loses the fact: the purchase order says the goods arrived and the stock never moved, with
/// nothing left to replay from. Publishing before the commit means announcing something that
/// might roll back. Writing the event to a table inside the same transaction is the only option
/// that cannot produce either, because the row and the change succeed or fail together.
/// </para>
/// <para>
/// Scoped: one queue per unit of work, drained by the module's <c>DbContext</c> during save.
/// </para>
/// </summary>
public interface IIntegrationEventQueue
{
    /// <summary>Events waiting to be written to the outbox.</summary>
    int Count { get; }

    /// <summary>Adds an event to be written when the current unit of work commits.</summary>
    void Enqueue(IIntegrationEvent integrationEvent);

    /// <summary>Removes and returns everything queued so far.</summary>
    IReadOnlyList<IIntegrationEvent> Drain();
}

/// <summary>The default in-memory queue. One instance per unit of work.</summary>
public sealed class IntegrationEventQueue : IIntegrationEventQueue
{
    private readonly List<IIntegrationEvent> _events = [];

    /// <inheritdoc />
    public int Count => _events.Count;

    /// <inheritdoc />
    public void Enqueue(IIntegrationEvent integrationEvent)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        _events.Add(integrationEvent);
    }

    /// <inheritdoc />
    public IReadOnlyList<IIntegrationEvent> Drain()
    {
        if (_events.Count == 0)
        {
            return [];
        }

        IIntegrationEvent[] drained = [.. _events];
        _events.Clear();

        return drained;
    }
}

/// <summary>
/// Turns an integration event into something a database column can hold, and back again.
/// <para>
/// The stored type name is a contract in its own right: rename a published event record and
/// every unprocessed row referring to it becomes undeliverable. That is a good reason to treat
/// the contracts assembly as append-only.
/// </para>
/// </summary>
public interface IIntegrationEventSerializer
{
    /// <summary>The stable name recorded against a stored event.</summary>
    string GetTypeName(IIntegrationEvent integrationEvent);

    /// <summary>Serializes an event for storage.</summary>
    string Serialize(IIntegrationEvent integrationEvent);

    /// <summary>
    /// Rebuilds a stored event, or null when no contract of that name is loaded — which means
    /// a published event type was renamed or removed while rows still referenced it.
    /// </summary>
    IIntegrationEvent? Deserialize(string typeName, string content);
}

/// <summary>
/// Delivers one integration event to every handler subscribed to it.
/// <para>
/// Separate from <see cref="IEventBus"/> on purpose. Publishing and delivering used to be the
/// same call, which is exactly why a failed handler could lose an event: there was nowhere for
/// the fact to wait. Now <see cref="IEventBus"/> only records the intent to publish, and this
/// does the work, later, from a row that survives a restart.
/// </para>
/// </summary>
public interface IIntegrationEventDispatcher
{
    /// <summary>
    /// Invokes every handler for the event, each in its own scope.
    /// Throws if any handler throws, so the caller can decide whether to retry.
    /// </summary>
    Task DispatchAsync(IntegrationEventEnvelope envelope, CancellationToken cancellationToken = default);
}

/// <summary>
/// A stored event together with the two things a handler cannot work out for itself.
/// <para>
/// The tenant travels with the message because there is no request to read it from any more.
/// This was a latent bug for as long as handlers ran inside the publisher's HTTP call and
/// quietly inherited its tenant; moving delivery to a background loop is what turns it into a
/// real one, and carrying it explicitly is the fix.
/// </para>
/// </summary>
/// <param name="MessageId">The outbox row's identity, used by consumers to deduplicate.</param>
/// <param name="TenantId">The tenant the event belongs to.</param>
/// <param name="Event">The event itself.</param>
public sealed record IntegrationEventEnvelope(Guid MessageId, Guid TenantId, IIntegrationEvent Event);

/// <summary>
/// Identifies the integration event a unit of work is currently handling, so the consuming
/// module can record that it has seen it.
/// <para>
/// Set by the outbox processor around each handler call and read by the module's
/// <c>DbContext</c>, which writes the inbox row in the same transaction as the handler's own
/// changes. That atomicity is the point: a consumer that marked a message handled in a separate
/// transaction from the work would still double-apply it after a crash in between.
/// </para>
/// <para>
/// Scoped, and null outside of event handling — an ordinary HTTP request writes no inbox row.
/// </para>
/// </summary>
public sealed class IntegrationEventScope
{
    /// <summary>The message being handled, or null outside of event handling.</summary>
    public Guid? MessageId { get; set; }

    /// <summary>The handler processing it.</summary>
    public string? HandlerName { get; set; }

    /// <summary>True while a handler is running under the outbox processor.</summary>
    public bool IsHandling => MessageId is not null && HandlerName is not null;

    /// <summary>Clears the scope.</summary>
    public void Clear()
    {
        MessageId = null;
        HandlerName = null;
    }
}
