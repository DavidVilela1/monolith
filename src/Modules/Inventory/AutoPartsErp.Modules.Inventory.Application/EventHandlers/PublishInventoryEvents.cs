using AutoPartsErp.IntegrationEvents.Inventory;
using AutoPartsErp.Modules.Inventory.Domain.Stock.Events;
using AutoPartsErp.Modules.Inventory.Domain.Transfers.Events;
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
                domainEvent.ReferenceType,
                domainEvent.MovementId.Value,
                domainEvent.Value,
                domainEvent.CurrencyCode,
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
                domainEvent.ReferenceType,
                domainEvent.MovementId.Value,
                domainEvent.CostValue,
                domainEvent.CurrencyCode,
                _tenantContext.TenantId),
            cancellationToken);
    }
}

/// <summary>Republishes a count's correction so Finance can post what it was worth.</summary>
public sealed class PublishStockAdjusted : IDomainEventHandler<StockAdjustedDomainEvent>
{
    private readonly IEventBus _eventBus;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initializes the handler.</summary>
    public PublishStockAdjusted(IEventBus eventBus, ITenantContext tenantContext)
    {
        _eventBus = eventBus;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public Task HandleAsync(
        StockAdjustedDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return _eventBus.PublishAsync(
            new StockAdjustedIntegrationEvent(
                domainEvent.Part.Value,
                domainEvent.WarehouseId.Value,
                domainEvent.Delta,
                domainEvent.Value,
                domainEvent.CurrencyCode,
                domainEvent.Reference,
                domainEvent.MovementId.Value,
                _tenantContext.TenantId),
            cancellationToken);
    }
}

/// <summary>
/// Republishes a revaluation so Finance can post the difference.
/// <para>
/// The fact that had nowhere to go for longest: the shelf was corrected and the part already sold
/// kept the old cost, with the difference sitting in a stock movement nobody consumed.
/// </para>
/// </summary>
public sealed class PublishStockRevalued : IDomainEventHandler<StockRevaluedDomainEvent>
{
    private readonly IEventBus _eventBus;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initializes the handler.</summary>
    public PublishStockRevalued(IEventBus eventBus, ITenantContext tenantContext)
    {
        _eventBus = eventBus;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public Task HandleAsync(
        StockRevaluedDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return _eventBus.PublishAsync(
            new StockRevaluedIntegrationEvent(
                domainEvent.Part.Value,
                domainEvent.WarehouseId.Value,
                domainEvent.Difference,
                domainEvent.CurrencyCode,
                domainEvent.Reference,
                domainEvent.MovementId.Value,
                _tenantContext.TenantId),
            cancellationToken);
    }
}

/// <summary>
/// Republishes a transfer that arrived short so Finance can post the shrinkage.
/// <para>
/// Goods that left one warehouse and never reached the other are gone: the company owned them at
/// breakfast and does not own them at lunch, and until now that carried its value in an event
/// nobody consumed.
/// </para>
/// </summary>
public sealed class PublishStockTransferClosedShort
    : IDomainEventHandler<StockTransferClosedShortDomainEvent>
{
    private readonly IEventBus _eventBus;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initializes the handler.</summary>
    public PublishStockTransferClosedShort(IEventBus eventBus, ITenantContext tenantContext)
    {
        _eventBus = eventBus;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public Task HandleAsync(
        StockTransferClosedShortDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return _eventBus.PublishAsync(
            new StockTransferClosedShortIntegrationEvent(
                domainEvent.StockTransferId.Value,
                domainEvent.Number,
                domainEvent.FromWarehouseId.Value,
                domainEvent.ToWarehouseId.Value,
                domainEvent.Reason,
                domainEvent.LostValue,
                domainEvent.CurrencyCode,
                _tenantContext.TenantId),
            cancellationToken);
    }
}
