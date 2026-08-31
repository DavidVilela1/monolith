using AutoPartsErp.Modules.Inventory.Application.Stock.Commands;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AutoPartsErp.Modules.Inventory.Infrastructure.Persistence.Jobs;

/// <summary>
/// Lapses reservations that have passed their expiry, on a timer.
/// <para>
/// Without something calling it, expiry is only a field. Quotes nobody converts would hold stock
/// indefinitely and the shelf would slowly fill with quantity reserved against orders that will
/// never exist — which looks, to everyone using the system, exactly like being out of stock.
/// </para>
/// <para>
/// It sweeps a bounded number of records per pass so a backlog cannot stall the job, and it
/// swallows its own failures: a stock sweep that crashes the API would be a worse bug than the
/// one it exists to prevent.
/// </para>
/// </summary>
public sealed class ReservationSweeper : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReservationSweeper> _logger;
    private readonly TimeSpan _interval;
    private readonly int _batchSize;

    /// <summary>Initializes the sweeper.</summary>
    /// <param name="scopeFactory">Creates a scope per pass, since the dispatcher is scoped.</param>
    /// <param name="logger">Log sink.</param>
    /// <param name="interval">How often to sweep. Defaults to one minute.</param>
    /// <param name="batchSize">How many balances to examine per pass.</param>
    public ReservationSweeper(
        IServiceScopeFactory scopeFactory,
        ILogger<ReservationSweeper> logger,
        TimeSpan? interval = null,
        int batchSize = 500)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _interval = interval ?? TimeSpan.FromMinutes(1);
        _batchSize = batchSize;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await SweepAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Reservation sweep failed; will retry on the next tick.");
            }
        }
    }

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        Result<int> expired = await dispatcher
            .SendAsync(new ExpireLapsedReservationsCommand(_batchSize), cancellationToken)
            .ConfigureAwait(false);

        if (expired.IsFailure)
        {
            string errorCode = expired.Error.Code;
            _logger.LogError("Reservation sweep reported {ErrorCode}.", errorCode);
            return;
        }

        // Read the result once into a local. Result<T>.Value is a property that throws on a
        // failed result, so passing it straight into a log call means doing that work even when
        // the level is switched off.
        int count = expired.Value;

        if (count > 0 && _logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("Expired {Count} lapsed stock reservations.", count);
        }
    }
}
