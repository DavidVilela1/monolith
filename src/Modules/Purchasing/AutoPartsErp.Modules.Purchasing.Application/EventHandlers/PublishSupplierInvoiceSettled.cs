using AutoPartsErp.IntegrationEvents.Purchasing;
using AutoPartsErp.ModuleContracts.Partners;
using AutoPartsErp.Modules.Purchasing.Domain.Invoices.Events;
using AutoPartsErp.SharedKernel.Abstractions;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Primitives;

namespace AutoPartsErp.Modules.Purchasing.Application.EventHandlers;

/// <summary>
/// Tells Finance the company owes a supplier's document.
/// <para>
/// The translation step this module already uses for everything that leaves it: the domain event
/// keeps its strongly typed identifiers, and what crosses the boundary is a flat record another
/// module can consume without knowing this aggregate exists.
/// </para>
/// <para>
/// The due date is worked out here rather than in Finance, and that is the one decision in the
/// handler. Partners owns the supplier's terms, Purchasing already talks to Partners, and Finance
/// talking to them as well would mean the same question asked from two modules with two chances of
/// answering it differently.
/// </para>
/// </summary>
public sealed class PublishSupplierInvoiceSettled
    : IDomainEventHandler<SupplierInvoiceSettledDomainEvent>
{
    private readonly IPartnerDirectory _partners;
    private readonly IEventBus _eventBus;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initializes the handler.</summary>
    public PublishSupplierInvoiceSettled(
        IPartnerDirectory partners,
        IEventBus eventBus,
        ITenantContext tenantContext)
    {
        _partners = partners;
        _eventBus = eventBus;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public async Task HandleAsync(
        SupplierInvoiceSettledDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        SupplierPaymentTerms? terms = await _partners
            .GetSupplierTermsAsync(domainEvent.SupplierId.Value, cancellationToken)
            .ConfigureAwait(false);

        // No agreed terms means due today, not due never. A payable with no date would sit
        // outside every payment run and every aging bucket, and the first anybody would hear of
        // it is the supplier ringing up — so it lands on today's run and somebody sees it.
        DateOnly dueDate = terms is null
            ? domainEvent.DocumentDate
            : domainEvent.DocumentDate.AddDays(terms.PaymentDays);

        await _eventBus.PublishAsync(
            new SupplierInvoiceSettledIntegrationEvent(
                domainEvent.SupplierInvoiceId.Value,
                domainEvent.SupplierId.Value,
                domainEvent.SupplierCode,
                domainEvent.SupplierDocumentNumber,
                domainEvent.DocumentDate,
                dueDate,
                domainEvent.NetAmount,
                domainEvent.VatAmount,
                domainEvent.GrossAmount,
                domainEvent.CurrencyCode,
                _tenantContext.TenantId),
            cancellationToken).ConfigureAwait(false);
    }
}
