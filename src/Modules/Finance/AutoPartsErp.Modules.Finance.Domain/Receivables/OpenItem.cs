using AutoPartsErp.Modules.Finance.Domain.Receivables.Events;
using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Finance.Domain.Receivables;

/// <summary>What kind of document put the item on the account.</summary>
public enum OpenItemKind
{
    /// <summary>Not set.</summary>
    Unknown = 0,

    /// <summary>An invoice. The customer owes it.</summary>
    Invoice = 1,

    /// <summary>A credit note. The company owes it back.</summary>
    CreditNote = 2,

    /// <summary>A debit note. Charged in addition, and owed like an invoice.</summary>
    DebitNote = 3,
}

/// <summary>Where an item stands.</summary>
public enum OpenItemStatus
{
    /// <summary>Not set.</summary>
    Unknown = 0,

    /// <summary>Nothing has been matched against it.</summary>
    Open = 1,

    /// <summary>Some of it has been matched, some is still outstanding.</summary>
    PartiallySettled = 2,

    /// <summary>Nothing is left outstanding.</summary>
    Settled = 3,

    /// <summary>The document behind it was voided, so it is no longer owed.</summary>
    Cancelled = 4,
}

/// <summary>
/// One line of a customer's account: a document, what it was for, and how much of it is still
/// outstanding.
/// <para>
/// This is the unit the whole module is built on. An invoice raises a debit item, a credit note
/// raises a credit item, and settling is the act of matching one against the other or against
/// money received. Everything a person asks of a sales ledger — what is this customer's balance,
/// which invoices are overdue and by how long, what did this receipt pay — is answered by reading
/// these rows, because the answer is recorded rather than inferred.
/// </para>
/// <para>
/// The alternative was a running balance per customer, and it is the wrong shape for the same
/// reason a bank statement is not one number: the moment somebody disputes an invoice, or asks
/// which one is thirty days late, a balance has nothing to say.
/// </para>
/// <para>
/// Amounts are always positive. Which direction the item points is <see cref="Kind"/>, and the
/// arithmetic that needs a sign asks for one rather than storing negative money — a negative
/// <see cref="Money"/> in the database is a number nobody can read at a glance.
/// </para>
/// </summary>
public sealed class OpenItem : AggregateRoot<OpenItemId>, ITenantScoped, IAuditable
{
    /// <summary>Longest document number kept. Invoicing's own limit is well inside this.</summary>
    public const int MaxDocumentNumberLength = 60;

    /// <summary>Longest cancellation reason kept.</summary>
    public const int MaxReasonLength = 300;

    private OpenItem(
        OpenItemId id,
        CustomerRef customerId,
        DocumentRef documentId,
        string documentNumber,
        OpenItemKind kind,
        Money originalAmount,
        DateOnly documentDate,
        DateOnly dueDate)
        : base(id)
    {
        CustomerId = customerId;
        DocumentId = documentId;
        DocumentNumber = documentNumber;
        Kind = kind;
        OriginalAmount = originalAmount;
        SettledAmount = Money.Zero(originalAmount.Currency);
        DocumentDate = documentDate;
        DueDate = dueDate;
        Status = OpenItemStatus.Open;
    }

    /// <summary>Required by EF Core materialization.</summary>
#pragma warning disable CS8618
    private OpenItem()
    {
    }
#pragma warning restore CS8618

    /// <summary>Whose account it sits on.</summary>
    public CustomerRef CustomerId { get; private set; }

    /// <summary>The document in Invoicing that raised it.</summary>
    public DocumentRef DocumentId { get; private set; }

    /// <summary>Its number, as printed, e.g. <c>FT SERIE2026/35</c>.</summary>
    public string DocumentNumber { get; private set; } = string.Empty;

    /// <summary>Invoice, credit note or debit note.</summary>
    public OpenItemKind Kind { get; private set; }

    /// <summary>What the document was for. Always positive.</summary>
    public Money OriginalAmount { get; private set; } = null!;

    /// <summary>How much of it has been matched. Always positive, never above the original.</summary>
    public Money SettledAmount { get; private set; } = null!;

    /// <summary>The date on the document.</summary>
    public DateOnly DocumentDate { get; private set; }

    /// <summary>When it falls due, from the customer's payment terms.</summary>
    public DateOnly DueDate { get; private set; }

    /// <summary>Where it stands.</summary>
    public OpenItemStatus Status { get; private set; }

    /// <summary>Why it was cancelled, when it was.</summary>
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

    /// <summary>What is still to be matched.</summary>
    public Money Outstanding => OriginalAmount - SettledAmount;

    /// <summary>The currency everything on this item is in.</summary>
    public Currency Currency => OriginalAmount.Currency;

    /// <summary>True when the customer owes it: an invoice or a debit note.</summary>
    public bool IsDebit => Kind is OpenItemKind.Invoice or OpenItemKind.DebitNote;

    /// <summary>True when it is owed back to the customer: a credit note.</summary>
    public bool IsCredit => Kind == OpenItemKind.CreditNote;

    /// <summary>True while anything is still outstanding and the item has not been cancelled.</summary>
    public bool IsOutstanding =>
        Status is OpenItemStatus.Open or OpenItemStatus.PartiallySettled;

    /// <summary>
    /// What this item contributes to the customer's balance: positive when they owe it,
    /// negative when it is owed back.
    /// </summary>
    public Money SignedOutstanding => IsCredit ? Outstanding.Negate() : Outstanding;

