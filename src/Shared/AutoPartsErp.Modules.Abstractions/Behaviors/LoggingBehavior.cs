using System.Diagnostics;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;
using Microsoft.Extensions.Logging;

namespace AutoPartsErp.Modules.Abstractions.Behaviors;

/// <summary>
/// Logs the name, outcome and duration of every request that passes through the dispatcher.
/// Business failures are logged at warning level with their stable error code, which makes
/// "how often does a picker try to ship stock that is not there?" a log query rather than a guess.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public sealed class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;

    /// <summary>Initializes the behaviour.</summary>
    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<TResponse> HandleAsync(
        TRequest request,
        Func<Task<TResponse>> next,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(next);

        string requestName = typeof(TRequest).Name;
        long start = Stopwatch.GetTimestamp();

        try
        {
            TResponse response = await next().ConfigureAwait(false);
            TimeSpan elapsed = Stopwatch.GetElapsedTime(start);

            // The IsEnabled guards are not ceremony: building these argument arrays costs
            // allocations and boxing on every single request, and this behaviour wraps every
            // request in the system. When the level is switched off in production, the work
            // should not happen at all.
            if (response is Result { IsFailure: true } failure)
            {
                if (_logger.IsEnabled(LogLevel.Warning))
                {
                    _logger.LogWarning(
                        "{RequestName} failed after {ElapsedMs} ms: {ErrorCode} ({ErrorType}) - {ErrorDescription}",
                        requestName,
                        elapsed.TotalMilliseconds,
                        failure.Error.Code,
                        failure.Error.Type,
                        failure.Error.Description);
                }
            }
            else if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    "{RequestName} completed in {ElapsedMs} ms",
                    requestName,
                    elapsed.TotalMilliseconds);
            }

            return response;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "{RequestName} threw after {ElapsedMs} ms",
                requestName,
                Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            throw;
        }
    }
}
