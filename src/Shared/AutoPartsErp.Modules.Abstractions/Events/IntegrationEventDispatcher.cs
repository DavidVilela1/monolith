using System.Reflection;
using AutoPartsErp.SharedKernel.Abstractions;
using AutoPartsErp.SharedKernel.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AutoPartsErp.Modules.Abstractions.Events;

/// <summary>
/// Hands one stored integration event to every handler subscribed to it.
/// <para>
/// Each handler gets its own DI scope, and therefore its own database contexts: one module's
/// failure cannot poison another's unit of work, and the inbox row each handler writes lands in
/// its own module's schema alongside its own changes.
/// </para>
/// <para>
/// Unlike the bus it replaces, this does not swallow failures. A handler that throws makes the
/// whole delivery fail, the outbox row keeps its place, and the sweep tries again later. Handlers
/// that already succeeded are re-run on that retry and short-circuit on their inbox row, which is
/// what makes at-least-once delivery survivable.
/// </para>
/// </summary>
public sealed class IntegrationEventDispatcher : IIntegrationEventDispatcher
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<IntegrationEventDispatcher> _logger;

    /// <summary>Initializes the dispatcher.</summary>
    public IntegrationEventDispatcher(
        IServiceScopeFactory scopeFactory,
        ILogger<IntegrationEventDispatcher> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    /// <exception cref="AggregateException">One or more handlers failed.</exception>
    public async Task DispatchAsync(
        IntegrationEventEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        Type eventType = envelope.Event.GetType();
        Type handlerContract = typeof(IIntegrationEventHandler<>).MakeGenericType(eventType);

        Type[] handlerTypes = DiscoverHandlers(handlerContract);

        if (handlerTypes.Length == 0)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "No handler subscribed to {EventName} ({MessageId})",
                    eventType.Name,
                    envelope.MessageId);
            }

            return;
        }

        List<Exception>? failures = null;

        foreach (Type handlerType in handlerTypes)
        {
            try
            {
                await InvokeAsync(handlerContract, handlerType, envelope, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "{HandlerName} failed handling {EventName} ({MessageId})",
                    handlerType.Name,
                    eventType.Name,
                    envelope.MessageId);

                // Keep going: the other subscribers are independent, and the retry will replay
                // only the ones that have not recorded an inbox row.
                (failures ??= []).Add(exception);
            }
        }

        if (failures is not null)
        {
            throw new AggregateException(
                $"{failures.Count} handler(s) failed for {eventType.Name} ({envelope.MessageId}).",
                failures);
        }
    }

    /// <summary>
    /// Resolves the subscribers once to learn their concrete types, then throws that scope away.
    /// <para>
    /// Constructing a handler is cheap — it takes repositories and a context, none of which opens
    /// a connection until something queries — and knowing the types up front is what lets each
    /// one run in a scope of its own below.
    /// </para>
    /// </summary>
    private Type[] DiscoverHandlers(Type handlerContract)
    {
        using IServiceScope probe = _scopeFactory.CreateScope();

        return [.. probe.ServiceProvider
            .GetServices(handlerContract)
            .Where(handler => handler is not null)
            .Select(handler => handler!.GetType())
            .Distinct()];
    }

    private async Task InvokeAsync(
        Type handlerContract,
        Type handlerType,
        IntegrationEventEnvelope envelope,
        CancellationToken cancellationToken)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();

        // There is no request behind this call, so the tenant has to be put in place explicitly
        // or every query filter in the handler would scope to nothing.
        scope.ServiceProvider.GetRequiredService<AmbientTenant>().Set(envelope.TenantId);

        IntegrationEventScope eventScope =
            scope.ServiceProvider.GetRequiredService<IntegrationEventScope>();

        eventScope.MessageId = envelope.MessageId;
        eventScope.HandlerName = handlerType.FullName ?? handlerType.Name;

        object handler = scope.ServiceProvider
            .GetServices(handlerContract)
            .FirstOrDefault(candidate => candidate?.GetType() == handlerType)
            ?? throw new InvalidOperationException(
                $"'{handlerType.FullName}' could not be resolved a second time. A handler " +
                "registration that is not deterministic cannot be delivered to reliably.");

        MethodInfo method = handlerContract.GetMethod("HandleAsync")
            ?? throw new InvalidOperationException($"'{handlerContract.Name}' has no HandleAsync method.");

        await ((Task)method.Invoke(handler, [envelope.Event, cancellationToken])!).ConfigureAwait(false);
    }
}
