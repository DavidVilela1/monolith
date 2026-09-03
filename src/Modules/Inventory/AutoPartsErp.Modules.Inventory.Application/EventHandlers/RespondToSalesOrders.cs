using AutoPartsErp.IntegrationEvents.Sales;
using AutoPartsErp.Modules.Inventory.Domain;
using AutoPartsErp.Modules.Inventory.Domain.Stock;
using AutoPartsErp.SharedKernel.Abstractions;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Inventory.Application.EventHandlers;

/// <summary>
/// Holds stock back for a confirmed sales order line.
/// <para>
/// Reservations have existed in this module since it was written, complete with a sweeper that
/// returns lapsed ones. Nothing has ever created one. This is the producer they were waiting
/// for, and the reason two counter staff can no longer sell the same brake disc in the ten
/// minutes between quoting it and picking it.
/// </para>
/// <para>
/// Sales asks Inventory what is available before it confirms, so a short line is normally
/// refused at the counter and never reaches here. This still throws when there is not enough,
/// because by then something has gone wrong that a person should see — stock sold in the seconds
/// between the check and the commit, most likely.
/// </para>
/// <para>
/// The exception is a deliberate back-order, which arrives with <c>AllowPartial</c> set. Then
/// holding whatever is there is the right answer and refusing is not: somebody chose to promise
/// goods that had not arrived, and ten failed retries would be the system arguing with them.
/// </para>
/// </summary>
public sealed class ReserveStockOnSalesOrderConfirmed
    : IIntegrationEventHandler<StockReservationRequestedIntegrationEvent>
{
    private readonly IStockItemRepository _stockItems;
    private readonly IInventoryUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    /// <summary>Initializes the handler.</summary>
    public ReserveStockOnSalesOrderConfirmed(
        IStockItemRepository stockItems,
        IInventoryUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _stockItems = stockItems;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// There is no stock record for the part in that warehouse, the units disagree, or there is
    /// not enough available to hold.
    /// </exception>
    public async Task HandleAsync(
        StockReservationRequestedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        StockItem stockItem = await SalesStockContext
            .LoadAsync(
                _stockItems,
                integrationEvent.PartId,
                integrationEvent.WarehouseId,
                integrationEvent.UnitCode,
                integrationEvent.OrderNumber,
                cancellationToken)
            .ConfigureAwait(false);

        // Belt as well as the inbox's braces. The inbox stops the same message being applied
        // twice; this also covers a second confirmation of the same order reaching us by some
        // other route, which would otherwise hold the stock twice over.
        StockReservation? already = SalesStockContext.FindActiveReservation(
            stockItem, integrationEvent.OrderNumber, integrationEvent.SalesOrderLineId);

        if (already is not null)
        {
            // Deliberately writes nothing, so no inbox row either. The path is a pure no-op and
            // re-running it changes nothing; if it ever grows a side effect it needs a save.
            return;
        }

        Result<MovementReference> reference = MovementReference.Create(
            ReferenceType.SalesOrder,
            integrationEvent.OrderNumber,
            SalesStockContext.LineNote(integrationEvent.SalesOrderLineId));

        if (reference.IsFailure)
        {
            throw new InvalidOperationException(
                $"Could not build a reference for {integrationEvent.OrderNumber}: {reference.Error}");
        }

        decimal toHold = integrationEvent.Quantity;

        if (integrationEvent.AllowPartial)
        {
            decimal available = stockItem.Available.Value;

            if (available <= 0m)
            {
                // A back-order with nothing on the shelf to hold. Not a failure — the order is
                // outstanding and will be picked when the goods arrive. Writes nothing, so no
                // inbox row either; re-running it would decide the same thing.
                return;
            }

            toHold = Math.Min(toHold, available);
        }

        Result<StockReservation> reserved = stockItem.Reserve(
            toHold, reference.Value, _clock.UtcNow);

        if (reserved.IsFailure)
        {
            throw new InvalidOperationException(
                $"Could not reserve {toHold} of part {integrationEvent.PartId} " +
                $"for {integrationEvent.OrderNumber}: {reserved.Error}");
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Takes stock off the shelf when goods leave against a sales order.
/// <para>
/// A reservation is all-or-nothing — <c>Fulfil</c> consumes the whole claim — but a dispatch is
/// often partial. So a partial dispatch releases the claim, issues what actually went, and
/// re-claims the remainder. Three operations rather than one, and the alternative is a reserved
/// quantity that slowly stops matching what is really promised.
/// </para>
/// </summary>
public sealed class IssueStockOnGoodsDispatched
    : IIntegrationEventHandler<GoodsDispatchedIntegrationEvent>
{
    private readonly IStockItemRepository _stockItems;
    private readonly IStockMovementRepository _movements;
    private readonly IInventoryUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    /// <summary>Initializes the handler.</summary>
    public IssueStockOnGoodsDispatched(
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
    /// There is no stock record, the units disagree, or the balance will not cover the issue.
    /// </exception>
    public async Task HandleAsync(
        GoodsDispatchedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        StockItem stockItem = await SalesStockContext
            .LoadAsync(
                _stockItems,
                integrationEvent.PartId,
                integrationEvent.WarehouseId,
                integrationEvent.UnitCode,
                integrationEvent.OrderNumber,
                cancellationToken)
            .ConfigureAwait(false);

        Result<MovementReference> reference = MovementReference.Create(
            ReferenceType.SalesOrder,
            integrationEvent.OrderNumber,
            SalesStockContext.LineNote(integrationEvent.SalesOrderLineId));

        if (reference.IsFailure)
        {
            throw new InvalidOperationException(
                $"Could not build a reference for {integrationEvent.OrderNumber}: {reference.Error}");
        }

        DateTimeOffset now = _clock.UtcNow;
        decimal dispatched = integrationEvent.Quantity;

        StockReservation? claim = SalesStockContext.FindActiveReservation(
            stockItem, integrationEvent.OrderNumber, integrationEvent.SalesOrderLineId);

        Result<StockMovement> movement;

        if (claim is null)
        {
            // No claim left: it expired, or somebody released it by hand. The goods still went,
            // so the balance still has to come down.
            movement = stockItem.Issue(dispatched, reference.Value, now);
        }
        else if (claim.Quantity.Value == dispatched)
        {
            movement = stockItem.Fulfil(claim.Id, now);
        }
        else
        {
            decimal remainder = claim.Quantity.Value - dispatched;

            Result released = stockItem.Release(claim.Id);
            if (released.IsFailure)
            {
                throw new InvalidOperationException(
                    $"Could not release the claim on {integrationEvent.OrderNumber}: {released.Error}");
            }

            movement = stockItem.Issue(dispatched, reference.Value, now);

            if (movement.IsSuccess && remainder > 0m)
            {
                Result<StockReservation> reclaimed =
                    stockItem.Reserve(remainder, reference.Value, now);

                if (reclaimed.IsFailure)
                {
                    throw new InvalidOperationException(
                        $"Issued {dispatched} against {integrationEvent.OrderNumber} but could not " +
                        $"re-claim the remaining {remainder}: {reclaimed.Error}");
                }
            }
        }

        if (movement.IsFailure)
        {
            throw new InvalidOperationException(
                $"Could not issue {dispatched} of part {integrationEvent.PartId} against " +
                $"{integrationEvent.OrderNumber}: {movement.Error}");
        }

        _movements.Add(movement.Value);

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Gives back everything a cancelled order was holding.
/// <para>
/// The event names the order and nothing else, so the claims are found by their reference. That
/// is deliberate: an event that had to carry its lines would have to be kept in step with them.
/// </para>
/// </summary>
public sealed class ReleaseStockOnSalesOrderCancelled
    : IIntegrationEventHandler<SalesOrderCancelledIntegrationEvent>
{
    private readonly IStockItemRepository _stockItems;
    private readonly IInventoryUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public ReleaseStockOnSalesOrderCancelled(
        IStockItemRepository stockItems,
        IInventoryUnitOfWork unitOfWork)
    {
        _stockItems = stockItems;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task HandleAsync(
        SalesOrderCancelledIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        IReadOnlyList<StockItem> holding = await _stockItems
            .GetWithActiveReservationForAsync(integrationEvent.OrderNumber, cancellationToken)
            .ConfigureAwait(false);

        if (holding.Count == 0)
        {
            // Deliberately writes nothing, so no inbox row either. Nothing was being held —
            // the order was never confirmed, or the claims already lapsed.
            return;
        }

        foreach (StockItem stockItem in holding)
        {
            // Every claim, not the first: one stock item can hold one per line, and giving back
            // some of them would leave stock reserved for an order that no longer exists.
            foreach (StockReservation claim in
                SalesStockContext.FindActiveReservations(stockItem, integrationEvent.OrderNumber))
            {
                Result released = stockItem.Release(claim.Id);
                if (released.IsFailure)
                {
                    throw new InvalidOperationException(
                        $"Could not release the claim on {integrationEvent.OrderNumber} for part " +
                        $"{stockItem.Part}: {released.Error}");
                }
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>The loading and lookup all three Sales handlers repeat.</summary>
internal static class SalesStockContext
{
    /// <summary>The note a Sales reservation carries, identifying which line claimed the stock.</summary>
    public static string LineNote(Guid salesOrderLineId) => $"Line {salesOrderLineId}";

    /// <summary>Loads the balance for a part in a warehouse, refusing anything it cannot apply truthfully.</summary>
    public static async Task<StockItem> LoadAsync(
        IStockItemRepository stockItems,
        Guid partId,
        Guid warehouseId,
        string unitCode,
        string orderNumber,
        CancellationToken cancellationToken)
    {
        StockItem? stockItem = await stockItems
            .GetAsync(new PartRef(partId), new WarehouseId(warehouseId), cancellationToken)
            .ConfigureAwait(false);

        if (stockItem is null)
        {
            throw new InvalidOperationException(
                $"Cannot act on {orderNumber}: no stock record exists for part {partId} in " +
                $"warehouse {warehouseId}. The part was probably never activated in the catalogue.");
        }

        if (!string.Equals(stockItem.Unit.Code, unitCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Cannot act on {orderNumber}: the line is in '{unitCode}' but part {partId} is " +
                $"stocked in '{stockItem.Unit.Code}'. Acting anyway would put the wrong number on the shelf.");
        }

        return stockItem;
    }

    /// <summary>
    /// The active claim one order <i>line</i> holds on a balance, if it still holds one.
    /// <para>
    /// Matched on the line as well as the order number. Today one stock item can only hold one
    /// claim per order, because Sales refuses a repeated part on an order — but that is an
    /// ordinary rule to relax (the same part at two prices, or for two dates), and the day it is
    /// relaxed, matching on the document alone would silently skip the second line's reservation
    /// and consume the wrong claim on dispatch.
    /// </para>
    /// </summary>
    public static StockReservation? FindActiveReservation(
        StockItem stockItem,
        string orderNumber,
        Guid salesOrderLineId)
    {
        string normalized = orderNumber?.Trim().ToUpperInvariant() ?? string.Empty;
        string note = LineNote(salesOrderLineId);

        return stockItem.Reservations.FirstOrDefault(reservation =>
            reservation.IsActive
            && reservation.Reference.Number == normalized
            && reservation.Reference.Note == note);
    }

    /// <summary>
    /// Every active claim a document holds on a balance.
    /// <para>
    /// A cancellation names the order and not its lines, so it has to give back all of them.
    /// </para>
    /// </summary>
    public static IReadOnlyList<StockReservation> FindActiveReservations(
        StockItem stockItem,
        string orderNumber)
    {
        string normalized = orderNumber?.Trim().ToUpperInvariant() ?? string.Empty;

        return [.. stockItem.Reservations.Where(reservation =>
            reservation.IsActive && reservation.Reference.Number == normalized)];
    }
}
