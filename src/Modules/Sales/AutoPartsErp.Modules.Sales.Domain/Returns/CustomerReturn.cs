using AutoPartsErp.Modules.Sales.Domain.Returns.Events;
using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Sales.Domain.Returns;

/// <summary>
/// Goods coming back from a customer, and what was decided about them.
/// <para>
/// A document rather than a credit note with a stock movement attached, and the difference
/// matters. A credit note is a financial instrument: it can be issued because the price was
/// wrong, because the invoice went to the wrong company, or because a discount was agreed after
/// the fact — none of which involve a single part moving. Putting stock back on every credit note
/// would invent stock. So the goods coming back are their own fact, and the credit follows from
/// them rather than the other way round.
/// </para>
/// <para>
/// <b>Two steps, and a middle.</b> Raising the return records what the customer says they are
/// sending; receiving it records what actually turned up and is what moves stock. A counter
/// return does both in the same minute and that is fine — but a workshop ringing to say a pump is
/// coming back next Tuesday must not put a pump on the shelf for a salesperson to promise
/// somebody on Monday.
/// </para>
/// <para>
/// <b>Not everything that comes back goes back on the shelf.</b> Each line says what was decided
/// about it: a sealed box returns to stock, a fitted and scratched alternator does not. The
/// customer is credited either way — that is a conversation with them, not a fact about the
/// shelf — so disposition changes what Inventory hears and nothing about what is owed.
/// </para>
/// </summary>
public sealed class CustomerReturn : AggregateRoot<CustomerReturnId>, IAuditable, ITenantScoped
{
    /// <summary>Longest permitted return number.</summary>
    public const int MaxNumberLength = 30;

    /// <summary>Longest permitted reason or note.</summary>
    public const int MaxNotesLength = 500;

    private readonly List<CustomerReturnLine> _lines = [];

    private CustomerReturn(
        CustomerReturnId id,
        string number,
        SalesOrderId salesOrderId,
        string orderNumber,
        CustomerRef customerId,
        string customerCode,
        string customerName,
        WarehouseRef toWarehouseId,
        string currencyCode,
        string reason)
        : base(id)
    {
        Number = number;
        SalesOrderId = salesOrderId;
        OrderNumber = orderNumber;
        CustomerId = customerId;
        CustomerCode = customerCode;
        CustomerName = customerName;
        ToWarehouseId = toWarehouseId;
        CurrencyCode = currencyCode;
        Reason = reason;
        Status = CustomerReturnStatus.Draft;
        CreatedBy = string.Empty;
    }

    /// <summary>Required by EF Core materialization.</summary>
#pragma warning disable CS8618
    private CustomerReturn()
    {
    }
#pragma warning restore CS8618

    /// <summary>The return's number, e.g. "RET-2026-00014".</summary>
    public string Number { get; private set; } = string.Empty;

    /// <summary>The order the goods went out on.</summary>
    public SalesOrderId SalesOrderId { get; private set; }

    /// <summary>Its number, snapshotted so the return reads on its own.</summary>
    public string OrderNumber { get; private set; } = string.Empty;

    /// <summary>Who is sending them back.</summary>
    public CustomerRef CustomerId { get; private set; }

    /// <summary>Their short code, as it was when the return was raised.</summary>
    public string CustomerCode { get; private set; } = string.Empty;

    /// <summary>Their name, as it was when the return was raised.</summary>
    public string CustomerName { get; private set; } = string.Empty;

    /// <summary>
    /// Which warehouse the goods are coming back to.
    /// <para>
    /// Taken from the order rather than chosen, because stock that left one branch and is booked
    /// back into another is a transfer nobody recorded, and it shows up a month later as two
    /// count variances that cancel out across the company and explain nothing at either site.
    /// </para>
    /// </summary>
    public WarehouseRef ToWarehouseId { get; private set; }

    /// <summary>The currency the original lines were priced in.</summary>
    public string CurrencyCode { get; private set; } = string.Empty;

    /// <summary>Where the return stands.</summary>
    public CustomerReturnStatus Status { get; private set; }

    /// <summary>Why the customer is sending them back.</summary>
    public string Reason { get; private set; } = string.Empty;

    /// <summary>The day the goods actually turned up.</summary>
    public DateOnly? ReceivedOn { get; private set; }

