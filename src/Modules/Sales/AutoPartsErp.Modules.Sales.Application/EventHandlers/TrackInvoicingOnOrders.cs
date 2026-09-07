using AutoPartsErp.IntegrationEvents.Invoicing;
using AutoPartsErp.Modules.Sales.Domain;
using AutoPartsErp.Modules.Sales.Domain.Orders;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Sales.Application.EventHandlers;

/// <summary>
/// Writes down on the order that a document has been drawn from it.
/// <para>
/// This is the return leg of the Sales–Invoicing bridge, and it goes by event rather than by
/// contract on purpose. Invoicing asks Sales a synchronous question when it needs an answer
/// before it can act; Sales is merely being told something that has already happened, and being
/// told a few hundred milliseconds late costs nothing. Making Invoicing wait for Sales to
/// acknowledge an issue would put a second module in the transaction that takes a document
/// number, which is the one transaction in this system that must stay small.
/// </para>
/// <para>
/// The consequence to be honest about: for a moment after issuing, the invoice exists and the
/// order does not know. A second draw request inside that window would be refused by the series
/// lock only if it got as far as issuing — it would not, because drawing produces a draft and two
/// drafts are harmless. What the window really costs is a duplicate draft, and somebody noticing.
/// </para>
/// </summary>
public sealed class RecordInvoiceOnSalesOrder
    : IIntegrationEventHandler<InvoiceIssuedIntegrationEvent>
{
    private readonly ISalesOrderRepository _orders;
    private readonly ISalesUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public RecordInvoiceOnSalesOrder(ISalesOrderRepository orders, ISalesUnitOfWork unitOfWork)
    {
        _orders = orders;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// The order says it was already invoiced as a different document. That is a genuine
    /// contradiction rather than a late delivery, and throwing sends the message to the inbox's
    /// retry and then to its dead letters, where a person will see it — which is the right place
    /// for two documents claiming the same order.
    /// </exception>
    public async Task HandleAsync(
        InvoiceIssuedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        // Most documents are not raised against an order. A counter sale keyed straight into
        // Invoicing has no order behind it at all, and there is nothing here to do.
        if (integrationEvent.SalesOrderId is not { } salesOrderId)
        {
            return;
        }

        SalesOrder? order = await _orders
            .GetByIdAsync(new SalesOrderId(salesOrderId), cancellationToken)
            .ConfigureAwait(false);

        // An order that is not there is not an error worth retrying forever. It means the
        // document was raised against something outside this tenant, or against an order that has
        // since been purged — neither of which this handler can fix by trying again.
        if (order is null)
        {
            return;
        }

        Result marked = order.MarkInvoiced(
            new InvoiceRef(integrationEvent.InvoiceId),
            integrationEvent.DocumentNumber,
            integrationEvent.DocumentDate);

        if (marked.IsFailure)
        {
            throw new InvalidOperationException(
                $"Cannot record {integrationEvent.DocumentNumber} against order " +
                $"{order.OrderNumber}: {marked.Error.Description}");
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Lets an order be invoiced again once the document raised against it has been voided.
/// <para>
/// Without this, voiding an invoice raised against the wrong customer would leave the order
/// permanently unbillable — the document is cancelled, the goods are still gone, and the only
/// remedy would be re-keying the whole order. The voided document keeps its number and its place
/// in the signature chain regardless; what it stops being is the thing that billed this order.
/// </para>
/// </summary>
public sealed class ClearInvoiceOnSalesOrder
    : IIntegrationEventHandler<InvoiceVoidedIntegrationEvent>
{
    private readonly ISalesOrderRepository _orders;
    private readonly ISalesUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public ClearInvoiceOnSalesOrder(ISalesOrderRepository orders, ISalesUnitOfWork unitOfWork)
    {
        _orders = orders;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task HandleAsync(
        InvoiceVoidedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        if (integrationEvent.SalesOrderId is not { } salesOrderId)
        {
            return;
        }

        SalesOrder? order = await _orders
            .GetByIdAsync(new SalesOrderId(salesOrderId), cancellationToken)
            .ConfigureAwait(false);

        if (order is null)
        {
            return;
        }

        // ClearInvoice ignores a void for a document the order is no longer pointing at, so a
        // redelivered void cannot unpick an invoice raised after it.
        order.ClearInvoice(new InvoiceRef(integrationEvent.InvoiceId));

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
