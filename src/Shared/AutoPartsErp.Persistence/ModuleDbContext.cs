using AutoPartsErp.Persistence.Inbox;
using AutoPartsErp.Persistence.Outbox;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Primitives;
using Microsoft.EntityFrameworkCore;

namespace AutoPartsErp.Persistence;

/// <summary>
/// The base every module's database context derives from.
/// <para>
/// It carries what all of them need and none of them should reimplement: the tenant the query
/// filters scope to, the outbox and inbox tables, and the event handling around a commit.
/// </para>
/// <para>
/// <b>The ordering here is the whole mechanism, so it is worth reading slowly.</b> Domain events
/// are dispatched <i>before</i> the write, not after. Their handlers translate them into
/// integration events, which are queued rather than published, and the queue is drained into
/// outbox rows on this same context. One <c>SaveChanges</c> therefore commits the business
/// change and the record of what to announce about it, together or not at all.
/// </para>
/// <para>
/// That is a deliberate reversal of what this class used to do. Dispatching after the commit
/// meant handlers observed only persisted state, which was the nicer guarantee — but it also
/// meant the announcement lived outside the transaction, and a process that died in the gap lost
/// the fact with nothing to replay from. Handlers now see uncommitted state, which is safe
/// precisely because the only thing they are allowed to do is queue an integration event: they
/// take no decisions and touch no other aggregate. Anything more ambitious in a domain event
/// handler is now a bug.
/// </para>
/// <para>
/// What stays in each module: its <c>DbSet</c>s, its schema name, its mappings and its query
/// filters. This class still knows about no business entity at all.
/// </para>
/// </summary>
public abstract class ModuleDbContext : DbContext
{
    private readonly ModuleDbContextDependencies _dependencies;
    private bool _inboxRecorded;

    /// <summary>Initializes the context.</summary>
    /// <param name="options">EF Core options, supplied by the container.</param>
    /// <param name="dependencies">
    /// Shared plumbing. Null at design time, where the model is built with no container.
    /// </param>
    protected ModuleDbContext(DbContextOptions options, ModuleDbContextDependencies? dependencies)
        : base(options)
    {
        _dependencies = dependencies ?? new ModuleDbContextDependencies();
    }

    /// <summary>Integration events committed by this module and awaiting delivery.</summary>
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    /// <summary>Messages this module's handlers have already dealt with.</summary>
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    /// <summary>
    /// The tenant every query is scoped to. Referenced by each module's global query filters.
    /// Falls back to <see cref="Guid.Empty"/> at design time, which matches no data — the safe
    /// direction for a filter to fail in.
    /// </summary>
    protected Guid CurrentTenantId => _dependencies.TenantContext?.TenantId ?? Guid.Empty;

    /// <inheritdoc />
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // 1. If this save is happening inside an event handler, has that handler already dealt
        //    with this message? Checking here rather than before the handler runs is what makes
        //    the answer and the work atomic: both are in the transaction below.
        if (await AlreadyHandledAsync(cancellationToken).ConfigureAwait(false))
        {
            // A redelivery. Throw the handler's work away rather than applying it twice; the
            // processor will mark the message processed and move on.
            ChangeTracker.Clear();
            return 0;
        }

        // 2. Collect and clear domain events. Cleared before anything else can throw, so a
        //    failure cannot leave them sitting on the aggregate to fire again on the next save.
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

        // 3. Dispatch them now, before the write, so anything they publish lands in the outbox
        //    inside this transaction.
        if (domainEvents.Length > 0 && _dependencies.DomainEventDispatcher is not null)
        {
            await _dependencies.DomainEventDispatcher
                .DispatchAsync(domainEvents, cancellationToken)
                .ConfigureAwait(false);
        }

        // 4. Drain whatever they queued into rows.
        WriteOutboxMessages();

        return await base.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        base.OnModelCreating(modelBuilder);

        // Applied after the module's own configurations, so both tables pick up the default
        // schema the module has already set. Every module gets its own pair.
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new InboxMessageConfiguration());
    }

    private async Task<bool> AlreadyHandledAsync(CancellationToken cancellationToken)
    {
        IntegrationEventScope? scope = _dependencies.EventScope;

        if (scope?.IsHandling != true)
        {
            return false;
        }

        // A handler must be exactly one unit of work. If it saves twice, the inbox row commits
        // with the first save and the rest commits separately - so a crash in between would
        // leave the message marked handled with half the work applied, which is precisely the
        // failure the inbox exists to prevent, arrived at quietly. Better to refuse.
        if (_inboxRecorded)
        {
            throw new InvalidOperationException(
                $"'{scope.HandlerName}' called SaveChangesAsync more than once while handling " +
                $"message {scope.MessageId}. An integration event handler must do its work in a " +
                "single unit of work, so that recording the message as handled and the work " +
                "itself commit together.");
        }

        Guid messageId = scope.MessageId!.Value;
        string handlerName = scope.HandlerName!;

        bool seen = await InboxMessages
            .AsNoTracking()
            .AnyAsync(
                message => message.MessageId == messageId && message.HandlerName == handlerName,
                cancellationToken)
            .ConfigureAwait(false);

        if (seen)
        {
            return true;
        }

        InboxMessages.Add(new InboxMessage
        {
            MessageId = messageId,
            HandlerName = handlerName,
            HandledAtUtc = Now,
        });

        _inboxRecorded = true;

        return false;
    }

    private void WriteOutboxMessages()
    {
        IIntegrationEventQueue? queue = _dependencies.IntegrationEvents;
        IIntegrationEventSerializer? serializer = _dependencies.Serializer;

        if (queue is null || serializer is null || queue.Count == 0)
        {
            return;
        }

        Guid tenantId = CurrentTenantId;

        foreach (IIntegrationEvent integrationEvent in queue.Drain())
        {
            OutboxMessages.Add(new OutboxMessage
            {
                Id = integrationEvent.EventId,
                Type = serializer.GetTypeName(integrationEvent),
                Content = serializer.Serialize(integrationEvent),
                TenantId = tenantId,
                OccurredAtUtc = integrationEvent.OccurredAtUtc,
            });
        }
    }

    private DateTimeOffset Now => _dependencies.Clock?.UtcNow ?? DateTimeOffset.UtcNow;
}