    /// <summary>
    /// Raises an item from an issued document.
    /// <para>
    /// Called from the handler for the integration event Invoicing publishes on issue, never by a
    /// person. Nothing here decides anything: a document that exists is owed, and the only
    /// judgement in the whole operation — when it falls due — was made by whoever set the
    /// customer's payment terms.
    /// </para>
    /// </summary>
    /// <param name="customerId">Whose account it goes on.</param>
    /// <param name="documentId">The document in Invoicing.</param>
    /// <param name="documentNumber">Its number, as printed.</param>
    /// <param name="kind">Invoice, credit note or debit note.</param>
    /// <param name="amount">The gross total. Positive whichever way the item points.</param>
    /// <param name="documentDate">The date on the document.</param>
    /// <param name="dueDate">When it falls due.</param>
    public static Result<OpenItem> Raise(
        CustomerRef customerId,
        DocumentRef documentId,
        string? documentNumber,
        OpenItemKind kind,
        Money amount,
        DateOnly documentDate,
        DateOnly dueDate)
    {
        ArgumentNullException.ThrowIfNull(amount);

        if (customerId.IsEmpty)
        {
            return FinanceErrors.OpenItem.CustomerRequired;
        }

        if (documentId.IsEmpty)
        {
            return FinanceErrors.OpenItem.DocumentRequired;
        }

        if (string.IsNullOrWhiteSpace(documentNumber))
        {
            return FinanceErrors.OpenItem.DocumentNumberRequired;
        }

        if (kind == OpenItemKind.Unknown)
        {
            return FinanceErrors.OpenItem.KindRequired;
        }

        // Zero is refused as well as negative. A document for nothing is not a thing the sales
        // ledger should be carrying, and if Invoicing ever issues one that is worth finding out
        // about here rather than discovering it as a row nobody can settle.
        if (!amount.IsPositive)
        {
            return FinanceErrors.OpenItem.AmountNotPositive;
        }

        // Not an error worth refusing over - due before issued is odd but harmless, and refusing
        // it would mean a document Invoicing has already given the customer cannot be recorded.
        DateOnly due = dueDate < documentDate ? documentDate : dueDate;

        var item = new OpenItem(
            OpenItemId.New(),
            customerId,
            documentId,
            documentNumber.Trim(),
            kind,
            amount,
            documentDate,
            due);

        item.Raise(new OpenItemRaisedDomainEvent(
            item.Id, customerId, documentId, item.DocumentNumber, kind, amount, due));

        return item;
    }

    /// <summary>
    /// True when the item is still outstanding and its due date has passed.
    /// </summary>
    /// <param name="on">The date to judge it against, normally today.</param>
    public bool IsOverdueOn(DateOnly on) => IsOutstanding && on > DueDate;

    /// <summary>
    /// How many days past due it is, or zero when it is not.
    /// </summary>
    /// <param name="on">The date to judge it against, normally today.</param>
    public int DaysOverdueOn(DateOnly on) =>
        IsOverdueOn(on) ? on.DayNumber - DueDate.DayNumber : 0;

    /// <summary>
    /// Matches an amount against the item.
    /// <para>
    /// Internal, and deliberately: settling one item is never the whole operation. Money arrives
    /// as a receipt and a credit note is a document, and both have their own record to keep of
    /// what they were matched to. <see cref="Settlement"/> is the only thing that calls this, and
    /// it calls it on both sides of the match in the same breath.
    /// </para>
    /// </summary>
    /// <param name="amount">How much to match. Positive, and no more than is outstanding.</param>
    internal Result Settle(Money amount)
    {
        ArgumentNullException.ThrowIfNull(amount);

        if (Status == OpenItemStatus.Cancelled)
        {
            return FinanceErrors.OpenItem.Cancelled(DocumentNumber);
        }

        if (amount.Currency != Currency)
        {
            return FinanceErrors.Settlement.CurrencyMismatch;
        }

        if (!amount.IsPositive)
        {
            return FinanceErrors.Settlement.AmountNotPositive;
        }

        if (amount > Outstanding)
        {
            return FinanceErrors.Settlement.ExceedsOutstanding(
                DocumentNumber, Outstanding.Amount);
        }

        SettledAmount += amount;

        Status = Outstanding.IsZero
            ? OpenItemStatus.Settled
            : OpenItemStatus.PartiallySettled;

        if (Status == OpenItemStatus.Settled)
        {
            Raise(new OpenItemSettledDomainEvent(Id, CustomerId, DocumentNumber, OriginalAmount));
        }

        return Result.Success();
    }

    /// <summary>
    /// Takes the item off the account because the document behind it was voided.
    /// <para>
    /// Refused once anything has been matched against it. A voided document that somebody has
    /// already paid is a real situation and not one this method can resolve: the money exists,
    /// the receipt that recorded it exists, and quietly cancelling the item would leave that
    /// receipt pointing at nothing. It needs a credit note, or a refund, and either way a person.
    /// </para>
    /// </summary>
    /// <param name="reason">Why it was voided, carried from the document.</param>
    public Result Cancel(string? reason)
    {
        if (Status == OpenItemStatus.Cancelled)
        {
            return Result.Success();
        }

        if (SettledAmount.IsPositive)
        {
            return FinanceErrors.OpenItem.CancelledAfterSettlement(
                DocumentNumber, SettledAmount.Amount);
        }

        Status = OpenItemStatus.Cancelled;
        CancellationReason = Clip(reason, MaxReasonLength);

        Raise(new OpenItemCancelledDomainEvent(
            Id, CustomerId, DocumentNumber, OriginalAmount, CancellationReason));

        return Result.Success();
    }

    private static string? Clip(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string trimmed = value.Trim();

        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
