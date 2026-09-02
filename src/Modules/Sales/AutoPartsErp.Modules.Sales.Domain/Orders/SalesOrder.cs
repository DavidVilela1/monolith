using AutoPartsErp.Modules.Sales.Domain.Orders.Events;
using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Sales.Domain.Orders;

/// <summary>
/// Something sold to a customer, and the record of what has actually left the building.
/// <para>
/// Counter sales and trade orders are the same aggregate with a different
/// <see cref="SalesOrderKind"/>. They differ in exactly two ways — whether credit is at risk,
/// and whether the goods leave immediately — and modelling them as separate types would mean
/// two of everything downstream for a distinction that is one field wide.
/// </para>
/// <para>
/// The credit decision deliberately does not live here. This aggregate knows what the order is
/// worth; <c>CustomerAccount</c> knows whether that is affordable. Keeping them apart is what
/// stops every order having to load a customer, and what makes "why was this refused?" a
/// question with one place to look.
/// </para>
/// </summary>
public sealed class SalesOrder : AggregateRoot<SalesOrderId>, IAuditable, ISoftDeletable, ITenantScoped
{
    /// <summary>Longest permitted order number.</summary>
    public const int MaxOrderNumberLength = 30;

    /// <summary>Longest permitted customer code snapshot.</summary>
    public const int MaxCustomerCodeLength = 20;

    /// <summary>Longest permitted customer name snapshot.</summary>
    public const int MaxCustomerNameLength = 200;

    /// <summary>Longest permitted customer reference.</summary>
    public const int MaxCustomerReferenceLength = 50;

    /// <summary>Longest permitted free-text note or reason.</summary>
    public const int MaxNotesLength = 500;

    private readonly List<SalesOrderLine> _lines = [];

    private SalesOrder(
        SalesOrderId id,
        string orderNumber,
        SalesOrderKind kind,
        CustomerRef customerId,
        string customerCode,
        string customerName,
        WarehouseRef warehouseId,
        Currency currency)
        : base(id)
    {
        OrderNumber = orderNumber;
        Kind = kind;
        CustomerId = customerId;
        CustomerCode = customerCode;
        CustomerName = customerName;
        FromWarehouseId = warehouseId;
        CurrencyCode = currency.Code;
        Status = SalesOrderStatus.Draft;
        CreatedBy = string.Empty;
    }

    /// <summary>Required by EF Core materialization.</summary>
#pragma warning disable CS8618
    private SalesOrder()
    {
    }
#pragma warning restore CS8618

    /// <summary>The number printed on the document, e.g. "SO-2026-01188".</summary>
    public string OrderNumber { get; private set; } = string.Empty;

    /// <summary>Whether this was taken at the counter or is being delivered.</summary>
    public SalesOrderKind Kind { get; private set; }

    /// <summary>Who it is for.</summary>
    public CustomerRef CustomerId { get; private set; }

    /// <summary>Their short code, as it was when the order was taken.</summary>
    public string CustomerCode { get; private set; } = string.Empty;

    /// <summary>Their name, as it was when the order was taken.</summary>
    public string CustomerName { get; private set; } = string.Empty;

    /// <summary>Where the goods come from.</summary>
    public WarehouseRef FromWarehouseId { get; private set; }

    /// <summary>ISO code of the currency the order is priced in.</summary>
    public string CurrencyCode { get; private set; } = Currency.Default.Code;

    /// <summary>Where the order is in its life.</summary>
    public SalesOrderStatus Status { get; private set; }

    /// <summary>The day it was confirmed.</summary>
    public DateOnly? ConfirmedOn { get; private set; }

    /// <summary>When the customer wants it.</summary>
    public DateOnly? RequiredBy { get; private set; }

    /// <summary>Their own order number, which they will quote when they ring about it.</summary>
    public string? CustomerReference { get; private set; }

    /// <summary>Anything worth recording against the order.</summary>
    public string? Notes { get; private set; }

    /// <summary>Why it was cancelled.</summary>
    public string? ClosureReason { get; private set; }

    /// <summary>The lines on the order.</summary>
    public IReadOnlyCollection<SalesOrderLine> Lines => _lines.AsReadOnly();

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
    public bool IsEditable => Status == SalesOrderStatus.Draft;

