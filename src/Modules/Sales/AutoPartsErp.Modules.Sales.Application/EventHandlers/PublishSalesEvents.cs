using AutoPartsErp.IntegrationEvents.Sales;
using AutoPartsErp.Modules.Sales.Domain.Orders.Events;
using AutoPartsErp.SharedKernel.Abstractions;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Primitives;

namespace AutoPartsErp.Modules.Sales.Application.EventHandlers;

/// <summary>Republishes a confirmed order for anything that cares what has been sold.</summary>
public sealed class PublishSalesOrderConfirmed
    : IDomainEventHandler<SalesOrderConfirmedDomainEvent>
{
    private readonly IEventBus _eventBus;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initializes the handler.</summary>
    public PublishSalesOrderConfirmed(IEventBus eventBus, ITenantContext tenantContext)
    {
        _eventBus = eventBus;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public Task HandleAsync(
        SalesOrderConfirmedDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return _eventBus.PublishAsync(
            new SalesOrderConfirmedIntegrationEvent(
                domainEvent.SalesOrderId.Value,
                domainEvent.OrderNumber,
                domainEvent.CustomerId.Value,
                domainEvent.WarehouseId.Value,
                domainEvent.NetTotal,
                domainEvent.VatTotal,
                domainEvent.GrossTotal,
                domainEvent.CurrencyCode,
                domainEvent.RequiredBy,
                _tenantContext.TenantId),
            cancellationToken);
    }
}

/// <summary>
/// Asks Inventory to hold stock back for a confirmed line.
/// <para>
/// One of these per line, each its own outbox row. A line for a part with no stock record fails
/// and retries on its own schedule instead of blocking the other five.
/// </para>
/// </summary>
public sealed class PublishStockReservationRequested
    : IDomainEventHandler<StockReservationRequestedDomainEvent>
{
    private readonly IEventBus _eventBus;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initializes the handler.</summary>
    public PublishStockReservationRequested(IEventBus eventBus, ITenantContext tenantContext)
    {
        _eventBus = eventBus;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public Task HandleAsync(
        StockReservationRequestedDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return _eventBus.PublishAsync(
            new StockReservationRequestedIntegrationEvent(
                domainEvent.SalesOrderId.Value,
                domainEvent.OrderNumber,
                domainEvent.LineId.Value,
                domainEvent.PartId.Value,
                domainEvent.WarehouseId.Value,
                domainEvent.Quantity,
                domainEvent.UnitCode,
                _tenantContext.TenantId),
            cancellationToken);
    }
}

/// <summary>Tells Inventory that goods have physically left, so it can take them off the shelf.</summary>
public sealed class PublishGoodsDispatched : IDomainEventHandler<GoodsDispatchedDomainEvent>
{
    private readonly IEventBus _eventBus;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initializes the handler.</summary>
    public PublishGoodsDispatched(IEventBus eventBus, ITenantContext tenantContext)
    {
        _eventBus = eventBus;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public Task HandleAsync(
        GoodsDispatchedDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return _eventBus.PublishAsync(
            new GoodsDispatchedIntegrationEvent(
                domainEvent.SalesOrderId.Value,
                domainEvent.OrderNumber,
                domainEvent.LineId.Value,
                domainEvent.PartId.Value,
                domainEvent.WarehouseId.Value,
                domainEvent.Quantity,
                domainEvent.UnitCode,
                _tenantContext.TenantId),
            cancellationToken);
    }
}

/// <summary>Tells Inventory to give back whatever it was holding for a cancelled order.</summary>
public sealed class PublishSalesOrderCancelled
    : IDomainEventHandler<SalesOrderCancelledDomainEvent>
{
    private readonly IEventBus _eventBus;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initializes the handler.</summary>
    public PublishSalesOrderCancelled(IEventBus eventBus, ITenantContext tenantContext)
    {
        _eventBus = eventBus;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public Task HandleAsync(
        SalesOrderCancelledDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return _eventBus.PublishAsync(
            new SalesOrderCancelledIntegrationEvent(
                domainEvent.SalesOrderId.Value,
                domainEvent.OrderNumber,
                domainEvent.CustomerId.Value,
                domainEvent.Reason,
                _tenantContext.TenantId),
            cancellationToken);
    }
}
