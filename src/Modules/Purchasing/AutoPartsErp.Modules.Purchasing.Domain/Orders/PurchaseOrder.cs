using AutoPartsErp.Modules.Purchasing.Domain.Orders.Events;
using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Purchasing.Domain.Orders;

/// <summary>
/// An instruction to a supplier to send us goods, and the record of what actually turned up.
/// <para>
/// The order is the consistency boundary, not the line. Receiving against one line changes the
/// state of the whole document — an order is "partially received" or "complete" as a unit — so
/// the two cannot be saved independently without the status going stale. That is exactly the
/// job an aggregate root exists to do.
/// </para>
/// <para>
/// Everything on the document that came from another module is a snapshot: the supplier's code,
/// each line's SKU, description and price. Purchasing does not join to Catalog or Partners to
/// render an order, because a document sent in 2026 must still read the same in 2030 after the
/// part has been renamed twice and the supplier has been bought by a competitor.
/// </para>
/// </summary>
public sealed class PurchaseOrder : AggregateRoot<PurchaseOrderId>, IAuditable, ISoftDeletable, ITenantScoped
{
    /// <summary>Longest permitted order number.</summary>
    public const int MaxOrderNumberLength = 30;

    /// <summary>Longest permitted supplier code snapshot.</summary>
    public const int MaxSupplierCodeLength = 20;

    /// <summary>Longest permitted supplier reference.</summary>
    public const int MaxSupplierReferenceLength = 50;

    /// <summary>Longest permitted free-text note or reason.</summary>
    public const int MaxNotesLength = 500;

    private readonly List<PurchaseOrderLine> _lines = [];

    private PurchaseOrder(
        PurchaseOrderId id,
        string orderNumber,
        SupplierRef supplierId,
        string supplierCode,
        WarehouseRef warehouseId,
        Currency currency)
        : base(id)
    {
        OrderNumber = orderNumber;
        SupplierId = supplierId;
        SupplierCode = supplierCode;
        DeliverToWarehouseId = warehouseId;
        CurrencyCode = currency.Code;
        Status = PurchaseOrderStatus.Draft;
        CreatedBy = string.Empty;
    }

    /// <summary>Required by EF Core materialization.</summary>
#pragma warning disable CS8618
    private PurchaseOrder()
    {
    }
#pragma warning restore CS8618

    /// <summary>The number printed on the document, e.g. "PO-2026-00042".</summary>
    public string OrderNumber { get; private set; } = string.Empty;

    /// <summary>Who we are buying from.</summary>
    public SupplierRef SupplierId { get; private set; }

    /// <summary>Their short code, as it was when the order was raised.</summary>
    public string SupplierCode { get; private set; } = string.Empty;

    /// <summary>Where the goods are to be delivered.</summary>
    public WarehouseRef DeliverToWarehouseId { get; private set; }

    /// <summary>ISO code of the currency the order is priced in.</summary>
    public string CurrencyCode { get; private set; } = Currency.Default.Code;

    /// <summary>Where the order is in its life.</summary>
    public PurchaseOrderStatus Status { get; private set; }

    /// <summary>The day it was sent to the supplier.</summary>
    public DateOnly? OrderedOn { get; private set; }

    /// <summary>The day the goods are expected, once the supplier has committed to one.</summary>
    public DateOnly? ExpectedOn { get; private set; }

    /// <summary>Their own order number, for when you have to ring them about it.</summary>
    public string? SupplierReference { get; private set; }

    /// <summary>Anything the buyer wanted to record against the order.</summary>
    public string? Notes { get; private set; }

    /// <summary>Why it was cancelled or closed short.</summary>
    public string? ClosureReason { get; private set; }

    /// <summary>The lines on the order.</summary>
    public IReadOnlyCollection<PurchaseOrderLine> Lines => _lines.AsReadOnly();