    /// <summary>The credit note that gave the money back, once one has been issued.</summary>
    public string? CreditNoteNumber { get; private set; }

    /// <summary>The day it was issued.</summary>
    public DateOnly? CreditedOn { get; private set; }

    /// <summary>Why it was called off.</summary>
    public string? ClosureReason { get; private set; }

    /// <summary>What is coming back.</summary>
    public IReadOnlyCollection<CustomerReturnLine> Lines => _lines.AsReadOnly();

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

    /// <summary>The currency, as a value object.</summary>
    public Currency Currency => Currency.FromCode(CurrencyCode);

    /// <summary>True while lines may still be added or taken off.</summary>
    public bool IsDraft => Status == CustomerReturnStatus.Draft;

    /// <summary>True once the goods are back and nothing further will move.</summary>
    public bool IsClosed => Status
        is CustomerReturnStatus.Received
        or CustomerReturnStatus.Credited
        or CustomerReturnStatus.Cancelled;

    /// <summary>True once the customer has had their money back.</summary>
    public bool IsCredited => Status == CustomerReturnStatus.Credited;

    /// <summary>What the customer is owed for the goods, before VAT.</summary>
    public Money NetTotal => Sum(line => line.NetTotal);

    /// <summary>The VAT to be credited.</summary>
    public Money VatTotal => Sum(line => line.VatAmount);

    /// <summary>What the credit note will come to.</summary>
    public Money GrossTotal => NetTotal + VatTotal;

    /// <summary>Raises an empty return against an order.</summary>
    /// <param name="number">The return number, already taken from the counter.</param>
    /// <param name="salesOrderId">The order the goods went out on.</param>
    /// <param name="orderNumber">Its number.</param>
    /// <param name="customerId">Who is sending them back.</param>
    /// <param name="customerCode">Their code, snapshotted.</param>
    /// <param name="customerName">Their name, snapshotted.</param>
    /// <param name="toWarehouseId">Where the goods are coming back to.</param>
    /// <param name="currencyCode">The currency the order was priced in.</param>
    /// <param name="reason">Why they are coming back. Required.</param>
    public static Result<CustomerReturn> Raise(
        string? number,
        SalesOrderId salesOrderId,
        string? orderNumber,
        CustomerRef customerId,
        string? customerCode,
        string? customerName,
        WarehouseRef toWarehouseId,
        string? currencyCode,
        string? reason)
    {
        if (string.IsNullOrWhiteSpace(number))
        {
            return SalesErrors.Return.NumberRequired;
        }

        if (salesOrderId.IsEmpty)
        {
            return SalesErrors.Return.OrderRequired;
        }

        if (customerId.IsEmpty)
        {
            return SalesErrors.Return.CustomerRequired;
        }

        if (toWarehouseId.IsEmpty)
        {
            return SalesErrors.Return.WarehouseRequired;
        }

        // Required, and it is the field somebody reads first. "Why is this coming back" decides
        // whether it goes on the shelf, whether the supplier hears about it, and whether the
        // customer is charged a restocking fee — and reconstructing it from a stock movement six
        // weeks later is not possible.
        if (string.IsNullOrWhiteSpace(reason))
        {
            return SalesErrors.Return.ReasonRequired;
        }

        if (!Currency.TryFromCode(currencyCode ?? string.Empty, out Currency currency))
        {
            return SalesErrors.Return.CurrencyUnknown;
        }

        string trimmedNumber = number.Trim();

        return trimmedNumber.Length > MaxNumberLength
            ? SalesErrors.Return.NumberTooLong
            : new CustomerReturn(
                CustomerReturnId.New(),
                trimmedNumber,
                salesOrderId,
                Trim(orderNumber, MaxNumberLength),
                customerId,
                Trim(customerCode, 30),
                Trim(customerName, 200),
                toWarehouseId,
                currency.Code,
                Trim(reason, MaxNotesLength));
    }

