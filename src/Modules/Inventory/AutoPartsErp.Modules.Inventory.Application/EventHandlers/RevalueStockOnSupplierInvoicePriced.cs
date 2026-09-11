using AutoPartsErp.IntegrationEvents.Purchasing;
using AutoPartsErp.Modules.Inventory.Domain;
using AutoPartsErp.Modules.Inventory.Domain.Stock;
using AutoPartsErp.SharedKernel.Abstractions;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Inventory.Application.EventHandlers;

/// <summary>
/// Corrects what the shelf is worth once the supplier says what the delivery actually cost.
/// <para>
/// A receipt is booked in at the purchase order's price, because that is the only figure anybody
/// has while the van is being unloaded. The invoice arrives days later with a different one — a
/// rise the order predated, a rebate taken off, a carriage line — and the shelf has been carrying
/// a figure nobody will ever pay since.
/// </para>
/// <para>
/// <b>Only what is still on the shelf is corrected.</b> The rest was sold at the old cost and is
/// gone: that share of the difference belongs in cost of sale, and this system has no general
/// ledger to put it in. Putting it on the shelf anyway would load the whole error onto whatever
/// happens to be left — twenty units out of a hundred each absorbing five times their share — and
/// the stock value would stop being a number anybody could explain. It is reported as a known gap
/// rather than quietly parked somewhere convenient.
/// </para>
/// </summary>
public sealed class RevalueStockOnSupplierInvoicePriced
    : IIntegrationEventHandler<SupplierInvoicePricedIntegrationEvent>
{
    private readonly IStockItemRepository _stockItems;
    private readonly IStockMovementRepository _movements;
    private readonly IInventoryUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    /// <summary>Initializes the handler.</summary>
    public RevalueStockOnSupplierInvoicePriced(
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
    public async Task HandleAsync(
        SupplierInvoicePricedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        var corrected = false;

        foreach (InvoicedLine line in integrationEvent.Lines)
        {
            if (await RevalueAsync(integrationEvent, line, cancellationToken).ConfigureAwait(false))
            {
                corrected = true;
            }
        }

        if (corrected)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<bool> RevalueAsync(
        SupplierInvoicePricedIntegrationEvent integrationEvent,
        InvoicedLine line,
        CancellationToken cancellationToken)
    {
        var part = new PartRef(line.PartId);
        var warehouse = new WarehouseId(line.WarehouseId);

        StockItem? stockItem = await _stockItems
            .GetAsync(part, warehouse, cancellationToken)
            .ConfigureAwait(false);

        // Nothing here throws. The invoice is settled and the company owes the money whatever this
        // module can or cannot do about it, and a delivery must never become unpayable because a
        // stock record went missing.
        if (stockItem is null)
        {
            return false;
        }

        // The same reference the receipt wrote, rebuilt. Number and note both, because the note is
        // what identifies the order line: without it, one line of a four-line order would be
        // valued against the whole order.
        Result<MovementReference> receiptReference = MovementReference.Create(
            ReferenceType.GoodsReceipt,
            line.OrderNumber,
            $"Purchase order line {line.PurchaseOrderLineId}");

        if (receiptReference.IsFailure)
        {
            return false;
        }

        IReadOnlyList<StockMovement> receipts = await _movements
            .GetForReferenceAsync(part, warehouse, receiptReference.Value, cancellationToken)
            .ConfigureAwait(false);

        Money? bookedIn = ValueOf(receipts, stockItem.StockValue.Currency);

        // A receipt that was never valued has no figure to correct towards. Stock that has never
        // been through a priced receipt is uncosted, and inventing the difference between a real
        // price and nothing would put the whole invoice onto the shelf as though the goods had
        // previously been free.
        if (bookedIn is null)
        {
            return false;
        }

        Currency currency = stockItem.StockValue.Currency;

        if (!string.Equals(line.CurrencyCode, currency.Code, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        Money invoiced = Money.Of(line.InvoicedUnitPrice, currency).Multiply(line.Quantity);
        Money whole = invoiced - bookedIn;

        Money share = ShareStillOnTheShelf(whole, line.Quantity, stockItem.OnHand.Value, currency);

        Result<MovementReference> reference = MovementReference.Create(
            ReferenceType.SupplierInvoice,
            integrationEvent.SupplierDocumentNumber,
            $"Purchase order line {line.PurchaseOrderLineId}");

        if (reference.IsFailure)
        {
            return false;
        }

        Result<StockMovement> movement = stockItem.Revalue(share, reference.Value, _clock.UtcNow);

        if (movement.IsFailure)
        {
            return false;
        }

        _movements.Add(movement.Value);

        return true;
    }

    /// <summary>
    /// What the receipt put on the balance, from the ledger rows it wrote.
    /// <para>
    /// Summed rather than derived from a unit cost, for the reason the ledger explains at length:
    /// a per-unit figure rounds, and a figure that rounds twice stops adding up to the balance
    /// sheet it is meant to explain.
    /// </para>
    /// </summary>
    private static Money? ValueOf(IReadOnlyList<StockMovement> receipts, Currency currency)
    {
        Money total = Money.Zero(currency);
        var found = false;

        foreach (StockMovement receipt in receipts)
        {
            if (receipt.CostValue is not { } value || value.Currency != currency)
            {
                continue;
            }

            total += value;
            found = true;
        }

        return found ? total : null;
    }

    /// <summary>
    /// The part of the difference that belongs to goods still on the shelf.
    /// <para>
    /// Proportional to what is left of what arrived, and capped at all of it: a shelf holding more
    /// than the delivery brought has been topped up from somewhere else, and those units were
    /// costed by their own receipts. Giving them a share of this invoice's difference would move
    /// an error from one place to another rather than fixing it.
    /// </para>
    /// <para>
    /// Rounded once, at the end. A share computed per unit and multiplied back would not add up to
    /// the figure the supplier charged.
    /// </para>
    /// </summary>
    private static Money ShareStillOnTheShelf(
        Money whole,
        decimal received,
        decimal onHand,
        Currency currency)
    {
        if (received <= 0m || onHand <= 0m)
        {
            return Money.Zero(currency);
        }

        if (onHand >= received)
        {
            return whole;
        }

        return Money.Of(
            decimal.Round(
                whole.Amount * onHand / received, currency.DecimalPlaces, MidpointRounding.ToEven),
            currency);
    }
}
