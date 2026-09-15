using AutoPartsErp.Modules.Purchasing.Application.Agreements.Commands;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AutoPartsErp.Modules.Purchasing.Infrastructure.Persistence.Jobs;

/// <summary>
/// Closes rebate periods that have ended, on a timer.
/// <para>
/// Without something calling it, a period ends and stays open. That matters more than it sounds:
/// closing is what turns a period's shortfall into a claim somebody chases, so a year nobody
/// closes is a rebate the company simply does not collect — and it looks, from every screen, like
/// a supplier who owed nothing.
/// </para>
/// <para>
/// A period is closed once its last day has passed, and a rebate year usually ends in December —
/// so this has nothing to do on almost every tick. It runs daily rather than by the minute for
/// that reason, and its cost on an ordinary day is one indexed query returning nothing.
/// </para>
/// <para>
/// It sweeps a bounded number per pass so a backlog cannot stall the job, and it swallows its own
/// failures: a rebate sweep that crashed the API would be a worse bug than the one it prevents.
/// </para>
/// </summary>
public sealed class RappelPeriodSweeper : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RappelPeriodSweeper> _logger;
    private readonly TimeSpan _interval;
    private readonly int _batchSize;

    /// <summary>Initializes the sweeper.</summary>
    /// <param name="scopeFactory">Creates a scope per pass, since the dispatcher is scoped.</param>
    /// <param name="logger">Log sink.</param>
    /// <param name="interval">How often to sweep. Defaults to a day.</param>
    /// <param name="batchSize">How many periods to close per pass.</param>
    public RappelPeriodSweeper(
        IServiceScopeFactory scopeFactory,
        ILogger<RappelPeriodSweeper> logger,
        TimeSpan? interval = null,
        int batchSize = 200)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _interval = interval ?? TimeSpan.FromHours(24);
        _batchSize = batchSize;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Once at startup, before the first tick. An installation restarted every evening would
        // otherwise never reach a twenty-four hour tick and never close anything at all.
        await SweepSafelyAsync(stoppingToken).ConfigureAwait(false);

        using var timer = new PeriodicTimer(_interval);

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await SweepSafelyAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task SweepSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await SweepAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception, "Rebate period sweep failed; will retry on the next tick.");
        }
    }

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        Result<int> swept = await dispatcher
            .SendAsync(new CloseDueRappelPeriodsCommand(_batchSize), cancellationToken)
            .ConfigureAwait(false);

        if (swept.IsFailure)
        {
            string errorCode = swept.Error.Code;
            _logger.LogError("Rebate period sweep reported {ErrorCode}.", errorCode);
            return;
        }

        // Read the result once into a local. Result<T>.Value throws on a failed result, so
        // passing it straight into a log call does that work even when the level is switched off.
        int count = swept.Value;

        if (count > 0 && _logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("Closed {Count} rebate periods that had ended.", count);
        }
    }
}
