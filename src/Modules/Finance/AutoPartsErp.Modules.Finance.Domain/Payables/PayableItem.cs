using AutoPartsErp.Modules.Finance.Domain.Payables.Events;
using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Finance.Domain.Payables;

/// <summary>What kind of document put the item on the supplier's account.</summary>
public enum PayableItemKind
{
    /// <summary>Not set.</summary>
    Unknown = 0,

    /// <summary>Their invoice. The company owes it.</summary>
    Invoice = 1,

    /// <summary>Their credit note. They owe it back.</summary>
    CreditNote = 2,

    /// <summary>Charged in addition, and owed like an invoice.</summary>
    DebitNote = 3,
}

/// <summary>Where a payable item stands.</summary>
public enum PayableItemStatus
{
    /// <summary>Not set.</summary>
    Unknown = 0,

    /// <summary>Nothing has been matched against it.</summary>
    Open = 1,

    /// <summary>Some of it has been paid, some is still outstanding.</summary>
    PartiallySettled = 2,

    /// <summary>Nothing is left outstanding.</summary>
    Settled = 3,

    /// <summary>The document behind it was withdrawn, so it is no longer owed.</summary>
    Cancelled = 4,
}

/// <summary>
/// One line of a supplier's account: their document, and how much of it the company still owes.
/// <para>
/// The mirror of <see cref="Receivables.OpenItem"/>, built the same way and for the same reason: a
/// purchase ledger answers "what do we owe this supplier?", "what is due this Friday?" and "what
/// did that transfer pay?" by reading rows that recorded the answer, not by keeping one balance
/// that has nothing to say the moment somebody disputes a document.
/// </para>
/// <para>
/// <b>A separate aggregate rather than a side flag on the receivable.</b> Both shapes are nearly
/// identical and merging them was tempting. The reason not to is a query that forgets the filter:
/// a sales ledger and a purchase ledger sharing a table would, one afternoon, net what a customer
/// owes against what the company owes a supplier and report a balance that is wrong in a way
/// nobody would question. They are also different partners, different screens, different people,
/// and different halves of a general ledger that does not exist yet.
/// </para>
/// <para>
/// Amounts are always positive. Which direction the item points is <see cref="Kind"/>, and the
/// arithmetic that needs a sign asks for one — a negative <see cref="Money"/> in the database is a
/// number nobody can read at a glance.
/// </para>
/// </summary>
public sealed class PayableItem : AggregateRoot<PayableItemId>, ITenantScoped, IAuditable
{
    /// <summary>Longest permitted supplier document number.</summary>
    public const int MaxDocumentNumberLength = 60;

    /// <summary>Longest permitted reason.</summary>
    public const int MaxReasonLength = 500;

    private PayableItem(
        PayableItemId id,
        SupplierRef supplierId,
        string supplierCode,
        string documentNumber,
        PayableItemKind kind,
        Money originalAmount,
        DateOnly documentDate,
        DateOnly dueDate)
        : base(id)
    {
        SupplierId = supplierId;
        SupplierCode = supplierCode;
        DocumentNumber = documentNumber;
        Kind = kind;
        OriginalAmount = originalAmount;
        SettledAmount = Money.Zero(originalAmount.Currency);
        DocumentDate = documentDate;
        DueDate = dueDate;
        Status = PayableItemStatus.Open;
        CreatedBy = string.Empty;
    }

    /// <summary>Required by EF Core materialization.</summary>
#pragma warning disable CS8618
    private PayableItem()
    {
    }
#pragma warning restore CS8618

    /// <summary>Whose account it goes on.</summary>
    public SupplierRef SupplierId { get; private set; }

    /// <summary>Their short code, snapshotted so a screen need not cross the module boundary.</summary>
    public string SupplierCode { get; private set; } = string.Empty;

    /// <summary>
    /// Their document number, as printed.
    /// <para>
    /// Their sequence, not the company's, which is why nothing here is unique on it. Suppliers
    /// restart series after changing software and send duplicate copies of documents already
    /// settled; a unique constraint on somebody else's numbering is one the company cannot fix
    /// when it fires at five o'clock.
    /// </para>
    /// </summary>
    public string DocumentNumber { get; private set; } = string.Empty;

