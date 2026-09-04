using AutoPartsErp.IntegrationEvents.Invoicing;
using AutoPartsErp.Modules.Invoicing.Domain;
using AutoPartsErp.Modules.Invoicing.Domain.Invoices;
using AutoPartsErp.Modules.Invoicing.Domain.Invoices.Events;
using AutoPartsErp.Modules.Invoicing.Domain.Series;
using AutoPartsErp.SharedKernel.Abstractions;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Primitives;

namespace AutoPartsErp.Modules.Invoicing.Application.EventHandlers;

/// <summary>
/// Republishes an issued document as a public contract.
/// <para>
/// The same translation step every other module uses. The sales order reference is fetched from
/// the document rather than carried on the domain event, because most documents do not have one
/// and an event field that is usually null teaches consumers to ignore it.
/// </para>
/// </summary>
public sealed class PublishInvoiceIssued : IDomainEventHandler<InvoiceIssuedDomainEvent>
{
    private readonly IEventBus _eventBus;
    private readonly IInvoiceRepository _invoices;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initializes the handler.</summary>
    public PublishInvoiceIssued(
        IEventBus eventBus,
        IInvoiceRepository invoices,
        ITenantContext tenantContext)
    {
        _eventBus = eventBus;
        _invoices = invoices;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public async Task HandleAsync(
        InvoiceIssuedDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        Invoice? invoice = await _invoices
            .GetByIdAsync(domainEvent.InvoiceId, cancellationToken)
            .ConfigureAwait(false);

        await _eventBus.PublishAsync(
            new InvoiceIssuedIntegrationEvent(
                domainEvent.InvoiceId.Value,
                domainEvent.Type.Code(),
                domainEvent.DocumentNumber,
                domainEvent.Atcud,
                domainEvent.CustomerId.Value,
                invoice?.SalesOrderId?.Value,
                domainEvent.NetTotal,
                domainEvent.VatTotal,
                domainEvent.GrossTotal,
                domainEvent.CurrencyCode,
                domainEvent.DocumentDate,
                _tenantContext.TenantId),
            cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Republishes a voided document.</summary>
public sealed class PublishInvoiceVoided : IDomainEventHandler<InvoiceVoidedDomainEvent>
{
    private readonly IEventBus _eventBus;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initializes the handler.</summary>
    public PublishInvoiceVoided(IEventBus eventBus, ITenantContext tenantContext)
    {
        _eventBus = eventBus;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public Task HandleAsync(
        InvoiceVoidedDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return _eventBus.PublishAsync(
            new InvoiceVoidedIntegrationEvent(
                domainEvent.InvoiceId.Value,
                domainEvent.Type.Code(),
                domainEvent.DocumentNumber,
                domainEvent.CustomerId.Value,
                domainEvent.GrossTotal,
                domainEvent.Reason,
                _tenantContext.TenantId),
            cancellationToken);
    }
}
