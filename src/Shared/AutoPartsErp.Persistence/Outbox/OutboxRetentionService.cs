using AutoPartsErp.SharedKernel.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AutoPartsErp.Persistence.Outbox;

/// <summary>
/// Deletes outbox and inbox rows that have outlived their usefulness.
/// <para>
/// Both tables are append-only in normal operation: one row per integration event published, one
/// per event handled per handler. Nothing ever went back for them, so a system that had been
/// running for two years had two years of them, and the partial index that keeps the sweep fast
/// did nothing for the table scan behind a support question or the time a backup takes.
/// </para>
/// <para>
/// <b>What is kept, and why the two numbers differ.</b> An outbox row is deleted only once it has
/// been delivered — anything still owed delivery stays for ever, because an undelivered message
/// is the one row in here nobody should lose to housekeeping. An inbox row is what stops a
/// redelivery being applied twice, so it is kept substantially longer than the publisher keeps
/// its copy: by the time one is deleted, the message it guards against has itself been gone for
/// months.
/// </para>
/// <para>
/// One of these runs per module, over that module's own two tables, on the same reasoning as the
/// sweep: the tables live in the module's schema and nothing outside it should be reaching in.
/// </para>
/// </summary>
/// <typeparam name="TContext">The module context whose tables this prunes.</typeparam>
public sealed class OutboxRetentionService<TContext> : BackgroundService
    where TContext : ModuleDbContext
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDateTimeProvider _clock;
    private readonly OutboxOptions _options;
    private readonly ILogger<OutboxRetentionService<TContext>> _logger;

    /// <summary>Initializes the service.</summary>
    public OutboxRetentionService(
        IServiceScopeFactory scopeFactory,
        IDateTimeProvider clock,
        IOptions<OutboxOptions> options,
        ILogger<OutboxRetentionService<TContext>> logger)
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
        if (_options.ProcessedRetention <= TimeSpan.Zero)
        {
            // Keeping everything is a real choice, and an installation that made it should not
            // also pay for a background service that wakes up every six hours to delete nothing.
            return;
        }

        // Housekeeping is never the reason a host is slow to start serving requests, so the first
        // pass waits a full interval rather than running at boot.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.RetentionInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                await PruneAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // Housekeeping failing is not urgent: nothing is lost, the tables are merely
                // larger than intended, and the next pass will try again.
                _logger.LogError(
                    exception,
                    "Outbox retention pass failed for {Module}",
                    typeof(TContext).Name);
            }
        }
    }

    private async Task PruneAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();

        TContext context = scope.ServiceProvider.GetRequiredService<TContext>();

        DateTimeOffset now = _clock.UtcNow;

        int outbox = await PruneInBatchesAsync(
            (cutoff, token) => context.PurgeProcessedOutboxAsync(
                cutoff, _options.RetentionBatchSize, token),
            now - _options.ProcessedRetention,
            cancellationToken).ConfigureAwait(false);

        // Clamped rather than trusted. A configuration that keeps inbox rows for less time than
        // the publisher keeps its outbox rows would delete the record of a message the publisher
        // can still hand back, and the symptom - one goods receipt applied twice, months later -
        // is not one anybody would trace to a settings file.
        TimeSpan handledRetention = _options.HandledRetention > _options.ProcessedRetention
            ? _options.HandledRetention
            : _options.ProcessedRetention;

        int inbox = await PruneInBatchesAsync(
            (cutoff, token) => context.PurgeHandledInboxAsync(
                cutoff, _options.RetentionBatchSize, token),
            now - handledRetention,
            cancellationToken).ConfigureAwait(false);

        if ((outbox > 0 || inbox > 0) && _logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "Retention removed {OutboxRows} delivered and {InboxRows} handled rows for {Module}",
                outbox,
                inbox,
                typeof(TContext).Name);
        }
    }

    private async Task<int> PruneInBatchesAsync(
        Func<DateTimeOffset, CancellationToken, Task<int>> delete,
        DateTimeOffset cutoffUtc,
        CancellationToken cancellationToken)
    {
        int total = 0;

        // A pass that has deleted a whole batch probably has more to delete, so it comes straight
        // back. A short batch means the backlog is gone and the pass is finished; the loop stops
        // there rather than running until the table is empty of everything it will ever hold.
        while (!cancellationToken.IsCancellationRequested)
        {
            int deleted = await delete(cutoffUtc, cancellationToken).ConfigureAwait(false);

            total += deleted;

            if (deleted < _options.RetentionBatchSize)
            {
                break;
            }
        }

        return total;
    }
}
