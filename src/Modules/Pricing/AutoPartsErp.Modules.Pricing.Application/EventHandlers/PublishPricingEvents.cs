using AutoPartsErp.IntegrationEvents.Pricing;
using AutoPartsErp.Modules.Pricing.Domain;
using AutoPartsErp.Modules.Pricing.Domain.Customers.Events;
using AutoPartsErp.Modules.Pricing.Domain.PriceLists;
using AutoPartsErp.Modules.Pricing.Domain.PriceLists.Events;
using AutoPartsErp.SharedKernel.Abstractions;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Primitives;

namespace AutoPartsErp.Modules.Pricing.Application.EventHandlers;

/// <summary>
/// Republishes a price change as a public contract.
/// <para>
/// The same translation step every other module uses: the domain event keeps its strongly typed
/// identifiers inside the module, and what leaves is a flat record of Guids and decimals.
/// </para>
/// <para>
/// The currency has to be fetched, because a price change knows the amount but the currency is a
/// property of the list. Repeating it on the domain event would be repeating something that can
/// then contradict its source.
/// </para>
/// </summary>
public sealed class PublishPriceChanged : IDomainEventHandler<PriceChangedDomainEvent>
{
    private readonly IEventBus _eventBus;
    private readonly IPriceListRepository _lists;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initializes the handler.</summary>
    public PublishPriceChanged(
        IEventBus eventBus,
        IPriceListRepository lists,
        ITenantContext tenantContext)
    {
        _eventBus = eventBus;
        _lists = lists;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public async Task HandleAsync(
        PriceChangedDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        PriceList? list = await _lists
            .GetByIdAsync(domainEvent.PriceListId, cancellationToken)
            .ConfigureAwait(false);

        // The list was loaded a moment ago to make this change, so it is in the change tracker
        // and this costs nothing. A null here means somebody deleted the list inside the same
        // transaction, which is not a thing any command does - and announcing a price with no
        // currency would be worse than announcing nothing.
        if (list is null)
        {
            return;
        }

        await _eventBus.PublishAsync(
            new PriceChangedIntegrationEvent(
                domainEvent.PriceListId.Value,
                domainEvent.PartId.Value,
                domainEvent.MinimumQuantity,
                domainEvent.UnitPrice,
                list.CurrencyCode,
                _tenantContext.TenantId),
            cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Republishes a list going live.
/// <para>
/// Announced separately from individual price changes because activation reprices everything the
/// list touches at once. A promotion going live moves a thousand prices without a single
/// PriceChanged event, and anything holding a quote needs to hear about that.
/// </para>
/// </summary>
public sealed class PublishPriceListActivated : IDomainEventHandler<PriceListActivatedDomainEvent>
{
    private readonly IEventBus _eventBus;
    private readonly IPriceListRepository _lists;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initializes the handler.</summary>
    public PublishPriceListActivated(
        IEventBus eventBus,
        IPriceListRepository lists,
        ITenantContext tenantContext)
    {
        _eventBus = eventBus;
        _lists = lists;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public async Task HandleAsync(
        PriceListActivatedDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        PriceList? list = await _lists
            .GetByIdAsync(domainEvent.PriceListId, cancellationToken)
            .ConfigureAwait(false);

        if (list is null)
        {
            return;
        }

        await _eventBus.PublishAsync(
            new PriceListActivatedIntegrationEvent(
                list.Id.Value,
                list.Code,
                list.Kind.ToString(),
                list.CurrencyCode,
                _tenantContext.TenantId),
            cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Republishes a change to a customer's terms.
/// <para>
/// Sales cares because an open quotation for that customer is now wrong; Finance cares because the
/// margin on everything outstanding just moved.
/// </para>
/// </summary>
public sealed class PublishCustomerPricingChanged
    : IDomainEventHandler<CustomerPricingRenegotiatedDomainEvent>
{
    private readonly IEventBus _eventBus;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initializes the handler.</summary>
    public PublishCustomerPricingChanged(IEventBus eventBus, ITenantContext tenantContext)
    {
        _eventBus = eventBus;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public Task HandleAsync(
        CustomerPricingRenegotiatedDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return _eventBus.PublishAsync(
            new CustomerPricingChangedIntegrationEvent(
                domainEvent.CustomerId.Value,
                domainEvent.PreviousPriceListId.Value,
                domainEvent.PriceListId.Value,
                domainEvent.PreviousDiscountPercent,
                domainEvent.DiscountPercent,
                _tenantContext.TenantId),
            cancellationToken);
    }
}
