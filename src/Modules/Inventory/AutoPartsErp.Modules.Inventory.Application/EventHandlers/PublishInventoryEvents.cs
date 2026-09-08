using AutoPartsErp.IntegrationEvents.Inventory;
using AutoPartsErp.Modules.Inventory.Domain.Stock.Events;
using AutoPartsErp.SharedKernel.Abstractions;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Primitives;

namespace AutoPartsErp.Modules.Inventory.Application.EventHandlers;

/// <summary>
/// Tells Purchasing that a part has reached the level at which it should be reordered.
/// <para>
/// This is the first half of the replenishment loop, and until now it was the missing half.
/// Inventory raised the domain event, the integration event existed, Purchasing had a handler
/// waiting for it — and nothing in between ever translated one into the other, so the handler
/// never ran and no replenishment suggestion was ever raised. The buyer's list was empty because
/// nothing could put anything on it, which looks exactly like a company that never runs out.
/// </para>
/// <para>
/// Worth naming the shape of that failure, because it is the shape this whole architecture is
/// prone to: nothing was broken. Every part compiled, every test passed, both ends were correct.
/// The wire between them was the thing that did not exist, and a missing wire raises no error —
/// it just means an event nobody sends and a handler nobody calls.
/// </para>
/// </summary>
public sealed class PublishStockFellBelowReorderPoint
    : IDomainEventHandler<StockFellBelowReorderPointDomainEvent>
{
    private readonly IEventBus _eventBus;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initializes the handler.</summary>
    public PublishStockFellBelowReorderPoint(IEventBus eventBus, ITenantContext tenantContext)
    {
        _eventBus = eventBus;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public Task HandleAsync(
        StockFellBelowReorderPointDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return _eventBus.PublishAsync(
            new StockFellBelowReorderPointIntegrationEvent(
                domainEvent.Part.Value,
                domainEvent.WarehouseId.Value,
                domainEvent.Available,
                domainEvent.OnOrder,
                domainEvent.ReorderPoint,
                domainEvent.ReorderQuantity,
                _tenantContext.TenantId),
            cancellationToken);
    }
}

/// <summary>
/// Republishes a receipt so anything downstream of the shelf knows stock arrived.
/// <para>
/// Deliberately not the same fact as <c>GoodsReceivedIntegrationEvent</c>, which Purchasing
/// publishes and which means "the supplier delivered against this order line". This one means
/// "the balance went up", and it is true of a customer return and a transfer in as well.
/// </para>
/// </summary>
public sealed class PublishStockReceived : IDomainEventHandler<StockReceivedDomainEvent>
{
    private readonly IEventBus _eventBus;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initializes the handler.</summary>
    public PublishStockReceived(IEventBus eventBus, ITenantContext tenantContext)
    {
        _eventBus = eventBus;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public Task HandleAsync(
        StockReceivedDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return _eventBus.PublishAsync(
            new StockReceivedIntegrationEvent(
                domainEvent.Part.Value,
                domainEvent.WarehouseId.Value,
                domainEvent.Quantity,
                domainEvent.Reference,
                _tenantContext.TenantId),
            cancellationToken);
    }
}

/// <summary>Republishes an issue so anything downstream of the shelf knows stock left it.</summary>
public sealed class PublishStockIssued : IDomainEventHandler<StockIssuedDomainEvent>
{
    private readonly IEventBus _eventBus;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initializes the handler.</summary>
    public PublishStockIssued(IEventBus eventBus, ITenantContext tenantContext)
    {
        _eventBus = eventBus;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public Task HandleAsync(
        StockIssuedDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return _eventBus.PublishAsync(
            new StockIssuedIntegrationEvent(
                domainEvent.Part.Value,
                domainEvent.WarehouseId.Value,
                domainEvent.Quantity,
                domainEvent.Reference,
                _tenantContext.TenantId),
            cancellationToken);
    }
}
