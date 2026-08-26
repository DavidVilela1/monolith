using AutoPartsErp.SharedKernel.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AutoPartsErp.Modules.Abstractions.Events;

/// <summary>
/// Delivers integration events to handlers inside the same process.
/// <para>
/// Because publishers only ever see <see cref="IEventBus"/>, replacing this with a real broker
/// (RabbitMQ, Azure Service Bus, Kafka) later means changing one DI registration. Handlers are
/// already written to be idempotent, which is the requirement that is expensive to retrofit.
/// </para>
/// <para>
/// Each handler runs in its own DI scope so that one module's failure cannot poison another
/// module's database context.
/// </para>
/// </summary>
public sealed class InProcessEventBus : IEventBus
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<InProcessEventBus> _logger;

    /// <summary>Initializes the bus.</summary>
    public InProcessEventBus(IServiceScopeFactory scopeFactory, ILogger<InProcessEventBus> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
        where TEvent : IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        using IServiceScope scope = _scopeFactory.CreateScope();

        IIntegrationEventHandler<TEvent>[] handlers = scope.ServiceProvider
            .GetServices<IIntegrationEventHandler<TEvent>>()
            .ToArray();

        if (handlers.Length == 0)
        {
            // typeof(TEvent).Name is a reflection call; skip it entirely when Debug is off.
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "No handler subscribed to {EventName} ({EventId})",
                    typeof(TEvent).Name,
                    integrationEvent.EventId);
            }

            return;
        }

        foreach (IIntegrationEventHandler<TEvent> handler in handlers)
        {
            try
            {
                await handler.HandleAsync(integrationEvent, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                // One subscriber failing must not stop the others: the publisher's transaction
                // has already committed and cannot be rolled back from here.
                _logger.LogError(
                    exception,
                    "{HandlerName} failed handling {EventName} ({EventId})",
                    handler.GetType().Name,
                    typeof(TEvent).Name,
                    integrationEvent.EventId);
            }
        }
    }
}
