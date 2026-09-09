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

    /// <summary>
    /// How much of what has gone out has been charged for.
    /// <para>
    /// A status rather than a reference to a document, because there is no longer one document to
    /// point at. An order can be billed across three invoices as the goods leave in three lorries,
    /// and a single <c>InvoiceId</c> column would be right for the first of them and a half-truth
    /// on every screen thereafter. Which documents were drawn is a question for Invoicing, which
    /// is the module that has them; what Sales owns is how much is still to bill.
    /// </para>
    /// <para>
    /// Stored rather than computed from the lines, because it is what the index for "orders
    /// waiting to be invoiced" is filtered on, and a filter cannot be written against a property
    /// that only exists in C#.
    /// </para>
    /// </summary>
    public SalesOrderInvoicingStatus InvoicingStatus { get; private set; }
        = SalesOrderInvoicingStatus.NotInvoiced;

    /// <summary>The date the most recent document was drawn from this order.</summary>
    public DateOnly? LastInvoicedOn { get; private set; }

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

    /// <summary>
    /// True when somebody with the authority to do it confirmed this order past the customer's
    /// credit limit.
    /// <para>
    /// Kept on the order rather than left to a log line, because the question a credit controller
    /// asks is "which orders went out over the limit", and that is a query. Who and when come
    /// from the audit columns and the confirmation date; by how much it exceeded does not come
    /// from anywhere, because the limit is on the account and the account has moved on since.
    /// </para>
    /// </summary>
    public bool CreditLimitOverridden { get; private set; }

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
    /// True once the order is finished with billing: everything sold has gone out and all of it
    /// has been charged for. An order with nothing to bill today but stock still to ship is not
    /// this.
    /// </summary>
    public bool IsInvoiced => InvoicingStatus == SalesOrderInvoicingStatus.Invoiced;

    /// <summary>True while something has gone out that nobody has charged for yet.</summary>
    public bool HasBillableLines => _lines.Exists(line => line.IsBillable);

    /// <summary>
    /// True when a document may be drawn from this order.
    /// <para>
    /// What has gone out may be charged for, and nothing else. Invoicing goods that have not
    /// shipped is a promise, and a promise with a document number on it is a problem — the number
    /// is reported to the tax authority, the VAT falls due, and the only way back is a credit note
    /// for goods that never moved.
    /// </para>
    /// <para>
    /// The order does not have to be finished. Six of ten went out this morning; those six can be
    /// charged for today and the other four when they follow, which is what a distributor
    /// supplying against a standing order actually does. The condition is therefore "something has
    /// gone out and nobody has charged for it", not "the order is closed".
    /// </para>
    /// </summary>
    public bool CanInvoice =>
        Status is SalesOrderStatus.PartiallyDispatched or SalesOrderStatus.Dispatched
        && HasBillableLines;

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
    /// <param name="priceSource">
    /// The code of the price list the price came from, or null when it was typed by hand.
    /// </param>
    public Result<SalesOrderLineId> AddLine(
        PartRef partId,
        string? sku,
        string? description,
        Quantity quantity,
        Money unitPrice,
        decimal discountPercent = 0m,
        decimal vatRatePercent = 0m,
        string? priceSource = null)
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
            partId, sku, description, quantity, unitPrice, discountPercent, vatRatePercent,
            priceSource);

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
    /// <param name="allowBackorder">
    /// True when the order was deliberately taken without the stock being there. Travels on to
    /// Inventory so it holds what it can instead of refusing outright.
    /// </param>
    /// <param name="creditLimitOverridden">
    /// True when the account only carried this order because somebody overrode its limit. The
    /// order records the fact; it does not decide it, and the decision has already been taken by
    /// the time this is called.
    /// </param>
    public Result Confirm(
        DateOnly today,
        DateOnly? requiredBy = null,
        bool allowBackorder = false,
        bool creditLimitOverridden = false)
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
        CreditLimitOverridden = creditLimitOverridden;

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
                line.Quantity.Unit.Code,
                allowBackorder));
        }

        return Result.Success();
    }

    /// <summary>
    /// Records goods coming back against one of this order's lines.
    /// <para>
    /// Called by the return, not by a person. Two aggregates change in the same transaction — the
    /// return and this order — which is a rule usually worth keeping and is broken here for the
    /// same reason it is broken when an order is confirmed: "how much of this line is still with
    /// the customer" has to move at the same instant as the document that says so, or the figure
    /// is a lie for as long as the two are apart. They live in the same module and the same
    /// transaction, and splitting them across an event would buy purity and pay for it with a
    /// number nobody can trust.
    /// </para>
    /// <para>
    /// The order's own status does not move. A dispatched order with a return against it is still
    /// a dispatched order; treating it as outstanding again would put it back on the picking list
    /// and promise the customer goods they have just sent back.
    /// </para>
    /// </summary>
    /// <param name="lineId">The line the goods went out on.</param>
    /// <param name="returned">How much is coming back.</param>
    public Result RecordReturn(SalesOrderLineId lineId, Quantity returned)
    {
        ArgumentNullException.ThrowIfNull(returned);

        SalesOrderLine? line = FindLine(lineId);

        if (line is null)
        {
            return SalesErrors.Line.NotFound(lineId.ToString());
        }

        return line.RecordReturn(returned);
    }

    /// <summary>How much of one line is still with the customer, for a screen building a return.</summary>
    /// <param name="lineId">The line.</param>
    public Quantity? ReturnableOn(SalesOrderLineId lineId) => FindLine(lineId)?.ReturnableQuantity;

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

    /// <summary>
    /// Records that a document has charged for some of what went out.
    /// <para>
    /// Called from an event handler, not from a person: Invoicing issues the document and says so,
    /// and Sales writes it down. Which means it arrives after the fact and cannot refuse on the
    /// grounds that the order was not ready — the document already exists and has a number the tax
    /// authority has been told about. What it can refuse is a quantity larger than what is left to
    /// bill, because that is not a late arrival but two documents charging for the same goods.
    /// </para>
    /// </summary>
    /// <param name="billed">How much of each line the document charged for.</param>
    /// <param name="on">The date on the document.</param>
    public Result RecordBilling(IReadOnlyDictionary<SalesOrderLineId, Quantity> billed, DateOnly on)
    {
        ArgumentNullException.ThrowIfNull(billed);

        if (billed.Count == 0)
        {
            return SalesErrors.Order.NothingBilled;
        }

        // Checked before anything is applied, for the same reason a settlement is: a three-line
        // document that failed on the third would otherwise leave two lines billed in memory,
        // with no transaction to roll back because nothing has been saved yet.
        foreach ((SalesOrderLineId lineId, Quantity quantity) in billed)
        {
            SalesOrderLine? line = _lines.Find(item => item.Id == lineId);

            if (line is null)
            {
                return SalesErrors.Line.NotFound(lineId.ToString());
            }

            if (quantity.Unit != line.Quantity.Unit)
            {
                return SalesErrors.Line.UnitMismatch;
            }

            if (quantity.Value <= 0m)
            {
                return SalesErrors.Line.BilledNotPositive;
            }

            if (quantity > line.BillableQuantity)
            {
                return SalesErrors.Line.OverBilled(line.Sku, line.BillableQuantity.Value);
            }
        }

        foreach ((SalesOrderLineId lineId, Quantity quantity) in billed)
        {
            SalesOrderLine line = _lines.Find(item => item.Id == lineId)!;

            Result applied = line.Bill(quantity);

            if (applied.IsFailure)
            {
                return applied;
            }
        }

        LastInvoicedOn = on;
        UpdateInvoicingStatus();

        return Result.Success();
    }

    /// <summary>
    /// Puts billed quantities back, because the document that charged for them was voided.
    /// <para>
    /// A voided invoice keeps its number and its place in the chain forever, but it no longer
    /// bills anybody — so what it charged for becomes billable again. Without this, voiding a
    /// document raised against the wrong customer would leave those goods permanently unbillable,
    /// and the only remedy would be re-keying the order.
    /// </para>
    /// <para>
    /// A quantity that is no longer there is put back as far as it goes rather than refused. By
    /// the time a void arrives the line may have been re-invoiced and credited in ways this
    /// aggregate cannot reconstruct, and refusing would stop the whole message rather than the
    /// part of it that no longer applies.
    /// </para>
    /// </summary>
    /// <param name="billed">How much of each line the voided document had charged for.</param>
    public Result ReverseBilling(IReadOnlyDictionary<SalesOrderLineId, Quantity> billed)
    {
        ArgumentNullException.ThrowIfNull(billed);

        foreach ((SalesOrderLineId lineId, Quantity quantity) in billed)
        {
            SalesOrderLine? line = _lines.Find(item => item.Id == lineId);

            if (line is null || quantity.Unit != line.Quantity.Unit)
            {
                continue;
            }

            Quantity toReverse = quantity > line.InvoicedQuantity ? line.InvoicedQuantity : quantity;

            if (toReverse.Value <= 0m)
            {
                continue;
            }

            Result reversed = line.Unbill(toReverse);

            if (reversed.IsFailure)
            {
                return reversed;
            }
        }

        UpdateInvoicingStatus();

        return Result.Success();
    }

    /// <summary>
    /// Works out where the order stands on billing, from the lines.
    /// <para>
    /// <c>Invoiced</c> means finished: everything sold has gone out and every bit of it has been
    /// charged for. Nothing left to bill <em>today</em> is not the same thing and must not be
    /// confused with it — an order with four of ten shipped and those four billed has nothing to
    /// bill this morning and six units to bill when the rest arrives.
    /// </para>
    /// <para>
    /// The reason this distinction is load-bearing rather than pedantic: the billing run reads
    /// <c>ix_sales_orders_tenant_awaiting_invoice</c>, which is filtered on
    /// <c>invoicing_status &lt;&gt; 'Invoiced'</c>. This method is the only thing that writes that
    /// column, and it is called only when a document is raised or voided — never on dispatch. An
    /// order marked <c>Invoiced</c> while it still had stock to ship would therefore leave the
    /// worklist and have nothing to bring it back: the remaining six units would be delivered and
    /// never charged for, and the order would look correct in every screen while it happened.
    /// </para>
    /// </summary>
    private void UpdateInvoicingStatus()
    {
        bool anyBilled = _lines.Exists(line => line.InvoicedQuantity.Value > 0m);

        InvoicingStatus = anyBilled
            ? HasBillableLines || HasOutstandingLines
                ? SalesOrderInvoicingStatus.PartiallyInvoiced
                : SalesOrderInvoicingStatus.Invoiced
            : SalesOrderInvoicingStatus.NotInvoiced;
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

/// <summary>How far through billing the order is.</summary>
public enum SalesOrderInvoicingStatus
{
    /// <summary>Unspecified. Never persisted.</summary>
    Unknown = 0,

    /// <summary>Nothing has been charged for yet.</summary>
    NotInvoiced = 1,

    /// <summary>
    /// Something has been charged for and the order is not finished.
    /// <para>
    /// Two different situations wear this status, and deliberately so: there is something that has
    /// gone out and not been billed, or there is nothing to bill today because the rest of the
    /// order has not shipped yet. Both mean the same thing to the billing run — come back to this
    /// order — and the run reads a filtered index that can only say <c>Invoiced</c> or not.
    /// </para>
    /// <para>
    /// Not the same as a partly dispatched order. A line can be fully dispatched and half
    /// invoiced, and a line can be half dispatched and fully invoiced for that half — the two
    /// quantities move independently.
    /// </para>
    /// </summary>
    PartiallyInvoiced = 2,

    /// <summary>
    /// Finished. Everything sold has gone out and all of it has been charged for.
    /// <para>
    /// Terminal in practice, and reachable only from a fully dispatched order. Voiding a document
    /// is the one thing that takes an order back out of it.
    /// </para>
    /// </summary>
    Invoiced = 3,
}