    /// <summary>The document in Purchasing this came from.</summary>
    public SupplierInvoiceRef SupplierInvoiceId { get; private set; }

    /// <summary>Invoice, credit note or debit note.</summary>
    public PayableItemKind Kind { get; private set; }

    /// <summary>The gross total, positive whichever way the item points.</summary>
    public Money OriginalAmount { get; private set; } = null!;

    /// <summary>How much of it has been matched off.</summary>
    public Money SettledAmount { get; private set; } = null!;

    /// <summary>The date on their document.</summary>
    public DateOnly DocumentDate { get; private set; }

    /// <summary>When the company has to pay it.</summary>
    public DateOnly DueDate { get; private set; }

    /// <summary>Where it stands.</summary>
    public PayableItemStatus Status { get; private set; }

    /// <summary>Why it was withdrawn, once it has been.</summary>
    public string? CancellationReason { get; private set; }

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

    /// <summary>What is still owed on it.</summary>
    public Money Outstanding => OriginalAmount - SettledAmount;

    /// <summary>True when the company owes it: an invoice or a debit note.</summary>
    public bool IsDebit => Kind is PayableItemKind.Invoice or PayableItemKind.DebitNote;

    /// <summary>True when the supplier owes it back.</summary>
    public bool IsCredit => Kind == PayableItemKind.CreditNote;

    /// <summary>True while something is still to be matched against it.</summary>
    public bool IsOutstanding =>
        Status is PayableItemStatus.Open or PayableItemStatus.PartiallySettled;

    /// <summary>
    /// What this contributes to the supplier's balance: positive when the company owes it,
    /// negative when it is owed back.
    /// </summary>
    public Money SignedOutstanding => IsCredit ? Outstanding.Negate() : Outstanding;

    /// <summary>
    /// Raises an item from a supplier's settled document.
    /// <para>
    /// Called from the handler for the event Purchasing publishes when an invoice settles, never
    /// by a person. Nothing here decides anything: a document the company has accepted is owed,
    /// and the only judgement in the operation — when it falls due — was made by whoever agreed
    /// the supplier's payment terms.
    /// </para>
    /// </summary>
    /// <param name="supplierId">Whose account it goes on.</param>
    /// <param name="supplierCode">Their short code.</param>
    /// <param name="supplierInvoiceId">The document in Purchasing.</param>
    /// <param name="documentNumber">Their document number, as printed.</param>
    /// <param name="kind">Invoice, credit note or debit note.</param>
    /// <param name="amount">The gross total. Positive whichever way the item points.</param>
    /// <param name="documentDate">The date on their document.</param>
    /// <param name="dueDate">When the company has to pay it.</param>
    public static Result<PayableItem> Raise(
        SupplierRef supplierId,
        string? supplierCode,
        SupplierInvoiceRef supplierInvoiceId,
        string? documentNumber,
        PayableItemKind kind,
        Money amount,
        DateOnly documentDate,
        DateOnly dueDate)
    {
        ArgumentNullException.ThrowIfNull(amount);

        if (supplierId.IsEmpty)
        {
            return FinanceErrors.Payable.SupplierRequired;
        }

        if (string.IsNullOrWhiteSpace(supplierCode))
        {
            return FinanceErrors.Payable.SupplierCodeRequired;
        }

        if (supplierInvoiceId.IsEmpty)
        {
            return FinanceErrors.Payable.DocumentRequired;
        }

        if (string.IsNullOrWhiteSpace(documentNumber))
        {
            return FinanceErrors.Payable.DocumentNumberRequired;
        }

        if (documentNumber.Trim().Length > MaxDocumentNumberLength)
        {
            return FinanceErrors.Payable.DocumentNumberTooLong;
        }

        if (kind == PayableItemKind.Unknown)
        {
            return FinanceErrors.Payable.KindRequired;
        }

        // Zero is refused as well as negative. A document for nothing is not something a purchase
        // ledger should carry, and if Purchasing ever settles one that is worth finding out here
        // rather than as a row nobody can ever pay off.
        if (!amount.IsPositive)
        {
            return FinanceErrors.Payable.AmountNotPositive;
        }

        // Odd but harmless, and refusing it would mean a document the company has already accepted
        // cannot be recorded. A supplier who dates their terms from the delivery rather than the
        // invoice produces this legitimately.
        DateOnly due = dueDate < documentDate ? documentDate : dueDate;

        var item = new PayableItem(
            PayableItemId.New(),
            supplierId,
            supplierCode.Trim().ToUpperInvariant(),
            documentNumber.Trim(),
            kind,
            amount,
            documentDate,
            due);

        item.SupplierInvoiceId = supplierInvoiceId;

        item.Raise(new PayableItemRaisedDomainEvent(
            item.Id,
            supplierId,
            item.SupplierCode,
            item.DocumentNumber,
            kind,
            amount.Amount,
            amount.Currency.Code,
            due));

        return item;
    }