    /// <summary>
    /// Puts a line of the original order on the return.
    /// <para>
    /// The price comes from the order line rather than from today's price list, and it has to:
    /// the customer is being given back what they paid, which is a fact about a document that has
    /// already been issued and not a question about what the part is worth this morning.
    /// </para>
    /// </summary>
    /// <param name="salesOrderLineId">The line of the original order.</param>
    /// <param name="partId">The part coming back.</param>
    /// <param name="sku">Its SKU, snapshotted.</param>
    /// <param name="description">Its description, snapshotted.</param>
    /// <param name="quantity">How much is coming back.</param>
    /// <param name="unitPrice">What they paid per unit.</param>
    /// <param name="discountPercent">The discount they had.</param>
    /// <param name="vatRatePercent">The VAT rate on the original line.</param>
    /// <param name="disposition">Whether it goes back on the shelf or is written off.</param>
    /// <param name="conditionNote">What state it arrived in.</param>
    public Result<CustomerReturnLineId> AddLine(
        SalesOrderLineId salesOrderLineId,
        PartRef partId,
        string? sku,
        string? description,
        Quantity quantity,
        Money unitPrice,
        decimal discountPercent,
        decimal vatRatePercent,
        ReturnDisposition disposition,
        string? conditionNote = null)
    {
        ArgumentNullException.ThrowIfNull(quantity);
        ArgumentNullException.ThrowIfNull(unitPrice);

        if (!IsDraft)
        {
            return Result.Failure<CustomerReturnLineId>(SalesErrors.Return.NotEditable);
        }

        if (unitPrice.Currency.Code != CurrencyCode)
        {
            return Result.Failure<CustomerReturnLineId>(SalesErrors.Return.CurrencyMismatch);
        }

        // One line of the original order appears at most once on one return. Two rows for the
        // same line would each be checked against the order's dispatched quantity separately and
        // both would pass, which is how a customer gets credited twice for one alternator.
        if (_lines.Exists(line => line.SalesOrderLineId == salesOrderLineId))
        {
            return Result.Failure<CustomerReturnLineId>(
                SalesErrors.Return.LineAlreadyOnReturn(salesOrderLineId.ToString()));
        }

        Result<CustomerReturnLine> line = CustomerReturnLine.Create(
            salesOrderLineId,
            partId,
            sku,
            description,
            quantity,
            unitPrice,
            discountPercent,
            vatRatePercent,
            disposition,
            conditionNote);

        if (line.IsFailure)
        {
            return Result.Failure<CustomerReturnLineId>(line.Error);
        }

        _lines.Add(line.Value);

        return line.Value.Id;
    }

    /// <summary>Takes a line off a draft return.</summary>
    /// <param name="lineId">The line.</param>
    public Result RemoveLine(CustomerReturnLineId lineId)
    {
        if (!IsDraft)
        {
            return SalesErrors.Return.NotEditable;
        }

        CustomerReturnLine? line = _lines.Find(candidate => candidate.Id == lineId);

        if (line is null)
        {
            return SalesErrors.Return.LineNotFound(lineId.ToString());
        }

        _lines.Remove(line);

        return Result.Success();
    }

    /// <summary>
    /// Records that the goods are physically back.
    /// <para>
    /// This is the step that moves stock, and the reason the document has two. Everything before
    /// it is a customer saying what they intend to send; this is somebody at a goods-in desk
    /// saying what is on the counter in front of them.
    /// </para>
    /// <para>
    /// Only the lines going back on the shelf are announced. A scrapped line still credits the
    /// customer and still appears on the document, but there is nothing for Inventory to do about
    /// a part that is in the bin.
    /// </para>
    /// </summary>
    /// <param name="today">The day the goods arrived.</param>
    public Result Receive(DateOnly today)
    {
        if (Status == CustomerReturnStatus.Cancelled)
        {
            return SalesErrors.Return.AlreadyClosed;
        }

        if (Status is CustomerReturnStatus.Received or CustomerReturnStatus.Credited)
        {
            return SalesErrors.Return.AlreadyReceived;
        }

        if (_lines.Count == 0)
        {
            return SalesErrors.Return.NoLines;
        }

        Status = CustomerReturnStatus.Received;
        ReceivedOn = today;

        Raise(new CustomerReturnReceivedDomainEvent(
            Id,
            Number,
            SalesOrderId,
            OrderNumber,
            CustomerId,
            GrossTotal.Amount,
            CurrencyCode,
            today));

        foreach (CustomerReturnLine line in _lines)
        {
            if (line.Disposition != ReturnDisposition.BackToStock)
            {
                continue;
            }

            Raise(new GoodsReturnedDomainEvent(
                Id,
                Number,
                SalesOrderId,
                OrderNumber,
                line.SalesOrderLineId,
                line.PartId,
                ToWarehouseId,
                line.Quantity.Value,
                line.Quantity.Unit.Code));
        }

        return Result.Success();
    }