    /// <inheritdoc />
    public Guid TenantId { get; set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <inheritdoc />
    public string CreatedBy { get; set; } = string.Empty;

    /// <inheritdoc />
    public DateTimeOffset? ModifiedAtUtc { get; set; }

    /// <inheritdoc />
    public string? ModifiedBy { get; set; }

    /// <inheritdoc />
    public bool IsDeleted { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? DeletedAtUtc { get; set; }

    /// <inheritdoc />
    public string? DeletedBy { get; set; }

    /// <summary>The currency the order is priced in.</summary>
    public Currency Currency => Currency.FromCode(CurrencyCode);

    /// <summary>True while the order can still be changed.</summary>
    public bool IsEditable => Status == PurchaseOrderStatus.Draft;

    /// <summary>True when goods may be booked in against it.</summary>
    public bool CanReceive => Status is PurchaseOrderStatus.Submitted
        or PurchaseOrderStatus.Confirmed
        or PurchaseOrderStatus.PartiallyReceived;

    /// <summary>True once nothing further will happen to the order.</summary>
    public bool IsClosed => Status is PurchaseOrderStatus.Received
        or PurchaseOrderStatus.Cancelled
        or PurchaseOrderStatus.ClosedShort;

    /// <summary>True while at least one line still has something to come.</summary>
    public bool HasOutstandingLines => _lines.Exists(line => line.IsOutstanding);

    /// <summary>The order value.</summary>
    public Money Total
    {
        get
        {
            Money total = Money.Zero(Currency);

            foreach (PurchaseOrderLine line in _lines)
            {
                total += line.LineTotal;
            }

            return total;
        }
    }

    /// <summary>The value still to be delivered.</summary>
    public Money OutstandingValue
    {
        get
        {
            Money outstanding = Money.Zero(Currency);

            foreach (PurchaseOrderLine line in _lines)
            {
                outstanding += line.OutstandingValue;
            }

            return outstanding;
        }
    }

    /// <summary>
    /// Starts a draft order. Nothing has been committed to the supplier until
    /// <see cref="Submit"/> is called.
    /// </summary>
    /// <param name="orderNumber">The number to print on the document.</param>
    /// <param name="supplierId">Who we are buying from.</param>
    /// <param name="supplierCode">Their short code, snapshotted onto the document.</param>
    /// <param name="warehouseId">Where the goods are to be delivered.</param>
    /// <param name="currency">The currency the order is priced in.</param>
    public static Result<PurchaseOrder> Draft(
        string? orderNumber,
        SupplierRef supplierId,
        string? supplierCode,
        WarehouseRef warehouseId,
        Currency currency)
    {
        ArgumentNullException.ThrowIfNull(currency);

        if (string.IsNullOrWhiteSpace(orderNumber))
        {
            return PurchasingErrors.Order.NumberRequired;
        }

        if (supplierId.IsEmpty)
        {
            return PurchasingErrors.Order.SupplierRequired;
        }

        if (warehouseId.IsEmpty)
        {
            return PurchasingErrors.Order.WarehouseRequired;
        }

        string number = orderNumber.Trim().ToUpperInvariant();
        if (number.Length > MaxOrderNumberLength)
        {
            number = number[..MaxOrderNumberLength];
        }

        var order = new PurchaseOrder(
            PurchaseOrderId.New(),
            number,
            supplierId,
            Clean(supplierCode, MaxSupplierCodeLength)?.ToUpperInvariant() ?? string.Empty,
            warehouseId,
            currency);

        order.Raise(new PurchaseOrderDraftedDomainEvent(order.Id, number, supplierId));

        return order;
    }

    /// <summary>Adds a part to the order.</summary>
    /// <param name="partId">The part to buy.</param>
    /// <param name="sku">Its SKU, snapshotted onto the document.</param>
    /// <param name="description">Its description, snapshotted onto the document.</param>
    /// <param name="quantity">How much to order.</param>
    /// <param name="unitPrice">The agreed price per unit, in the order's currency.</param>
    public Result<PurchaseOrderLineId> AddLine(
        PartRef partId,
        string? sku,
        string? description,
        Quantity quantity,
        Money unitPrice)
    {
        ArgumentNullException.ThrowIfNull(quantity);
        ArgumentNullException.ThrowIfNull(unitPrice);

        if (!IsEditable)
        {
            return PurchasingErrors.Order.NotEditable;
        }

        if (unitPrice.Currency != Currency)
        {
            return PurchasingErrors.Line.CurrencyMismatch;
        }

        if (_lines.Exists(line => line.PartId == partId))
        {
            return PurchasingErrors.Line.DuplicatePart;
        }

        Result<PurchaseOrderLine> line = PurchaseOrderLine.Create(
            partId, sku, description, quantity, unitPrice);

        if (line.IsFailure)
        {
            return Result.Failure<PurchaseOrderLineId>(line.Error);
        }

        _lines.Add(line.Value);

        return line.Value.Id;
    }

    /// <summary>Changes how much of a part is being ordered.</summary>
    /// <param name="lineId">The line to change.</param>
    /// <param name="quantity">The new quantity, in the unit the line was raised in.</param>
    public Result ChangeLineQuantity(PurchaseOrderLineId lineId, Quantity quantity)
    {
        ArgumentNullException.ThrowIfNull(quantity);

        if (!IsEditable)
        {
            return PurchasingErrors.Order.NotEditable;
        }

        PurchaseOrderLine? line = FindLine(lineId);

        return line is null
            ? PurchasingErrors.Line.NotFound(lineId.ToString())
            : line.ChangeQuantity(quantity);
    }

    /// <summary>Changes the agreed price on a line.</summary>
    /// <param name="lineId">The line to change.</param>
    /// <param name="unitPrice">The new price per unit, in the order's currency.</param>
    public Result ChangeLinePrice(PurchaseOrderLineId lineId, Money unitPrice)
    {
        ArgumentNullException.ThrowIfNull(unitPrice);

        if (!IsEditable)
        {
            return PurchasingErrors.Order.NotEditable;
        }

        PurchaseOrderLine? line = FindLine(lineId);

        return line is null
            ? PurchasingErrors.Line.NotFound(lineId.ToString())
            : line.ChangeUnitPrice(unitPrice);
    }

    /// <summary>Takes a part off the order.</summary>
    /// <param name="lineId">The line to remove.</param>
    public Result RemoveLine(PurchaseOrderLineId lineId)
    {
        if (!IsEditable)
        {
            return PurchasingErrors.Order.NotEditable;
        }

        PurchaseOrderLine? line = FindLine(lineId);
        if (line is null)
        {
            return PurchasingErrors.Line.NotFound(lineId.ToString());
        }

        _lines.Remove(line);

        return Result.Success();
    }

    /// <summary>Records a note against the order.</summary>
    /// <param name="notes">Free text, or null to clear.</param>
    public Result SetNotes(string? notes)
    {
        if (IsClosed)
        {
            return PurchasingErrors.Order.AlreadyClosed;
        }

        Notes = Clean(notes, MaxNotesLength);

        return Result.Success();
    }

    /// <summary>
    /// Sends the order to the supplier. This is the point of commitment, which is why it is a
    /// separate step from raising the document: a buyer can build an order over a morning and
    /// still change their mind about every line on it.
    /// </summary>
    /// <param name="today">The current date, supplied so the transition is testable.</param>
    /// <param name="expectedOn">When delivery is expected, if a date is already known.</param>
    public Result Submit(DateOnly today, DateOnly? expectedOn = null)
    {
        if (Status != PurchaseOrderStatus.Draft)
        {
            return Status == PurchaseOrderStatus.Cancelled
                ? PurchasingErrors.Order.AlreadyClosed
                : PurchasingErrors.Order.AlreadySubmitted;
        }

        if (_lines.Count == 0)
        {
            return PurchasingErrors.Order.NoLines;
        }

        if (expectedOn is { } expected && expected < today)
        {
            return PurchasingErrors.Order.ExpectedDateInPast;
        }

        Status = PurchaseOrderStatus.Submitted;
        OrderedOn = today;
        ExpectedOn = expectedOn;

        Money total = Total;

        Raise(new PurchaseOrderSubmittedDomainEvent(
            Id, OrderNumber, SupplierId, DeliverToWarehouseId, total.Amount, total.Currency.Code, expectedOn));

        return Result.Success();
    }

    /// <summary>Records the supplier's acknowledgement and the date they promised.</summary>
    /// <param name="expectedOn">The date they committed to.</param>
    /// <param name="today">The current date, supplied so the transition is testable.</param>
    /// <param name="supplierReference">Their own order number.</param>
    public Result Confirm(DateOnly expectedOn, DateOnly today, string? supplierReference = null)
    {
        if (Status != PurchaseOrderStatus.Submitted)
        {
            return PurchasingErrors.Order.NotAwaitingConfirmation;
        }

        if (expectedOn < today)
        {
            return PurchasingErrors.Order.ExpectedDateInPast;
        }

        Status = PurchaseOrderStatus.Confirmed;
        ExpectedOn = expectedOn;
        SupplierReference = Clean(supplierReference, MaxSupplierReferenceLength);

        Raise(new PurchaseOrderConfirmedDomainEvent(Id, OrderNumber, expectedOn, SupplierReference));

        return Result.Success();
    }

    /// <summary>
    /// Books a delivery in against one line.
    /// <para>
    /// The order does not touch stock itself. It raises
    /// <see cref="GoodsReceivedDomainEvent"/>, which leaves the module as an integration event,
    /// and Inventory decides what that means for the balance. Purchasing knowing how to write a
    /// stock movement would be exactly the coupling the module boundary exists to prevent.
    /// </para>
    /// </summary>
    /// <param name="lineId">The line the goods arrived against.</param>
    /// <param name="received">How much arrived, in the unit the line was ordered in.</param>
    public Result ReceiveLine(PurchaseOrderLineId lineId, Quantity received)
    {
        ArgumentNullException.ThrowIfNull(received);

        if (!CanReceive)
        {
            return IsClosed
                ? PurchasingErrors.Order.AlreadyClosed
                : PurchasingErrors.Order.NotReceivable;
        }

        PurchaseOrderLine? line = FindLine(lineId);
        if (line is null)
        {
            return PurchasingErrors.Line.NotFound(lineId.ToString());
        }

        Result receipt = line.Receive(received);
        if (receipt.IsFailure)
        {
            return receipt;
        }

        Raise(new GoodsReceivedDomainEvent(
            Id,
            OrderNumber,
            line.Id,
            line.PartId,
            DeliverToWarehouseId,
            received.Value,
            received.Unit.Code,
            line.UnitPrice.Amount,
            line.UnitPrice.Currency.Code));

        if (HasOutstandingLines)
        {
            Status = PurchaseOrderStatus.PartiallyReceived;
        }
        else
        {
            Status = PurchaseOrderStatus.Received;
            Raise(new PurchaseOrderCompletedDomainEvent(Id, OrderNumber));
        }

        return Result.Success();
    }

    /// <summary>
    /// Cancels the order. Only possible while nothing has arrived: once goods are on the shelf
    /// there has to be a document behind them, so a part-delivered order is closed short instead.
    /// </summary>
    /// <param name="reason">Why. The supplier will ask.</param>
    public Result Cancel(string? reason)
    {
        if (IsClosed)
        {
            return PurchasingErrors.Order.AlreadyClosed;
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return PurchasingErrors.Order.CancelReasonRequired;
        }

        if (Status == PurchaseOrderStatus.PartiallyReceived)
        {
            return PurchasingErrors.Order.CannotCancelAfterReceipt;
        }

        Status = PurchaseOrderStatus.Cancelled;
        ClosureReason = Clean(reason, MaxNotesLength);

        Raise(new PurchaseOrderCancelledDomainEvent(Id, OrderNumber, ClosureReason!));

        return Result.Success();
    }

    /// <summary>
    /// Accepts a short delivery and stops chasing the balance — the usual end of an order where
    /// the supplier sent 96 of the 100 and both sides have agreed to leave it there.
    /// </summary>
    /// <param name="reason">Why the shortfall was accepted.</param>
    public Result CloseShort(string? reason)
    {
        if (IsClosed)
        {
            return PurchasingErrors.Order.AlreadyClosed;
        }

        if (Status != PurchaseOrderStatus.PartiallyReceived)
        {
            return PurchasingErrors.Order.NotReceivable;
        }

        if (!HasOutstandingLines)
        {
            return PurchasingErrors.Order.NothingOutstanding;
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return PurchasingErrors.Order.CancelReasonRequired;
        }

        Status = PurchaseOrderStatus.ClosedShort;
        ClosureReason = Clean(reason, MaxNotesLength);

        Raise(new PurchaseOrderClosedShortDomainEvent(Id, OrderNumber, ClosureReason!));

        return Result.Success();
    }

    private PurchaseOrderLine? FindLine(PurchaseOrderLineId lineId) =>
        _lines.Find(line => line.Id == lineId);

    private static string? Clean(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string trimmed = value.Trim();

        return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }
}

/// <summary>Where a purchase order is in its life.</summary>
public enum PurchaseOrderStatus
{
    /// <summary>Unspecified. Never persisted.</summary>
    Unknown = 0,

    /// <summary>Being built. Nothing has been committed to anyone.</summary>
    Draft = 1,

    /// <summary>Sent to the supplier, not yet acknowledged.</summary>
    Submitted = 2,

    /// <summary>The supplier has acknowledged it and committed to a date.</summary>
    Confirmed = 3,

    /// <summary>Some of it has arrived; some is still to come.</summary>
    PartiallyReceived = 4,

    /// <summary>Everything ordered has arrived.</summary>
    Received = 5,

    /// <summary>Called off before anything arrived.</summary>
    Cancelled = 6,

    /// <summary>Part-delivered, and the balance written off rather than chased.</summary>
    ClosedShort = 7,
}
