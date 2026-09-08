using AutoPartsErp.IntegrationEvents.Purchasing;
using AutoPartsErp.Modules.Inventory.Domain;
using AutoPartsErp.Modules.Inventory.Domain.Stock;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Inventory.Application.EventHandlers;

/// <summary>
/// Records what a submitted purchase order is bringing in, so the warehouse stops asking for it.
/// <para>
/// This closes the half of the replenishment loop that was open. Stock fell below its reorder
/// point, Purchasing turned that into an order — and until now nothing told Inventory the goods
/// were coming, so the next issue dropped the balance again, raised the signal again, and put the
/// same line back on the buyer's list. Every day, until the lorry arrived.
/// </para>
/// <para>
/// The order need not be confirmed. Submitted is the point of commitment: the supplier has been
/// told, and buying the same part again on the strength of not having heard back is precisely the
/// mistake this prevents. If the supplier never delivers, the order is cancelled or closed short
/// and the expectation goes with it.
/// </para>
/// </summary>
public sealed class TrackIncomingOnPurchaseOrderSubmitted
    : IIntegrationEventHandler<PurchaseOrderSubmittedIntegrationEvent>
{
    private readonly IStockItemRepository _stockItems;
    private readonly IInventoryUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public TrackIncomingOnPurchaseOrderSubmitted(
        IStockItemRepository stockItems,
        IInventoryUnitOfWork unitOfWork)
    {
        _stockItems = stockItems;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    /// <remarks>
    /// A line for a part with no stock record is skipped rather than thrown on. The order is a
    /// real commitment either way, and refusing the whole message because one part was never
    /// activated in the catalogue would lose the expectations for the other nine lines — which
    /// would leave the buyer's list wrong about nine parts to be strict about one.
    /// </remarks>
    public async Task HandleAsync(
        PurchaseOrderSubmittedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        var warehouse = new WarehouseId(integrationEvent.WarehouseId);
        var order = new PurchaseOrderRef(integrationEvent.PurchaseOrderId);
        bool changed = false;

        foreach (OrderedPartLine line in integrationEvent.Lines)
        {
            StockItem? stockItem = await _stockItems
                .GetAsync(new PartRef(line.PartId), warehouse, cancellationToken)
                .ConfigureAwait(false);

            if (stockItem is null)
            {
                continue;
            }

            // A line ordered in a different unit from the one the balance is kept in cannot be
            // added to that balance truthfully. Skipping it understates what is coming, which
            // costs an extra order; adding it would overstate it, which costs a stockout.
            if (!string.Equals(
                    stockItem.Unit.Code, line.UnitCode, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Result expected = stockItem.ExpectIncoming(
                order,
                new PurchaseOrderLineRef(line.PurchaseOrderLineId),
                integrationEvent.OrderNumber,
                line.Quantity,
                integrationEvent.ExpectedOn);

            changed |= expected.IsSuccess;
        }

        if (changed)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}

/// <summary>
/// Stops expecting the goods on an order that was cancelled or closed short.
/// <para>
/// Two events, one behaviour, and the shared implementation says why: to the shelf, "the supplier
/// cancelled" and "we agreed to stop chasing the last four" are the same fact — the balance is not
/// coming. They stay separate events because they are different conversations with the supplier
/// and anything reporting on supplier performance will want to tell them apart.
/// </para>
/// <para>
/// Dropping an expectation is the moment the part may need buying again, from somebody else and
/// possibly in a hurry. It is also the only such moment where no stock moves — which is exactly
/// why the aggregate re-checks the reorder point here, and why a version of this that only
/// subtracted a number would be quietly wrong.
/// </para>
/// </summary>
public sealed class DropIncomingOnPurchaseOrderClosed
    : IIntegrationEventHandler<PurchaseOrderCancelledIntegrationEvent>,
      IIntegrationEventHandler<PurchaseOrderClosedShortIntegrationEvent>
{
    private readonly IStockItemRepository _stockItems;
    private readonly IInventoryUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public DropIncomingOnPurchaseOrderClosed(
        IStockItemRepository stockItems,
        IInventoryUnitOfWork unitOfWork)
    {
        _stockItems = stockItems;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public Task HandleAsync(
        PurchaseOrderCancelledIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        return DropAsync(
            integrationEvent.PurchaseOrderId,
            integrationEvent.WarehouseId,
            integrationEvent.Lines,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task HandleAsync(
        PurchaseOrderClosedShortIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        return DropAsync(
            integrationEvent.PurchaseOrderId,
            integrationEvent.WarehouseId,
            integrationEvent.Lines,
            cancellationToken);
    }

    private async Task DropAsync(
        Guid purchaseOrderId,
        Guid warehouseId,
        IReadOnlyList<OrderedPartLine> lines,
        CancellationToken cancellationToken)
    {
        var warehouse = new WarehouseId(warehouseId);
        var order = new PurchaseOrderRef(purchaseOrderId);
        bool changed = false;

        // The lines say which stock records to load; the aggregate decides what is still
        // outstanding on each. Trusting the event's quantity instead would double-subtract
        // whenever a receipt and a close crossed in flight.
        foreach (Guid partId in lines.Select(line => line.PartId).Distinct())
        {
            StockItem? stockItem = await _stockItems
                .GetAsync(new PartRef(partId), warehouse, cancellationToken)
                .ConfigureAwait(false);

            if (stockItem is null)
            {
                continue;
            }

            changed |= stockItem.CancelIncoming(order) > 0;
        }

        if (changed)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
