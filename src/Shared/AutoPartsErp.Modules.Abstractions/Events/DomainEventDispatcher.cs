using AutoPartsErp.SharedKernel.Abstractions;
using AutoPartsErp.SharedKernel.Primitives;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AutoPartsErp.Modules.Abstractions.Events;

/// <summary>
/// Dispatches domain events raised by aggregates to their handlers, after the unit of work
/// has committed. Handlers therefore always observe persisted state.
/// </summary>
public sealed class DomainEventDispatcher : IDomainEventDispatcher
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DomainEventDispatcher> _logger;

    /// <summary>Initializes the dispatcher.</summary>
    public DomainEventDispatcher(IServiceProvider serviceProvider, ILogger<DomainEventDispatcher> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task DispatchAsync(
        IReadOnlyCollection<IDomainEvent> domainEvents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvents);

        foreach (IDomainEvent domainEvent in domainEvents)
        {
            Type handlerType = typeof(IDomainEventHandler<>).MakeGenericType(domainEvent.GetType());

            foreach (object? handler in _serviceProvider.GetServices(handlerType))
            {
                if (handler is null)
                {
                    continue;
                }

                try
                {
                    await InvokeAsync(handler, handlerType, domainEvent, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    _logger.LogError(
                        exception,
                        "{HandlerName} failed handling {EventName} ({EventId})",
                        handler.GetType().Name,
                        domainEvent.GetType().Name,
                        domainEvent.EventId);
                }
            }
        }
    }

    private static Task InvokeAsync(
        object handler,
        Type handlerType,
        IDomainEvent domainEvent,
        CancellationToken cancellationToken)
    {
        System.Reflection.MethodInfo method =
            handlerType.GetMethod(nameof(IDomainEventHandler<IDomainEvent>.HandleAsync))
            ?? throw new InvalidOperationException($"'{handlerType.Name}' has no HandleAsync method.");

        return (Task)method.Invoke(handler, [domainEvent, cancellationToken])!;
    }
}
