using AutoPartsErp.IntegrationEvents.Sales;
using AutoPartsErp.Modules.Inventory.Domain;
using AutoPartsErp.Modules.Inventory.Domain.Stock;
using AutoPartsErp.SharedKernel.Abstractions;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Inventory.Application.EventHandlers;

/// <summary>
/// Puts a customer's returned goods back on the shelf, at what they cost when they left.
/// <para>
/// The mirror of <c>IssueStockOnGoodsDispatched</c>, with one thing it has to do that the
/// dispatch does not: work out what the goods are worth. Sales knows what the customer paid and
/// deliberately does not know what the company paid — costing belongs to the module that owns the
/// shelf — so the value is found here, in this module's own ledger, from the movement that took
/// the goods off it.
/// </para>
/// <para>
/// <b>Why not today's average.</b> A part sold in March at €40 and returned in September onto a
/// shelf that has since averaged down to €31 would come back worth nine euros more than it cost,
/// and the difference would be a silent profit on a transaction where the company made nothing.
/// The README carried that as a known issue with a note saying the fix needed Sales to carry a
/// cost back. It does not: the number was already here, in the row the dispatch wrote.
/// </para>
/// <para>
/// Only saleable lines arrive. Sales does not raise the event for a scrapped return, so there is
/// no disposition to read here and nothing to decide.
/// </para>
/// </summary>
public sealed class ReceiveStockOnCustomerReturn
    : IIntegrationEventHandler<GoodsReturnedIntegrationEvent>
{
    private readonly IStockItemRepository _stockItems;
    private readonly IStockMovementRepository _movements;
    private readonly IInventoryUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    /// <summary>Initializes the handler.</summary>
    public ReceiveStockOnCustomerReturn(
        IStockItemRepository stockItems,
        IStockMovementRepository movements,
        IInventoryUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _stockItems = stockItems;
        _movements = movements;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// There is no stock record for the part in that warehouse, the units disagree, or the
    /// balance refused the receipt.
    /// </exception>
    public async Task HandleAsync(
        GoodsReturnedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        StockItem stockItem = await SalesStockContext
            .LoadAsync(
                _stockItems,
                integrationEvent.PartId,
                integrationEvent.WarehouseId,
                integrationEvent.UnitCode,
                integrationEvent.ReturnNumber,
                cancellationToken)
            .ConfigureAwait(false);

        // Two references, and they are not the same one. The movement is stamped with the return
        // that brought the goods in, because that is what caused it and what somebody will trace
        // it back to. The lookup is built as the dispatch recorded it, because that is the row
        // holding the cost.
        Result<MovementReference> reference = MovementReference.Create(
            ReferenceType.CustomerReturn,
            integrationEvent.ReturnNumber,
            SalesStockContext.LineNote(integrationEvent.SalesOrderLineId));

        if (reference.IsFailure)
        {
            throw new InvalidOperationException(
                $"Could not build a reference for {integrationEvent.ReturnNumber}: {reference.Error}");
        }

        Result<MovementReference> dispatched = MovementReference.Create(
            ReferenceType.SalesOrder,
            integrationEvent.OrderNumber,
            SalesStockContext.LineNote(integrationEvent.SalesOrderLineId));

        if (dispatched.IsFailure)
        {
            throw new InvalidOperationException(
                $"Could not build a reference for {integrationEvent.OrderNumber}: {dispatched.Error}");
        }

        IReadOnlyList<StockMovement> history = await _movements
            .GetForReferenceAsync(
                new PartRef(integrationEvent.PartId),
                new WarehouseId(integrationEvent.WarehouseId),
                dispatched.Value,
                cancellationToken)
            .ConfigureAwait(false);

        // Null when nothing that left the shelf carried a value — stock that has never been
        // through a priced receipt. The quantity joins the balance uncosted, which is what the
        // issue did in the other direction and is the honest state rather than a cost of zero.
        Money? value = StockMovement.ValueOfReturn(history, integrationEvent.Quantity);

        Result<StockMovement> movement = stockItem.ReceiveReturn(
            integrationEvent.Quantity, reference.Value, _clock.UtcNow, value);

        if (movement.IsFailure)
        {
            throw new InvalidOperationException(
                $"Could not book {integrationEvent.ReturnNumber} back into stock: {movement.Error}");
        }

        _movements.Add(movement.Value);

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
