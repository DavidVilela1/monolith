using AutoPartsErp.SharedKernel.Abstractions;
using AutoPartsErp.SharedKernel.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AutoPartsErp.Persistence.Outbox;

/// <summary>How the outbox sweep behaves. Bound from <c>Erp:Outbox</c>.</summary>
public sealed class OutboxOptions
{
    /// <summary>The configuration section these are read from.</summary>
    public const string SectionName = "Erp:Outbox";

    /// <summary>How long to wait before the first sweep, so migrations finish first.</summary>
    public TimeSpan StartupDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>How often to look for undelivered messages when the last sweep found nothing.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Messages read per sweep.</summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>
    /// How many failures before a message is left alone.
    /// <para>
    /// It is not deleted and not marked processed — it sits in the table with its error, which
    /// is the honest state for something the system could not deliver and a person now has to
    /// look at. Silently dropping it would be worse than the bug that caused it.
    /// </para>
    /// </summary>
    public int MaxAttempts { get; set; } = 10;

    /// <summary>The longest gap between retries, however many have failed.</summary>
    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// How long a claimed batch stays invisible to other sweeps.
    /// <para>
    /// Long enough that a slow batch is never stolen from underneath the instance working on it,
    /// short enough that a host killed mid-batch does not strand its messages for an afternoon.
    /// Being wrong in the short direction costs a duplicate delivery, which the inbox already
    /// absorbs; being wrong in the long direction costs a delay.
    /// </para>
    /// </summary>
    public TimeSpan ClaimLease { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How long a delivered message is kept before housekeeping deletes it.
    /// <para>
    /// Not zero, because "was this event published, and when?" is a question somebody asks the
    /// week after a wrong stock figure, not the same afternoon. Zero or less keeps everything
    /// for ever, which is the setting for an installation that would rather buy disk.
    /// </para>
    /// </summary>
    public TimeSpan ProcessedRetention { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// How long a record of having handled a message is kept.
    /// <para>
    /// Deliberately longer than <see cref="ProcessedRetention"/>. This row is what stops a
    /// redelivery being applied twice, and the only redelivery it can still face is a person
    /// reaching into the publisher's table and resetting a message that never went out. Keeping
    /// it well past the point where the publisher's copy is gone is what makes that safe.
    /// </para>
    /// </summary>
    public TimeSpan HandledRetention { get; set; } = TimeSpan.FromDays(90);

    /// <summary>How often housekeeping runs.</summary>
    public TimeSpan RetentionInterval { get; set; } = TimeSpan.FromHours(6);

    /// <summary>Rows deleted per statement, so one pass is many small locks rather than one long one.</summary>
    public int RetentionBatchSize { get; set; } = 1000;
}

/// <summary>
/// Delivers one module's committed integration events, and keeps trying until they land.
/// <para>
/// One of these runs per module, over that module's own outbox table. Publishing is now just a
/// row; this is what turns the row into a handler call, and what makes a failed handler a retry
/// rather than a fact quietly dropped on the floor.
/// </para>
/// <para>
/// Delivery is at-least-once, which is the strongest thing any queue can honestly offer: a
/// handler that succeeds and then loses the connection before its row is marked will be called
/// again. Consumers are expected to cope, and the inbox table is how they do.
/// </para>
/// <para>
/// <b>Safe to run on more than one host.</b> Each sweep claims its batch before delivering any of
/// it, taking a lease no other sweep will step on — see
/// <see cref="ModuleDbContext.ClaimOutboxMessagesAsync"/> for how. Two instances therefore share
/// the backlog rather than each working through all of it.
/// </para>
/// </summary>
/// <typeparam name="TContext">The module context whose outbox this drains.</typeparam>
public sealed class OutboxProcessor<TContext> : BackgroundService
    where TContext : ModuleDbContext
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDateTimeProvider _clock;
    private readonly OutboxOptions _options;
    private readonly ILogger<OutboxProcessor<TContext>> _logger;

    /// <summary>Initializes the processor.</summary>
    public OutboxProcessor(
        IServiceScopeFactory scopeFactory,
        IDateTimeProvider clock,
        IOptions<OutboxOptions> options,
        ILogger<OutboxProcessor<TContext>> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _scopeFactory = scopeFactory;
        _clock = clock;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // The host starts background services before the development-only migration step has
        // finished, so the first sweep would otherwise query a table that does not exist yet.
        await Task.Delay(_options.StartupDelay, stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            int handled = 0;

            try
            {
                handled = await SweepAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // A sweep failing is infrastructure trouble - the database being down, usually.
                // Log it and try again; the messages are safe in the table either way.
                _logger.LogError(
                    exception,
                    "Outbox sweep failed for {Module}",
                    typeof(TContext).Name);
            }

            // A full batch probably means there is more waiting, so come straight back. A short
            // one is now ambiguous — it can also mean another host held some of the rows this
            // sweep asked for — and the cost of reading it the cautious way is one poll interval
            // in a case where somebody else is already doing the work.
            TimeSpan delay = handled >= _options.BatchSize ? TimeSpan.Zero : _options.PollInterval;

            if (delay > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();

        TContext context = scope.ServiceProvider.GetRequiredService<TContext>();
        IIntegrationEventSerializer serializer =
            scope.ServiceProvider.GetRequiredService<IIntegrationEventSerializer>();

        // Resolved once for the whole batch. The dispatcher holds nothing per-message — it makes
        // a fresh scope per handler itself — so giving each message its own was a scope built
        // and thrown away for nothing.
        IIntegrationEventDispatcher dispatcher =
            scope.ServiceProvider.GetRequiredService<IIntegrationEventDispatcher>();

        DateTimeOffset now = _clock.UtcNow;

        // Claim first, deliver second. The claim is one statement that hands this sweep a batch
        // nobody else will touch; without it two hosts read the same rows and deliver everything
        // twice.
        Guid[] claimed = await context
            .ClaimOutboxMessagesAsync(
                _options.BatchSize, _options.MaxAttempts, now, _options.ClaimLease, cancellationToken)
            .ConfigureAwait(false);

        if (claimed.Length == 0)
        {
            return 0;
        }

        List<OutboxMessage> pending = await context.OutboxMessages
            .Where(message => claimed.Contains(message.Id))
            .OrderBy(message => message.OccurredAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (OutboxMessage message in pending)
        {
            await DeliverAsync(message, serializer, dispatcher, now, cancellationToken)
                .ConfigureAwait(false);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return pending.Count;
    }

    private async Task DeliverAsync(
        OutboxMessage message,
        IIntegrationEventSerializer serializer,
        IIntegrationEventDispatcher dispatcher,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        IIntegrationEvent? integrationEvent;

        try
        {
            integrationEvent = serializer.Deserialize(message.Type, message.Content);
        }
        catch (Exception exception)
        {
            RecordFailure(message, now, $"Could not deserialize: {exception}");
            return;
        }

        if (integrationEvent is null)
        {
            // The contract was renamed or removed while rows still referred to it. Retrying will
            // not help, so burn the attempts immediately and leave it for a person.
            message.Attempts = _options.MaxAttempts;
            message.Error = $"No integration event contract named '{message.Type}' is loaded.";
            message.NextAttemptAtUtc = null;

            _logger.LogError(
                "Outbox message {MessageId} refers to unknown contract {ContractName}",
                message.Id,
                message.Type);

            return;
        }

        var envelope = new IntegrationEventEnvelope(message.Id, message.TenantId, integrationEvent);

        try
        {
            // The dispatcher gives each handler its own scope, and therefore its own database
            // contexts rather than the one this sweep is holding — so a consumer's failure
            // cannot poison the outbox update that records it.
            await dispatcher.DispatchAsync(envelope, cancellationToken).ConfigureAwait(false);

            message.ProcessedAtUtc = now;
            message.Error = null;
            message.NextAttemptAtUtc = null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            RecordFailure(message, now, exception.ToString());
        }
    }

    private void RecordFailure(OutboxMessage message, DateTimeOffset now, string error)
    {
        message.Attempts++;
        message.Error = error.Length > OutboxMessageConfiguration.MaxErrorLength
            ? error[..OutboxMessageConfiguration.MaxErrorLength]
            : error;

        if (message.Attempts >= _options.MaxAttempts)
        {
            message.NextAttemptAtUtc = null;

            _logger.LogError(
                "Outbox message {MessageId} ({ContractName}) gave up after {Attempts} attempts",
                message.Id,
                message.Type,
                message.Attempts);

            return;
        }

        message.NextAttemptAtUtc = now.Add(Backoff(message.Attempts));

        if (_logger.IsEnabled(LogLevel.Warning))
        {
            _logger.LogWarning(
                "Outbox message {MessageId} ({ContractName}) failed, attempt {Attempts}, retrying at {NextAttempt}",
                message.Id,
                message.Type,
                message.Attempts,
                message.NextAttemptAtUtc);
        }
    }

    private TimeSpan Backoff(int attempts)
    {
        // 2s, 4s, 8s, 16s ... capped. Enough to ride out a restart without hammering a database
        // that is already having a bad time.
        double seconds = Math.Pow(2, Math.Min(attempts, 20));
        TimeSpan backoff = TimeSpan.FromSeconds(seconds);

        return backoff > _options.MaxBackoff ? _options.MaxBackoff : backoff;
    }
}
