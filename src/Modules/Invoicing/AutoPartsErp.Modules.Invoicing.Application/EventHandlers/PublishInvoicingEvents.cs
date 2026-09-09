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

        // A second event, for the one consumer that needs the lines. Sales has to know how much
        // of each of its lines was charged for; nobody else does, and putting the lines on the
        // event above would send an order's internals to every consumer of every document.
        //
        // Credit note lines carry no sales order line - DraftCreditNote copies the line it
        // credits, not the line the original was drawn from - so BilledLines comes back empty for
        // one and no billing is recorded. That is what stops crediting an order looking like
        // invoicing more of it.
        IReadOnlyList<BilledOrderLine> billed = BilledLines(invoice);

        if (invoice?.SalesOrderId is { } salesOrderId && billed.Count > 0)
        {
            await _eventBus.PublishAsync(
                new SalesOrderBilledIntegrationEvent(
                    salesOrderId.Value,
                    domainEvent.InvoiceId.Value,
                    domainEvent.DocumentNumber,
                    domainEvent.DocumentDate,
                    billed,
                    _tenantContext.TenantId),
                cancellationToken).ConfigureAwait(false);
        }

        // And a third, for the return this credit note came from, when it came from one. Most
        // credit notes do not: a wrong price or a wrong customer involves no goods at all.
        if (invoice?.CustomerReturnId is { } customerReturnId)
        {
            await _eventBus.PublishAsync(
                new CustomerReturnCreditedIntegrationEvent(
                    customerReturnId.Value,
                    domainEvent.InvoiceId.Value,
                    domainEvent.DocumentNumber,
                    domainEvent.DocumentDate,
                    domainEvent.GrossTotal,
                    domainEvent.CurrencyCode,
                    _tenantContext.TenantId),
                cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The lines of a document that charge for a sales order line, as plain numbers.
    /// <para>
    /// Lines with no order line behind them are left out rather than sent as nulls. A counter sale
    /// keyed straight into Invoicing has none at all, and a document drawn from an order can still
    /// carry a line somebody added by hand.
    /// </para>
    /// </summary>
    internal static IReadOnlyList<BilledOrderLine> BilledLines(Invoice? invoice) =>
        invoice is null
            ? []
            : [.. invoice.Lines
                .Where(line => line.SalesOrderLineId is not null)
                .Select(line => new BilledOrderLine(
                    line.SalesOrderLineId!.Value.Value,
                    line.Quantity.Value,
                    line.Quantity.Unit.Code))];
}

/// <summary>
/// Republishes a voided document.
/// <para>
/// Loads the document for the same reason the issued handler does: the sales order reference
/// lives on the aggregate rather than on the event, and Sales needs it to work out which of its
/// orders has just become billable again.
/// </para>
/// </summary>
public sealed class PublishInvoiceVoided : IDomainEventHandler<InvoiceVoidedDomainEvent>
{
    private readonly IEventBus _eventBus;
    private readonly IInvoiceRepository _invoices;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initializes the handler.</summary>
    public PublishInvoiceVoided(
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
        InvoiceVoidedDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        Invoice? invoice = await _invoices
            .GetByIdAsync(domainEvent.InvoiceId, cancellationToken)
            .ConfigureAwait(false);

        await _eventBus.PublishAsync(
            new InvoiceVoidedIntegrationEvent(
                domainEvent.InvoiceId.Value,
                domainEvent.Type.Code(),
                domainEvent.DocumentNumber,
                domainEvent.CustomerId.Value,
                invoice?.SalesOrderId?.Value,
                domainEvent.GrossTotal,
                domainEvent.Reason,
                _tenantContext.TenantId),
            cancellationToken).ConfigureAwait(false);

        IReadOnlyList<BilledOrderLine> billed = PublishInvoiceIssued.BilledLines(invoice);

        if (invoice?.SalesOrderId is { } salesOrderId && billed.Count > 0)
        {
            await _eventBus.PublishAsync(
                new SalesOrderBillingReversedIntegrationEvent(
                    salesOrderId.Value,
                    domainEvent.InvoiceId.Value,
                    domainEvent.DocumentNumber,
                    billed,
                    _tenantContext.TenantId),
                cancellationToken).ConfigureAwait(false);
        }
    }
}
