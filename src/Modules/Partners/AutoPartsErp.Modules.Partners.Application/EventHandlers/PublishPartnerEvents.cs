using AutoPartsErp.IntegrationEvents.Partners;
using AutoPartsErp.Modules.Partners.Domain.Partners.Events;
using AutoPartsErp.SharedKernel.Abstractions;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Primitives;

namespace AutoPartsErp.Modules.Partners.Application.EventHandlers;

/// <summary>
/// Republishes a hold so Sales can stop serving the account.
/// Same translation step as Catalog uses: the domain event stays private, the contract goes out.
/// </summary>
public sealed class PublishPartnerPlacedOnHold : IDomainEventHandler<PartnerPlacedOnHoldDomainEvent>
{
    private readonly IEventBus _eventBus;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initializes the handler.</summary>
    public PublishPartnerPlacedOnHold(IEventBus eventBus, ITenantContext tenantContext)
    {
        _eventBus = eventBus;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public Task HandleAsync(
        PartnerPlacedOnHoldDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return _eventBus.PublishAsync(
            new PartnerPlacedOnHoldIntegrationEvent(
                domainEvent.PartnerId.Value,
                domainEvent.Code,
                domainEvent.Reason,
                _tenantContext.TenantId),
            cancellationToken);
    }
}

/// <summary>Republishes the lifting of a hold.</summary>
public sealed class PublishPartnerHoldReleased : IDomainEventHandler<PartnerHoldReleasedDomainEvent>
{
    private readonly IEventBus _eventBus;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initializes the handler.</summary>
    public PublishPartnerHoldReleased(IEventBus eventBus, ITenantContext tenantContext)
    {
        _eventBus = eventBus;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public Task HandleAsync(
        PartnerHoldReleasedDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return _eventBus.PublishAsync(
            new PartnerHoldReleasedIntegrationEvent(
                domainEvent.PartnerId.Value,
                domainEvent.Code,
                _tenantContext.TenantId),
            cancellationToken);
    }
}

/// <summary>Republishes a credit limit change for Finance.</summary>
public sealed class PublishCreditLimitChanged : IDomainEventHandler<CreditLimitChangedDomainEvent>
{
    private readonly IEventBus _eventBus;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initializes the handler.</summary>
    public PublishCreditLimitChanged(IEventBus eventBus, ITenantContext tenantContext)
    {
        _eventBus = eventBus;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public Task HandleAsync(
        CreditLimitChangedDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return _eventBus.PublishAsync(
            new CreditLimitChangedIntegrationEvent(
                domainEvent.PartnerId.Value,
                domainEvent.Code,
                domainEvent.PreviousLimit,
                domainEvent.NewLimit,
                _tenantContext.TenantId),
            cancellationToken);
    }
}

/// <summary>Republishes the opening of a customer account so Sales can serve them.</summary>
public sealed class PublishCustomerAccountOpened : IDomainEventHandler<CustomerRoleGrantedDomainEvent>
{
    private readonly IEventBus _eventBus;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initializes the handler.</summary>
    public PublishCustomerAccountOpened(IEventBus eventBus, ITenantContext tenantContext)
    {
        _eventBus = eventBus;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public Task HandleAsync(
        CustomerRoleGrantedDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return _eventBus.PublishAsync(
            new CustomerAccountOpenedIntegrationEvent(
                domainEvent.PartnerId.Value,
                domainEvent.Code,
                domainEvent.LegalName,
                domainEvent.CreditLimit,
                domainEvent.CurrencyCode,
                domainEvent.PaymentDueInDays,
                domainEvent.PaymentEndOfMonth,
                domainEvent.PriceListCode,
                _tenantContext.TenantId),
            cancellationToken);
    }
}

/// <summary>Republishes the end of a relationship.</summary>
public sealed class PublishPartnerClosed : IDomainEventHandler<PartnerClosedDomainEvent>
{
    private readonly IEventBus _eventBus;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initializes the handler.</summary>
    public PublishPartnerClosed(IEventBus eventBus, ITenantContext tenantContext)
    {
        _eventBus = eventBus;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public Task HandleAsync(
        PartnerClosedDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return _eventBus.PublishAsync(
            new PartnerClosedIntegrationEvent(
                domainEvent.PartnerId.Value,
                domainEvent.Code,
                _tenantContext.TenantId),
            cancellationToken);
    }
}