    /// <summary>True when goods may go out against it.</summary>
    public bool CanDispatch => Status is SalesOrderStatus.Confirmed
        or SalesOrderStatus.PartiallyDispatched;

    /// <summary>True once nothing further will happen to the order.</summary>
    public bool IsClosed => Status is SalesOrderStatus.Dispatched or SalesOrderStatus.Cancelled;

    /// <summary>True while at least one line still owes the customer something.</summary>
    public bool HasOutstandingLines => _lines.Exists(line => line.IsOutstanding);

    /// <summary>
    /// True when the order puts credit at risk. A counter sale is paid before the goods leave,
    /// so it does not.
    /// </summary>
    public bool ConsumesCredit => Kind == SalesOrderKind.Order;

    /// <summary>The value before VAT.</summary>
    public Money NetTotal => Sum(line => line.NetTotal);

    /// <summary>The VAT on the order.</summary>
    public Money VatTotal => Sum(line => line.VatAmount);

    /// <summary>What the customer will be invoiced.</summary>
    public Money GrossTotal => Sum(line => line.GrossTotal);

    /// <summary>Starts an order. Nothing is promised to anyone until it is confirmed.</summary>
    /// <param name="orderNumber">The number to print on the document.</param>
    /// <param name="kind">Counter sale or delivered order.</param>
    /// <param name="customerId">Who it is for.</param>
    /// <param name="customerCode">Their short code, snapshotted onto the document.</param>
    /// <param name="customerName">Their name, snapshotted onto the document.</param>
    /// <param name="warehouseId">Where the goods come from.</param>
    /// <param name="currency">The currency the order is priced in.</param>
    public static Result<SalesOrder> Draft(
        string? orderNumber,
        SalesOrderKind kind,
        CustomerRef customerId,
        string? customerCode,
        string? customerName,
        WarehouseRef warehouseId,
        Currency currency)
    {
        ArgumentNullException.ThrowIfNull(currency);

        if (string.IsNullOrWhiteSpace(orderNumber))
        {
            return SalesErrors.Order.NumberRequired;
        }

        if (customerId.IsEmpty)
        {
            return SalesErrors.Order.CustomerRequired;
        }

        if (warehouseId.IsEmpty)
        {
            return SalesErrors.Order.WarehouseRequired;
        }

        var order = new SalesOrder(
            SalesOrderId.New(),
            Clip(orderNumber, MaxOrderNumberLength).ToUpperInvariant(),
            kind,
            customerId,
            Clean(customerCode, MaxCustomerCodeLength)?.ToUpperInvariant() ?? string.Empty,
            Clean(customerName, MaxCustomerNameLength) ?? string.Empty,
            warehouseId,
            currency);

        order.Raise(new SalesOrderDraftedDomainEvent(order.Id, order.OrderNumber, customerId));

        return order;
    }

    /// <summary>Adds a part to the order.</summary>
    /// <param name="partId">The part to sell.</param>
    /// <param name="sku">Its SKU, snapshotted onto the document.</param>
    /// <param name="description">Its description, snapshotted onto the document.</param>
    /// <param name="quantity">How much to sell.</param>
    /// <param name="unitPrice">The list price per unit, in the order's currency.</param>
    /// <param name="discountPercent">The discount given, 0 to 100.</param>
    /// <param name="vatRatePercent">The VAT rate, 0 to 100.</param>
    public Result<SalesOrderLineId> AddLine(
        PartRef partId,
        string? sku,
        string? description,
        Quantity quantity,
        Money unitPrice,
        decimal discountPercent = 0m,
        decimal vatRatePercent = 0m)
    {
        ArgumentNullException.ThrowIfNull(quantity);
        ArgumentNullException.ThrowIfNull(unitPrice);

        if (!IsEditable)
        {
            return SalesErrors.Order.NotEditable;
        }

        if (unitPrice.Currency != Currency)
        {
            return SalesErrors.Line.CurrencyMismatch;
        }

        if (_lines.Exists(line => line.PartId == partId))
        {
            return SalesErrors.Line.DuplicatePart;
        }

        Result<SalesOrderLine> line = SalesOrderLine.Create(
            partId, sku, description, quantity, unitPrice, discountPercent, vatRatePercent);

        if (line.IsFailure)
        {
            return Result.Failure<SalesOrderLineId>(line.Error);
        }

        _lines.Add(line.Value);

        return line.Value.Id;
    }