    /// <summary>
    /// Records that a credit note has been issued for these goods.
    /// <para>
    /// Told by Invoicing, after the fact. The return does not decide when somebody is credited
    /// and cannot refuse it — the document exists, it has been declared to the tax authority, and
    /// a return that argued with it would be arguing with something that already happened.
    /// </para>
    /// <para>
    /// The number is stored rather than the document's identity. A person looking at a return
    /// wants to read "NC SERIE2026/12", and a screen that had to fetch it from another module to
    /// show a returns list would fetch it once per row.
    /// </para>
    /// </summary>
    /// <param name="creditNoteNumber">The credit note's number, as printed.</param>
    /// <param name="creditedOn">The date on it.</param>
    public Result RecordCredited(string? creditNoteNumber, DateOnly creditedOn)
    {
        if (Status != CustomerReturnStatus.Received)
        {
            return SalesErrors.Return.NotReceived;
        }

        Status = CustomerReturnStatus.Credited;
        CreditNoteNumber = Trim(creditNoteNumber, MaxNumberLength);
        CreditedOn = creditedOn;

        return Result.Success();
    }

    /// <summary>
    /// Calls the return off before the goods arrive.
    /// <para>
    /// Only from draft. Once stock is back on the shelf there is nothing to cancel — the way out
    /// of that is another movement, not an undo.
    /// </para>
    /// </summary>
    /// <param name="reason">Why. Required, because somebody will ask.</param>
    public Result Cancel(string? reason)
    {
        if (!IsDraft)
        {
            return Status == CustomerReturnStatus.Cancelled
                ? SalesErrors.Return.AlreadyClosed
                : SalesErrors.Return.AlreadyReceived;
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return SalesErrors.Return.ClosureReasonRequired;
        }

        Status = CustomerReturnStatus.Cancelled;
        ClosureReason = Trim(reason, MaxNotesLength);

        return Result.Success();
    }

    private Money Sum(Func<CustomerReturnLine, Money> selector)
    {
        Money total = Money.Zero(Currency);

        foreach (CustomerReturnLine line in _lines)
        {
            total += selector(line);
        }

        return total;
    }

    private static string Trim(string? value, int maxLength)
    {
        string trimmed = (value ?? string.Empty).Trim();

        return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }
}

/// <summary>Where a customer return has got to.</summary>
public enum CustomerReturnStatus
{
    /// <summary>Unspecified. Never persisted.</summary>
    Unknown = 0,

    /// <summary>Raised, and the goods are not here yet.</summary>
    Draft = 1,

    /// <summary>The goods are back. Stock has moved.</summary>
    Received = 2,

    /// <summary>Called off before anything arrived.</summary>
    Cancelled = 3,

    /// <summary>
    /// The goods are back and a credit note has been issued for them.
    /// <para>
    /// After <c>Cancelled</c> in the numbering rather than beside <c>Received</c>, because the
    /// values are stored as text and the order of the members is not what anything reads.
    /// Renumbering the two before it to make the list look tidy would silently change what every
    /// existing row means the day somebody switches this to an integer column.
    /// </para>
    /// </summary>
    Credited = 4,
}

/// <summary>What was decided about a returned part.</summary>
public enum ReturnDisposition
{
    /// <summary>Unspecified. Never persisted.</summary>
    Unknown = 0,

    /// <summary>Saleable. It goes back on the shelf and rejoins the balance.</summary>
    BackToStock = 1,

    /// <summary>
    /// Not saleable. It is credited to the customer and written off rather than shelved.
    /// <para>
    /// Nothing is booked into stock for these, deliberately: booking it in and immediately
    /// adjusting it out would put two movements in the ledger for one part that was never on a
    /// shelf, and make every stock report count a scrapped alternator for the instant between
    /// them.
    /// </para>
    /// </summary>
    Scrap = 2,
}
