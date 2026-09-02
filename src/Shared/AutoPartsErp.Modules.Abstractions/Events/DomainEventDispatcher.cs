using AutoPartsErp.SharedKernel.Abstractions;
using AutoPartsErp.SharedKernel.Primitives;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AutoPartsErp.Modules.Abstractions.Events;

/// <summary>
/// Dispatches domain events raised by aggregates to their handlers, inside the unit of work
/// that raised them and before it commits.
/// <para>
/// A failure here fails the whole save, on purpose. These handlers exist to translate a domain
/// event into an integration event and queue it for the outbox; if that cannot happen, the
/// business change must not commit either — otherwise the module has recorded something and
/// silently decided not to tell anyone about it, which is the exact failure the outbox was
/// built to remove.
/// </para>
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

                    // Rethrown, unlike before. This runs inside the caller's transaction now,
                    // so the honest response to a handler that cannot do its job is to fail the
                    // save rather than commit and hope somebody reads the log.
                    throw;
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
