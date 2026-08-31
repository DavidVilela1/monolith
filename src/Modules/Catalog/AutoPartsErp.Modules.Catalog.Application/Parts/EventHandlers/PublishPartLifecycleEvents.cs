using AutoPartsErp.IntegrationEvents.Catalog;
using AutoPartsErp.Modules.Catalog.Domain.Parts.Events;
using AutoPartsErp.SharedKernel.Abstractions;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Primitives;

namespace AutoPartsErp.Modules.Catalog.Application.Parts.EventHandlers;

/// <summary>
/// Republishes Catalog's internal domain events as public integration events.
/// <para>
/// This translation step is the module boundary made concrete. Domain events are Catalog's own
/// business — they name Catalog's types, change whenever the aggregate changes, and no other
/// module should ever see one. Integration events are a published contract: plain records of
/// primitives, versioned deliberately, safe for anyone to consume.
/// </para>
/// <para>
/// Without this seam, the first module to subscribe directly to <c>PartActivatedDomainEvent</c>
/// would quietly weld itself to Catalog's internals, and every later change to the Part aggregate
/// would ripple outward. One small class keeps that from happening.
/// </para>
/// <para>
/// Dispatch happens after the transaction commits, so a subscriber can never react to a part
/// activation that later rolls back.
/// </para>
/// </summary>
public sealed class PublishPartActivated : IDomainEventHandler<PartActivatedDomainEvent>
{
    private readonly IEventBus _eventBus;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initializes the handler.</summary>
    public PublishPartActivated(IEventBus eventBus, ITenantContext tenantContext)
    {
        _eventBus = eventBus;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public Task HandleAsync(PartActivatedDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return _eventBus.PublishAsync(
            new PartActivatedIntegrationEvent(
                domainEvent.PartId.Value,
                domainEvent.Sku,
                domainEvent.StockUnitCode,
                _tenantContext.TenantId),
            cancellationToken);
    }
}

/// <summary>Republishes a discontinuation for Purchasing and Sales.</summary>
public sealed class PublishPartDiscontinued : IDomainEventHandler<PartDiscontinuedDomainEvent>
{
    private readonly IEventBus _eventBus;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initializes the handler.</summary>
    public PublishPartDiscontinued(IEventBus eventBus, ITenantContext tenantContext)
    {
        _eventBus = eventBus;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public Task HandleAsync(
        PartDiscontinuedDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return _eventBus.PublishAsync(
            new PartDiscontinuedIntegrationEvent(
                domainEvent.PartId.Value,
                domainEvent.SupersededBy?.Value,
                _tenantContext.TenantId),
            cancellationToken);
    }
}

/// <summary>Republishes a retirement.</summary>
public sealed class PublishPartObsoleted : IDomainEventHandler<PartObsoletedDomainEvent>
{
    private readonly IEventBus _eventBus;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initializes the handler.</summary>
    public PublishPartObsoleted(IEventBus eventBus, ITenantContext tenantContext)
    {
        _eventBus = eventBus;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public Task HandleAsync(
        PartObsoletedDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return _eventBus.PublishAsync(
            new PartObsoletedIntegrationEvent(domainEvent.PartId.Value, _tenantContext.TenantId),
            cancellationToken);
    }
}
