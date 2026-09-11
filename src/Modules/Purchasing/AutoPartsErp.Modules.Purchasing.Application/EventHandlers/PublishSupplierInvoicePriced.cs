using AutoPartsErp.IntegrationEvents.Purchasing;
using AutoPartsErp.Modules.Purchasing.Domain;
using AutoPartsErp.Modules.Purchasing.Domain.Invoices;
using AutoPartsErp.Modules.Purchasing.Domain.Invoices.Events;
using AutoPartsErp.Modules.Purchasing.Domain.Orders;
using AutoPartsErp.SharedKernel.Abstractions;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Primitives;

namespace AutoPartsErp.Modules.Purchasing.Application.EventHandlers;

/// <summary>
/// Tells Inventory when a supplier charged something the purchase order had not predicted.
/// <para>
/// The goods went onto the shelf at the price the order was placed at, because that is the only
/// figure anybody had when the van arrived. The supplier's invoice is the first time the company
/// knows what the delivery really cost, and until the difference reaches the shelf the balance
/// sheet disagrees with the money about to leave the bank.
/// </para>
/// <para>
/// Only the lines that actually differ are published. Most deliveries are invoiced at the price
/// they were ordered at, and an event listing every line of every document would be almost
/// entirely rows asking the other module to do nothing — and would put a write into Inventory's
/// schema behind every purchase in the company.
/// </para>
/// <para>
/// Nothing is published at all when every line agrees. A translation step that publishes an empty
/// event is a translation step that wakes a handler, opens a transaction and saves nothing, once
/// per delivery, forever.
/// </para>
/// </summary>
public sealed class PublishSupplierInvoicePriced
    : IDomainEventHandler<SupplierInvoiceSettledDomainEvent>
{
    private readonly ISupplierInvoiceRepository _invoices;
    private readonly IPurchaseOrderRepository _orders;
    private readonly IEventBus _eventBus;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initializes the handler.</summary>
    public PublishSupplierInvoicePriced(
        ISupplierInvoiceRepository invoices,
        IPurchaseOrderRepository orders,
        IEventBus eventBus,
        ITenantContext tenantContext)
    {
        _invoices = invoices;
        _orders = orders;
        _eventBus = eventBus;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public async Task HandleAsync(
        SupplierInvoiceSettledDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        SupplierInvoice? invoice = await _invoices
            .GetByIdAsync(domainEvent.SupplierInvoiceId, cancellationToken)
            .ConfigureAwait(false);

        if (invoice is null)
        {
            return;
        }

        var priced = new List<InvoicedLine>();

        // Cached per order, because a document usually collects several lines from the same one
        // and asking for it once per line would be three round trips to answer one question.
        var seen = new Dictionary<PurchaseOrderId, PurchaseOrder?>();

        foreach (SupplierInvoiceLine line in invoice.Lines)
        {
            if (!seen.TryGetValue(line.PurchaseOrderId, out PurchaseOrder? order))
            {
                order = await _orders
                    .GetByIdAsync(line.PurchaseOrderId, cancellationToken)
                    .ConfigureAwait(false);

                seen[line.PurchaseOrderId] = order;
            }

            if (order is null)
            {
                continue;
            }

            PurchaseOrderLine? ordered = order.Lines
                .FirstOrDefault(candidate => candidate.Id == line.PurchaseOrderLineId);

            // The comparison is against what the receipt was booked in at, which is the order
            // line's price. Comparing against the agreed price instead would miss the case this
            // exists for: an order placed before a price rise, received after it, and invoiced at
            // the new figure.
            if (ordered is null || ordered.UnitPrice.Amount == line.UnitPrice.Amount)
            {
                continue;
            }

            priced.Add(new InvoicedLine(
                order.OrderNumber,
                line.PurchaseOrderLineId.Value,
                line.PartId.Value,
                order.DeliverToWarehouseId.Value,
                line.Quantity.Value,
                line.UnitPrice.Amount,
                line.UnitPrice.Currency.Code));
        }

        if (priced.Count == 0)
        {
            return;
        }

        await _eventBus.PublishAsync(
            new SupplierInvoicePricedIntegrationEvent(
                invoice.Id.Value,
                domainEvent.SupplierDocumentNumber,
                domainEvent.DocumentDate,
                priced,
                _tenantContext.TenantId),
            cancellationToken).ConfigureAwait(false);
    }
}
