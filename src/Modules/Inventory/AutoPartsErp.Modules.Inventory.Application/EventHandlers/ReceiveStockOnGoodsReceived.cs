using AutoPartsErp.IntegrationEvents.Purchasing;
using AutoPartsErp.Modules.Inventory.Domain;
using AutoPartsErp.Modules.Inventory.Domain.Stock;
using AutoPartsErp.SharedKernel.Abstractions;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Inventory.Application.EventHandlers;

/// <summary>
/// Puts stock on the shelf when Purchasing books a delivery in against a purchase order.
/// <para>
/// This closes the loop that Inventory itself started. Stock fell below its reorder point, that
/// fact left as an integration event, Purchasing turned it into an order, the goods arrived, and
/// the receipt comes back here as another fact. Neither module references the other at any point
/// along the way; they meet only at two records and a Guid.
/// </para>
/// <para>
/// Note what this handler does <i>not</i> do: it does not clear the on-order quantity, because
/// nothing sets it yet. <c>SetOnOrder</c> exists on the aggregate and is still called by nobody —
/// wiring it to the submitted-order event is the obvious next thing, and would make "how many are
/// actually coming?" answerable at the counter.
/// </para>
/// </summary>
public sealed class ReceiveStockOnGoodsReceived
    : IIntegrationEventHandler<GoodsReceivedIntegrationEvent>
{
    private readonly IStockItemRepository _stockItems;
    private readonly IStockMovementRepository _movements;
    private readonly IInventoryUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    /// <summary>Initializes the handler.</summary>
    public ReceiveStockOnGoodsReceived(
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
    /// <remarks>
    /// <para>
    /// <b>Not yet idempotent.</b> Every delivery of this event adds to the balance, so a
    /// redelivered receipt would count the goods twice. That is safe today because the in-process
    /// bus publishes once and does not retry, and it stops being safe the moment a real broker is
    /// introduced. The fix is the inbox table that belongs with the outbox already on the list —
    /// not a check bolted on here, because "have I seen this event?" is a question every consumer
    /// in the system will need to ask.
    /// </para>
    /// <para>
    /// <b>And it fails open.</b> The bus logs a throwing handler and carries on, so any of the
    /// exceptions below leave the purchase order recorded as received while the stock never
    /// arrives on the balance — two records that disagree, with a log line as the only trace.
    /// The same outbox work is what makes that recoverable; until then the log is load-bearing.
    /// </para>
    /// <para>
    /// The tenant comes from the ambient <see cref="ITenantContext"/>, not from the event's own
    /// <c>TenantId</c>, which is correct only because the in-process bus runs the handler inside
    /// the publishing request. Dispatch this out of band and the query filters would resolve to
    /// an empty tenant and match nothing. Every handler in the system shares that assumption.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// There is no stock record for the part in that warehouse, or the delivery arrived in a
    /// different unit from the one the balance is kept in. Both mean the receipt cannot be
    /// applied truthfully, and a wrong balance is worse than a loud failure: the event bus logs
    /// it with the handler and event identity, and somebody has to look at the order.
    /// </exception>
    public async Task HandleAsync(
        GoodsReceivedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        var part = new PartRef(integrationEvent.PartId);
        var warehouse = new WarehouseId(integrationEvent.WarehouseId);

        StockItem? stockItem = await _stockItems
            .GetAsync(part, warehouse, cancellationToken)
            .ConfigureAwait(false);

        if (stockItem is null)
        {
            throw new InvalidOperationException(
                $"Cannot receive {integrationEvent.OrderNumber}: no stock record exists for part " +
                $"{integrationEvent.PartId} in warehouse {integrationEvent.WarehouseId}. The part " +
                "was probably never activated in the catalogue.");
        }

        if (!string.Equals(stockItem.Unit.Code, integrationEvent.UnitCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Cannot receive {integrationEvent.OrderNumber}: the order line is in " +
                $"'{integrationEvent.UnitCode}' but part {integrationEvent.PartId} is stocked in " +
                $"'{stockItem.Unit.Code}'. Booking it in anyway would put the wrong number on the shelf.");
        }

        // The purchase order number, not a separate goods-received number. There is no GRN
        // document in the system yet, and inventing one here would create a reference that
        // points at nothing findable.
        Result<MovementReference> reference = MovementReference.Create(
            ReferenceType.GoodsReceipt,
            integrationEvent.OrderNumber,
            $"Purchase order line {integrationEvent.PurchaseOrderLineId}");

        if (reference.IsFailure)
        {
            throw new InvalidOperationException(
                $"Could not build a movement reference for {integrationEvent.OrderNumber}: " +
                $"{reference.Error}");
        }

        Result<StockMovement> movement = stockItem.Receive(
            integrationEvent.Quantity, reference.Value, _clock.UtcNow);

        if (movement.IsFailure)
        {
            throw new InvalidOperationException(
                $"Could not receive {integrationEvent.Quantity} of part {integrationEvent.PartId} " +
                $"against {integrationEvent.OrderNumber}: {movement.Error}");
        }

        _movements.Add(movement.Value);

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