    /// <summary>Changes how much of a part is being sold.</summary>
    /// <param name="lineId">The line to change.</param>
    /// <param name="quantity">The new quantity, in the unit the line was raised in.</param>
    public Result ChangeLineQuantity(SalesOrderLineId lineId, Quantity quantity)
    {
        ArgumentNullException.ThrowIfNull(quantity);

        if (!IsEditable)
        {
            return SalesErrors.Order.NotEditable;
        }

        SalesOrderLine? line = FindLine(lineId);

        return line is null
            ? SalesErrors.Line.NotFound(lineId.ToString())
            : line.ChangeQuantity(quantity);
    }

    /// <summary>Changes the price or discount on a line.</summary>
    /// <param name="lineId">The line to change.</param>
    /// <param name="unitPrice">The new list price per unit.</param>
    /// <param name="discountPercent">The new discount, 0 to 100.</param>
    public Result ChangeLinePricing(SalesOrderLineId lineId, Money unitPrice, decimal discountPercent)
    {
        ArgumentNullException.ThrowIfNull(unitPrice);

        if (!IsEditable)
        {
            return SalesErrors.Order.NotEditable;
        }

        if (unitPrice.Currency != Currency)
        {
            return SalesErrors.Line.CurrencyMismatch;
        }

        SalesOrderLine? line = FindLine(lineId);

        return line is null
            ? SalesErrors.Line.NotFound(lineId.ToString())
            : line.ChangePricing(unitPrice, discountPercent);
    }

    /// <summary>Takes a part off the order.</summary>
    /// <param name="lineId">The line to remove.</param>
    public Result RemoveLine(SalesOrderLineId lineId)
    {
        if (!IsEditable)
        {
            return SalesErrors.Order.NotEditable;
        }

        SalesOrderLine? line = FindLine(lineId);
        if (line is null)
        {
            return SalesErrors.Line.NotFound(lineId.ToString());
        }

        _lines.Remove(line);

        return Result.Success();
    }

    /// <summary>Records the customer's own reference and any notes.</summary>
    /// <param name="customerReference">Their order number.</param>
    /// <param name="notes">Free text, or null to clear.</param>
    public Result SetReferences(string? customerReference, string? notes)
    {
        if (IsClosed)
        {
            return SalesErrors.Order.AlreadyClosed;
        }

        CustomerReference = Clean(customerReference, MaxCustomerReferenceLength);
        Notes = Clean(notes, MaxNotesLength);

        return Result.Success();
    }

    /// <summary>
    /// Confirms the order: the figure is agreed and the stock is claimed.
    /// <para>
    /// Whether the customer can afford it has already been decided by then — the handler asks
    /// their account first, and only gets here if the answer was yes. This method's job is the
    /// transition and the events, and it raises one reservation request per line so Inventory
    /// can hold the stock back.
    /// </para>
    /// </summary>
    /// <param name="today">The current date, supplied so the transition is testable.</param>
    /// <param name="requiredBy">When the customer wants it.</param>
    public Result Confirm(DateOnly today, DateOnly? requiredBy = null)
    {
        if (Status != SalesOrderStatus.Draft)
        {
            return Status == SalesOrderStatus.Cancelled
                ? SalesErrors.Order.AlreadyClosed
                : SalesErrors.Order.AlreadyConfirmed;
        }

        if (_lines.Count == 0)
        {
            return SalesErrors.Order.NoLines;
        }

        if (requiredBy is { } required && required < today)
        {
            return SalesErrors.Order.RequiredDateInPast;
        }

        Status = SalesOrderStatus.Confirmed;
        ConfirmedOn = today;
        RequiredBy = requiredBy;

        Raise(new SalesOrderConfirmedDomainEvent(
            Id,
            OrderNumber,
            CustomerId,
            FromWarehouseId,
            NetTotal.Amount,
            VatTotal.Amount,
            GrossTotal.Amount,
            CurrencyCode,
            requiredBy));

        foreach (SalesOrderLine line in _lines)
        {
            Raise(new StockReservationRequestedDomainEvent(
                Id,
                OrderNumber,
                line.Id,
                line.PartId,
                FromWarehouseId,
                line.Quantity.Value,
                line.Quantity.Unit.Code));
        }

        return Result.Success();
    }

