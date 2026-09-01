using AutoPartsErp.IntegrationEvents.Purchasing;
using AutoPartsErp.Modules.Purchasing.Domain.Orders.Events;
using AutoPartsErp.SharedKernel.Abstractions;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Primitives;

namespace AutoPartsErp.Modules.Purchasing.Application.EventHandlers;

/// <summary>
/// Republishes a submitted order as a public contract.
/// <para>
/// The same translation step Catalog, Inventory and Partners all use. The domain event stays
/// inside the module with its strongly typed identifiers; what leaves is a flat record of Guids
/// and decimals that another module — or another process, later — can consume without knowing
/// this aggregate exists.
/// </para>
/// </summary>
public sealed class PublishPurchaseOrderSubmitted
    : IDomainEventHandler<PurchaseOrderSubmittedDomainEvent>
{
    private readonly IEventBus _eventBus;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initializes the handler.</summary>
    public PublishPurchaseOrderSubmitted(IEventBus eventBus, ITenantContext tenantContext)
    {
        _eventBus = eventBus;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public Task HandleAsync(
        PurchaseOrderSubmittedDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return _eventBus.PublishAsync(
            new PurchaseOrderSubmittedIntegrationEvent(
                domainEvent.PurchaseOrderId.Value,
                domainEvent.OrderNumber,
                domainEvent.SupplierId.Value,
                domainEvent.WarehouseId.Value,
                domainEvent.Total,
                domainEvent.CurrencyCode,
                domainEvent.ExpectedOn,
                _tenantContext.TenantId),
            cancellationToken);
    }
}

/// <summary>
/// Republishes a goods receipt so Inventory can put the stock on a shelf.
/// <para>
/// This is the return leg of the loop Inventory started: it reported that a part had run low,
/// Purchasing turned that into an order, and the delivery against that order now comes back as a
/// fact Inventory acts on. Neither module references the other at any point along the way.
/// </para>
/// </summary>
public sealed class PublishGoodsReceived : IDomainEventHandler<GoodsReceivedDomainEvent>
{
    private readonly IEventBus _eventBus;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initializes the handler.</summary>
    public PublishGoodsReceived(IEventBus eventBus, ITenantContext tenantContext)
    {
        _eventBus = eventBus;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public Task HandleAsync(
        GoodsReceivedDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return _eventBus.PublishAsync(
            new GoodsReceivedIntegrationEvent(
                domainEvent.PurchaseOrderId.Value,
                domainEvent.OrderNumber,
                domainEvent.LineId.Value,
                domainEvent.PartId.Value,
                domainEvent.WarehouseId.Value,
                domainEvent.Quantity,
                domainEvent.UnitCode,
                domainEvent.UnitPrice,
                domainEvent.CurrencyCode,
                _tenantContext.TenantId),
            cancellationToken);
    }
}

/// <summary>Republishes a cancellation so anything counting on the goods stops counting on them.</summary>
public sealed class PublishPurchaseOrderCancelled
    : IDomainEventHandler<PurchaseOrderCancelledDomainEvent>
{
    private readonly IEventBus _eventBus;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initializes the handler.</summary>
    public PublishPurchaseOrderCancelled(IEventBus eventBus, ITenantContext tenantContext)
    {
        _eventBus = eventBus;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public Task HandleAsync(
        PurchaseOrderCancelledDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return _eventBus.PublishAsync(
            new PurchaseOrderCancelledIntegrationEvent(
                domainEvent.PurchaseOrderId.Value,
                domainEvent.OrderNumber,
                domainEvent.Reason,
                _tenantContext.TenantId),
            cancellationToken);
    }
}