    /// <summary>True when it is still owed and its due date has passed.</summary>
    /// <param name="on">The date to judge it against, normally today.</param>
    public bool IsOverdueOn(DateOnly on) => IsOutstanding && on > DueDate;

    /// <summary>How many days past due it is, or zero when it is not.</summary>
    /// <param name="on">The date to judge it against, normally today.</param>
    public int DaysOverdueOn(DateOnly on) =>
        IsOverdueOn(on) ? on.DayNumber - DueDate.DayNumber : 0;

    /// <summary>
    /// Matches an amount off the item.
    /// <para>
    /// Internal, so nothing outside this module can settle a payable without a payment or a credit
    /// note behind it. An amount that appears against a supplier with nothing explaining it is how
    /// a purchase ledger stops reconciling to the bank.
    /// </para>
    /// </summary>
    /// <param name="amount">What to match off. Positive.</param>
    internal Result Settle(Money amount)
    {
        ArgumentNullException.ThrowIfNull(amount);

        if (!IsOutstanding)
        {
            return Status == PayableItemStatus.Cancelled
                ? FinanceErrors.Payable.Cancelled
                : FinanceErrors.Payable.AlreadySettled;
        }

        if (amount.Currency != OriginalAmount.Currency)
        {
            return FinanceErrors.Payable.CurrencyMismatch;
        }

        if (!amount.IsPositive)
        {
            return FinanceErrors.Payable.SettlementNotPositive;
        }

        if (amount.Amount > Outstanding.Amount)
        {
            return FinanceErrors.Payable.SettlementExceedsOutstanding;
        }

        SettledAmount += amount;

        Status = Outstanding.Amount == 0m
            ? PayableItemStatus.Settled
            : PayableItemStatus.PartiallySettled;

        if (Status == PayableItemStatus.Settled)
        {
            Raise(new PayableItemSettledDomainEvent(
                Id, SupplierId, DocumentNumber, OriginalAmount.Amount, OriginalAmount.Currency.Code));
        }

        return Result.Success();
    }

    /// <summary>
    /// Withdraws the item, because the document behind it was cancelled or credited in full.
    /// </summary>
    /// <param name="reason">Why.</param>
    public Result Cancel(string? reason)
    {
        if (Status == PayableItemStatus.Cancelled)
        {
            return FinanceErrors.Payable.Cancelled;
        }

        // Something has already been paid against it. Unwinding that here would leave a payment
        // pointing at a row that no longer owes anything, and the bank reconciliation would be
        // short by exactly that amount with nothing to explain it.
        if (SettledAmount.IsPositive)
        {
            return FinanceErrors.Payable.PartlyPaid;
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return FinanceErrors.Payable.CancelReasonRequired;
        }

        if (reason.Trim().Length > MaxReasonLength)
        {
            return FinanceErrors.Payable.ReasonTooLong;
        }

        CancellationReason = reason.Trim();
        Status = PayableItemStatus.Cancelled;

        return Result.Success();
    }
}
