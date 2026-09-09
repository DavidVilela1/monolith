using System.Data;
using System.Data.Common;
using System.Globalization;
using AutoPartsErp.Persistence.Inbox;
using AutoPartsErp.Persistence.Numbering;
using AutoPartsErp.Persistence.Outbox;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

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
    /// The module's document number counters. Mapped so the table exists; never queried from C#.
    /// <para>
    /// Reading a counter into memory, adding one and saving it back is the bug this table was
    /// introduced to remove, so the only code that touches it is
    /// <see cref="TakeNextNumberAsync"/>, which never reads it into memory at all.
    /// </para>
    /// </summary>
    public DbSet<NumberSequence> NumberSequences => Set<NumberSequence>();

    /// <summary>
    /// The tenant every query is scoped to. Referenced by each module's global query filters.
    /// Falls back to <see cref="Guid.Empty"/> at design time, which matches no data — the safe
    /// direction for a filter to fail in.
    /// </summary>
    protected Guid CurrentTenantId => _dependencies.TenantContext?.TenantId ?? Guid.Empty;

    /// <summary>
    /// Takes the next number in a run, atomically.
    /// <para>
    /// One statement does all of it: creates the run if this is its first number, increments it if
    /// it is not, and returns the number taken. PostgreSQL locks the row for the duration of the
    /// statement, so two callers arriving together are serialised by the database rather than by
    /// hope, and the second one reads the value the first one wrote. There is no window between a
    /// read and a write for two callers to occupy at once, because there is no read.
    /// </para>
    /// <para>
    /// The lock is released when the surrounding transaction ends. Inside an explicit transaction
    /// that means a rollback puts the number back and no gap appears; with only the implicit
    /// transaction around a single statement it means the number is spent as soon as it is taken,
    /// and an operation that then fails leaves a gap.
    /// </para>
    /// <para>
    /// That difference is deliberate and is the line between this and a series of invoices. A
    /// missing SO-2026-00042 is untidy; a missing invoice number is a question from the tax
    /// authority. Invoicing therefore holds its own lock across the whole issue, in a transaction
    /// it opens itself. Commercial documents do not need that, and paying for it would mean an
    /// explicit transaction around creating every order.
    /// </para>
    /// </summary>
    /// <param name="sequenceKey">What is being numbered, for example <c>sales-order</c>.</param>
    /// <param name="year">The year whose run to draw from.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number taken, which no other caller will be given.</returns>
    public async Task<int> TakeNextNumberAsync(
        string sequenceKey,
        int year,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sequenceKey);

        // ON CONFLICT DO UPDATE rather than a SELECT ... FOR UPDATE followed by an UPDATE: it is
        // the same row lock in one round trip instead of two, and it removes the case where the
        // row does not exist yet, which is the case a lock cannot cover because there is nothing
        // to lock. The counter is left pointing one past what was returned.
        string sql =
            $"INSERT INTO \"{DefaultSchema}\".\"number_sequences\" "
            + "(\"tenant_id\", \"sequence_key\", \"year\", \"next_number\") "
            + "VALUES (@tenant, @key, @year, 2) "
            + "ON CONFLICT (\"tenant_id\", \"sequence_key\", \"year\") "
            + "DO UPDATE SET \"next_number\" = \"number_sequences\".\"next_number\" + 1 "
            + "RETURNING \"next_number\" - 1";

        object? taken = await ExecuteRawAsync<object?>(
            sql,
            command =>
            {
                AddParameter(command, "tenant", CurrentTenantId);
                AddParameter(command, "key", sequenceKey);
                AddParameter(command, "year", year);
            },
            (command, token) => command.ExecuteScalarAsync(token),
            cancellationToken).ConfigureAwait(false);

        return taken is null or DBNull
            ? throw new InvalidOperationException(
                $"The {sequenceKey} counter returned no number, which should not be reachable: "
                + "the statement either inserts a row or updates one.")
            : Convert.ToInt32(taken, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Takes ownership of a batch of undelivered outbox messages, so that no other sweep will
    /// pick them up while this one is working through them.
    /// <para>
    /// One statement, and the reason more than one instance of the host can now run. The inner
    /// select takes <c>FOR UPDATE SKIP LOCKED</c>, which is PostgreSQL's answer to exactly this:
    /// rows another transaction is already holding are stepped over rather than waited for, so
    /// two sweeps running at the same moment take two disjoint batches instead of both taking
    /// the same one. Without it a second instance delivered every message a second time — safe,
    /// because consumers deduplicate through the inbox, and wasteful, which is a different
    /// complaint but still a complaint.
    /// </para>
    /// <para>
    /// The claim is a lease rather than a flag, written into <c>next_attempt_at_utc</c> — the
    /// column that already means "do not look at this before". A sweep that finishes normally
    /// overwrites it; a host that is killed mid-batch leaves it, and the messages become visible
    /// again when the lease runs out. Nothing has to notice the crash for the work to resume,
    /// which is the property a flag with no expiry would not have.
    /// </para>
    /// </summary>
    /// <param name="batchSize">The most messages to claim.</param>
    /// <param name="maxAttempts">Messages that have failed this many times are left alone.</param>
    /// <param name="now">The current time.</param>
    /// <param name="lease">How long the claim holds if this sweep never comes back.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The claimed message identities, which may be fewer than <paramref name="batchSize"/>
    /// because another sweep held some of them.
    /// </returns>
    public async Task<Guid[]> ClaimOutboxMessagesAsync(
        int batchSize,
        int maxAttempts,
        DateTimeOffset now,
        TimeSpan lease,
        CancellationToken cancellationToken = default)
    {
        string table = $"\"{DefaultSchema}\".\"outbox_messages\"";

        string sql =
            $"UPDATE {table} SET \"next_attempt_at_utc\" = @leaseUntil "
            + $"WHERE \"id\" IN (SELECT \"id\" FROM {table} "
            + "WHERE \"processed_at_utc\" IS NULL "
            + "AND \"attempts\" < @maxAttempts "
            + "AND (\"next_attempt_at_utc\" IS NULL OR \"next_attempt_at_utc\" <= @now) "
            + "ORDER BY \"occurred_at_utc\" "
            + "LIMIT @batchSize "
            + "FOR UPDATE SKIP LOCKED) "
            + "RETURNING \"id\"";

        return await ExecuteRawAsync<Guid[]>(
            sql,
            command =>
            {
                AddParameter(command, "leaseUntil", now.Add(lease));
                AddParameter(command, "maxAttempts", maxAttempts);
                AddParameter(command, "now", now);
                AddParameter(command, "batchSize", batchSize);
            },
            async (command, token) =>
            {
                var claimed = new List<Guid>(batchSize);

                await using DbDataReader reader =
                    await command.ExecuteReaderAsync(token).ConfigureAwait(false);

                while (await reader.ReadAsync(token).ConfigureAwait(false))
                {
                    claimed.Add(reader.GetGuid(0));
                }

                return claimed.ToArray();
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes delivered outbox messages older than a cut-off, up to a limit.
    /// <para>
    /// Only rows with a <c>processed_at_utc</c>. Anything still owed delivery stays, however old
    /// it is, because an undelivered message that a person has not looked at for a month is the
    /// one thing in this table nobody should lose to housekeeping.
    /// </para>
    /// <para>
    /// Bounded by <paramref name="batchSize"/> and meant to be called in a loop. A single
    /// unbounded delete on a table that has been growing for a year is one long transaction, one
    /// enormous write-ahead log entry, and a lock held over the rows the sweep wants.
    /// </para>
    /// </summary>
    /// <param name="cutoffUtc">Messages processed before this are eligible.</param>
    /// <param name="batchSize">The most rows to delete in this call.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How many rows were deleted.</returns>
    public async Task<int> PurgeProcessedOutboxAsync(
        DateTimeOffset cutoffUtc,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        string table = $"\"{DefaultSchema}\".\"outbox_messages\"";

        string sql =
            $"DELETE FROM {table} WHERE \"id\" IN "
            + $"(SELECT \"id\" FROM {table} "
            + "WHERE \"processed_at_utc\" IS NOT NULL AND \"processed_at_utc\" < @cutoff "
            + "LIMIT @batchSize)";

        return await ExecuteRawAsync<int>(
            sql,
            command =>
            {
                AddParameter(command, "cutoff", cutoffUtc);
                AddParameter(command, "batchSize", batchSize);
            },
            (command, token) => command.ExecuteNonQueryAsync(token),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes inbox records older than a cut-off, up to a limit.
    /// <para>
    /// These are what stop a redelivered message being handled twice, so deleting one trades a
    /// guarantee for space. The trade is only safe well after the publisher has thrown its own
    /// copy away, which is why the two retentions are not the same number and why the longer one
    /// belongs here.
    /// </para>
    /// </summary>
    /// <param name="cutoffUtc">Records handled before this are eligible.</param>
    /// <param name="batchSize">The most rows to delete in this call.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How many rows were deleted.</returns>
    public async Task<int> PurgeHandledInboxAsync(
        DateTimeOffset cutoffUtc,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        string table = $"\"{DefaultSchema}\".\"inbox_messages\"";

        string sql =
            $"DELETE FROM {table} WHERE (\"message_id\", \"handler_name\") IN "
            + "(SELECT \"message_id\", \"handler_name\" "
            + $"FROM {table} WHERE \"handled_at_utc\" < @cutoff "
            + "LIMIT @batchSize)";

        return await ExecuteRawAsync<int>(
            sql,
            command =>
            {
                AddParameter(command, "cutoff", cutoffUtc);
                AddParameter(command, "batchSize", batchSize);
            },
            (command, token) => command.ExecuteNonQueryAsync(token),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The module's schema, read from the model rather than passed in, so that a module which
    /// changes its schema name changes it in one place and every statement here follows.
    /// </summary>
    /// <exception cref="InvalidOperationException">The context declared no default schema.</exception>
    private string DefaultSchema =>
        Model.GetDefaultSchema()
        ?? throw new InvalidOperationException(
            $"{GetType().Name} has no default schema, so its tables cannot be located. "
            + "Every module context sets one with HasDefaultSchema.");

    /// <summary>
    /// Runs one hand-written statement on this context's connection and transaction.
    /// <para>
    /// The statements above are hand-written because each of them is a thing EF Core cannot
    /// express and the database can: an upsert that returns, a claim that skips locked rows, a
    /// bounded delete. What they share is the plumbing around them, and that is all this is.
    /// </para>
    /// </summary>
    private async Task<TResult> ExecuteRawAsync<TResult>(
        string sql,
        Action<DbCommand> bindParameters,
        Func<DbCommand, CancellationToken, Task<TResult>> execute,
        CancellationToken cancellationToken)
    {
        DbConnection connection = Database.GetDbConnection();
        bool openedHere = false;

        if (connection.State != ConnectionState.Open)
        {
            await Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            openedHere = true;
        }

        try
        {
            await using DbCommand command = connection.CreateCommand();
            command.CommandText = sql;

            // Enlisted by hand, because a command created from the connection knows nothing about
            // the context's transaction. Without this a numbering increment would commit on its
            // own, and rolling the operation back would leave the number spent - which is the
            // behaviour TakeNextNumberAsync documents as belonging to the no-transaction case,
            // arrived at by accident in the case that asked for better.
            if (Database.CurrentTransaction is { } transaction)
            {
                command.Transaction = transaction.GetDbTransaction();
            }

            bindParameters(command);

            return await execute(command, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (openedHere)
            {
                await Database.CloseConnectionAsync().ConfigureAwait(false);
            }
        }
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

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

        // Applied after the module's own configurations, so these tables pick up the default
        // schema the module has already set. Every module gets its own set.
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new InboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new NumberSequenceConfiguration());
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