    /// <summary>
    /// Records goods leaving against one line.
    /// <para>
    /// Sales does not move stock itself. It raises <see cref="GoodsDispatchedDomainEvent"/>,
    /// which leaves the module as an integration event, and Inventory decides what that means
    /// for the balance and the ledger.
    /// </para>
    /// </summary>
    /// <param name="lineId">The line the goods went out against.</param>
    /// <param name="dispatched">How much went, in the unit the line was sold in.</param>
    public Result DispatchLine(SalesOrderLineId lineId, Quantity dispatched)
    {
        ArgumentNullException.ThrowIfNull(dispatched);

        if (!CanDispatch)
        {
            return IsClosed ? SalesErrors.Order.AlreadyClosed : SalesErrors.Order.NotDispatchable;
        }

        SalesOrderLine? line = FindLine(lineId);
        if (line is null)
        {
            return SalesErrors.Line.NotFound(lineId.ToString());
        }

        Result result = line.Dispatch(dispatched);
        if (result.IsFailure)
        {
            return result;
        }

        Raise(new GoodsDispatchedDomainEvent(
            Id,
            OrderNumber,
            line.Id,
            line.PartId,
            FromWarehouseId,
            dispatched.Value,
            dispatched.Unit.Code));

        if (HasOutstandingLines)
        {
            Status = SalesOrderStatus.PartiallyDispatched;
        }
        else
        {
            Status = SalesOrderStatus.Dispatched;

            Money gross = GrossTotal;

            Raise(new SalesOrderCompletedDomainEvent(
                Id, OrderNumber, CustomerId, gross.Amount, gross.Currency.Code));
        }

        return Result.Success();
    }

    /// <summary>
    /// Calls the order off. Only possible while nothing has gone out: once goods have left, the
    /// correction is a credit note, not a document that claims the sale never happened.
    /// </summary>
    /// <param name="reason">Why.</param>
    public Result Cancel(string? reason)
    {
        if (IsClosed)
        {
            return SalesErrors.Order.AlreadyClosed;
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return SalesErrors.Order.CancelReasonRequired;
        }

        if (Status == SalesOrderStatus.PartiallyDispatched)
        {
            return SalesErrors.Order.CannotCancelAfterDispatch;
        }

        Status = SalesOrderStatus.Cancelled;
        ClosureReason = Clean(reason, MaxNotesLength);

        Raise(new SalesOrderCancelledDomainEvent(Id, OrderNumber, CustomerId, ClosureReason!));

        return Result.Success();
    }

    private Money Sum(Func<SalesOrderLine, Money> selector)
    {
        Money total = Money.Zero(Currency);

        foreach (SalesOrderLine line in _lines)
        {
            total += selector(line);
        }

        return total;
    }

    private SalesOrderLine? FindLine(SalesOrderLineId lineId) =>
        _lines.Find(line => line.Id == lineId);

    private static string Clip(string value, int maxLength)
    {
        string trimmed = value.Trim();

        return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }

    private static string? Clean(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : Clip(value, maxLength);
}

/// <summary>Whether a sale was taken at the counter or is being delivered.</summary>
public enum SalesOrderKind
{
    /// <summary>Unspecified. Never persisted.</summary>
    Unknown = 0,

    /// <summary>Paid and taken on the spot. No credit at risk.</summary>
    CounterSale = 1,

    /// <summary>Delivered later, on account.</summary>
    Order = 2,
}

/// <summary>Where a sales order is in its life.</summary>
public enum SalesOrderStatus
{
    /// <summary>Unspecified. Never persisted.</summary>
    Unknown = 0,

    /// <summary>Being built. Nothing is promised to anyone.</summary>
    Draft = 1,

    /// <summary>Agreed with the customer and claiming stock.</summary>
    Confirmed = 2,

    /// <summary>Some of it has gone out; some is still owed.</summary>
    PartiallyDispatched = 3,

    /// <summary>Everything on it has gone out.</summary>
    Dispatched = 4,

    /// <summary>Called off before anything went out.</summary>
    Cancelled = 5,
}
