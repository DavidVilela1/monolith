using AutoPartsErp.IntegrationEvents.Invoicing;
using AutoPartsErp.Modules.Sales.Domain;
using AutoPartsErp.Modules.Sales.Domain.Orders;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Sales.Application.EventHandlers;

/// <summary>
/// Writes down on the order how much of it a document has charged for.
/// <para>
/// This is the return leg of the Sales–Invoicing bridge, and it goes by event rather than by
/// contract on purpose. Invoicing asks Sales a synchronous question when it needs an answer
/// before it can act; Sales is merely being told something that has already happened, and being
/// told a few hundred milliseconds late costs nothing. Making Invoicing wait for Sales to
/// acknowledge an issue would put a second module in the transaction that takes a document
/// number, which is the one transaction in this system that must stay small.
/// </para>
/// <para>
/// The consequence to be honest about: for a moment after issuing, the document exists and the
/// order still thinks those goods are unbilled. A second draw inside that window produces a
/// duplicate draft for quantities that have in fact been charged for — and unlike before, that
/// draft can now be issued, because nothing about the order forbids a second document. What
/// protects the customer is that <see cref="SalesOrder.RecordBilling"/> refuses to bill more than
/// went out: the second document's event will fail, land in the dead letters, and be seen. Not
/// prevention, but detection, and it is written down here rather than discovered later.
/// </para>
/// </summary>
public sealed class RecordBillingOnSalesOrder
    : IIntegrationEventHandler<SalesOrderBilledIntegrationEvent>
{
    private readonly ISalesOrderRepository _orders;
    private readonly ISalesUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public RecordBillingOnSalesOrder(ISalesOrderRepository orders, ISalesUnitOfWork unitOfWork)
    {
        _orders = orders;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// The order refused the quantities — most likely because they have already been charged for
    /// by another document. That is a genuine contradiction rather than a late delivery, and
    /// throwing sends the message to the inbox's retry and then to its dead letters, where a
    /// person will see it. Two documents charging for the same goods is exactly the thing that
    /// should reach a person.
    /// </exception>
    public async Task HandleAsync(
        SalesOrderBilledIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        SalesOrder? order = await _orders
            .GetByIdAsync(new SalesOrderId(integrationEvent.SalesOrderId), cancellationToken)
            .ConfigureAwait(false);

        // An order that is not there is not an error worth retrying forever. It means the document
        // was raised against something outside this tenant, or against an order that has since
        // been purged — neither of which this handler can fix by trying again.
        if (order is null)
        {
            return;
        }

        Result<Dictionary<SalesOrderLineId, Quantity>> billed = Read(integrationEvent.Lines);

        if (billed.IsFailure)
        {
            throw new InvalidOperationException(
                $"Cannot record {integrationEvent.DocumentNumber} against order "
                + $"{order.OrderNumber}: {billed.Error.Description}");
        }

        Result recorded = order.RecordBilling(billed.Value, integrationEvent.DocumentDate);

        if (recorded.IsFailure)
        {
            throw new InvalidOperationException(
                $"Cannot record {integrationEvent.DocumentNumber} against order "
                + $"{order.OrderNumber}: {recorded.Error.Description}");
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Turns the event's plain numbers back into quantities.</summary>
    internal static Result<Dictionary<SalesOrderLineId, Quantity>> Read(
        IReadOnlyList<BilledOrderLine> lines)
    {
        var billed = new Dictionary<SalesOrderLineId, Quantity>();

        foreach (BilledOrderLine line in lines)
        {
            if (!UnitOfMeasure.TryFromCode(line.UnitCode, out UnitOfMeasure unit))
            {
                return SalesErrors.Line.UnitMismatch;
            }

            Result<Quantity> quantity = Quantity.Create(line.Quantity, unit);

            if (quantity.IsFailure)
            {
                return Result.Failure<Dictionary<SalesOrderLineId, Quantity>>(quantity.Error);
            }

            var lineId = new SalesOrderLineId(line.SalesOrderLineId);

            // One document can charge for one line more than once - two deliveries of the same
            // part on one invoice - and the order cares about the total, not about how the
            // document laid it out.
            billed[lineId] = billed.TryGetValue(lineId, out Quantity? already)
                ? already + quantity.Value
                : quantity.Value;
        }

        return billed;
    }
}

/// <summary>
/// Puts billed quantities back when the document that charged for them is voided.
/// <para>
/// A voided invoice keeps its number and its place in the chain forever, but it bills nobody — so
/// what it covered becomes billable again. Without this, voiding a document raised against the
/// wrong customer would leave those goods permanently unbillable, and the only remedy would be
/// re-keying the order.
/// </para>
/// </summary>
public sealed class ReverseBillingOnSalesOrder
    : IIntegrationEventHandler<SalesOrderBillingReversedIntegrationEvent>
{
    private readonly ISalesOrderRepository _orders;
    private readonly ISalesUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public ReverseBillingOnSalesOrder(ISalesOrderRepository orders, ISalesUnitOfWork unitOfWork)
    {
        _orders = orders;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// The event named a unit this system does not know, which means the two modules disagree
    /// about something they both have in a shared assembly. Not a late delivery, and not something
    /// retrying fixes.
    /// </exception>
    public async Task HandleAsync(
        SalesOrderBillingReversedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        SalesOrder? order = await _orders
            .GetByIdAsync(new SalesOrderId(integrationEvent.SalesOrderId), cancellationToken)
            .ConfigureAwait(false);

        if (order is null)
        {
            return;
        }

        Result<Dictionary<SalesOrderLineId, Quantity>> billed =
            RecordBillingOnSalesOrder.Read(integrationEvent.Lines);

        if (billed.IsFailure)
        {
            throw new InvalidOperationException(
                $"Cannot reverse {integrationEvent.DocumentNumber} against order "
                + $"{order.OrderNumber}: {billed.Error.Description}");
        }

        Result reversed = order.ReverseBilling(billed.Value);

        if (reversed.IsFailure)
        {
            throw new InvalidOperationException(
                $"Cannot reverse {integrationEvent.DocumentNumber} against order "
                + $"{order.OrderNumber}: {reversed.Error.Description}");
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
