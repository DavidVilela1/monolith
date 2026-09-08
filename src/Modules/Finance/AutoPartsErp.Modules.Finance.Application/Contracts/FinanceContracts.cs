namespace AutoPartsErp.Modules.Finance.Application.Contracts;

/// <summary>One line of a customer's account.</summary>
/// <param name="OpenItemId">The item.</param>
/// <param name="DocumentId">The document in Invoicing, for anybody who wants to open it.</param>
/// <param name="DocumentNumber">Its number, as printed.</param>
/// <param name="Kind">Invoice, credit note or debit note.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="DocumentDate">The date on the document.</param>
/// <param name="DueDate">When it falls due.</param>
/// <param name="OriginalAmount">What it was for. Always positive.</param>
/// <param name="SettledAmount">How much of it has been matched.</param>
/// <param name="Outstanding">What is left. Always positive.</param>
/// <param name="SignedOutstanding">
/// What it contributes to the balance: positive when owed, negative for a credit note. This is
/// the column a statement adds up, and the one to trust when the two disagree.
/// </param>
/// <param name="CurrencyCode">The currency of all four amounts.</param>
/// <param name="DaysOverdue">How many days past due, or zero when it is not.</param>
public sealed record OpenItemDto(
    Guid OpenItemId,
    Guid DocumentId,
    string DocumentNumber,
    string Kind,
    string Status,
    DateOnly DocumentDate,
    DateOnly DueDate,
    decimal OriginalAmount,
    decimal SettledAmount,
    decimal Outstanding,
    decimal SignedOutstanding,
    string CurrencyCode,
    int DaysOverdue);

/// <summary>
/// What a customer owes, and the documents that make it up.
/// </summary>
/// <param name="CustomerId">The customer.</param>
/// <param name="Code">Their code.</param>
/// <param name="LegalName">Their legal name.</param>
/// <param name="CurrencyCode">The currency they trade in.</param>
/// <param name="AsAt">The date the statement was drawn, which decides what counts as overdue.</param>
/// <param name="Balance">The total owed: invoices less credit notes, all still outstanding.</param>
/// <param name="OverdueBalance">How much of that balance is past its due date.</param>
/// <param name="UnallocatedReceipts">
/// Money received and not yet matched to a document. It is already off the balance, and it is
/// listed separately because it is work somebody still has to do.
/// </param>
/// <param name="Items">The open documents, oldest first.</param>
public sealed record CustomerStatement(
    Guid CustomerId,
    string Code,
    string LegalName,
    string CurrencyCode,
    DateOnly AsAt,
    decimal Balance,
    decimal OverdueBalance,
    decimal UnallocatedReceipts,
    IReadOnlyList<OpenItemDto> Items);

/// <summary>
/// One customer's row in an ageing report.
/// <para>
/// The buckets are the ones every credit controller in the country reads without being told what
/// they mean: not yet due, and then thirty-day steps out to ninety-plus.
/// </para>
/// </summary>
/// <param name="CustomerId">The customer.</param>
/// <param name="Code">Their code.</param>
/// <param name="LegalName">Their legal name.</param>
/// <param name="CurrencyCode">The currency of every amount on the row.</param>
/// <param name="NotYetDue">Outstanding and not yet due.</param>
/// <param name="OneToThirty">One to thirty days past due.</param>
/// <param name="ThirtyOneToSixty">Thirty-one to sixty days past due.</param>
/// <param name="SixtyOneToNinety">Sixty-one to ninety days past due.</param>
/// <param name="OverNinety">More than ninety days past due.</param>
/// <param name="Total">The whole balance, which is the five buckets added up.</param>
/// <param name="OldestDueDate">The due date of the oldest thing still outstanding.</param>
public sealed record AgingRow(
    Guid CustomerId,
    string Code,
    string LegalName,
    string CurrencyCode,
    decimal NotYetDue,
    decimal OneToThirty,
    decimal ThirtyOneToSixty,
    decimal SixtyOneToNinety,
    decimal OverNinety,
    decimal Total,
    DateOnly? OldestDueDate);

/// <summary>A receipt, as a list of them is rendered.</summary>
/// <param name="ReceiptId">The receipt.</param>
/// <param name="Number">Its number.</param>
/// <param name="CustomerId">Who paid.</param>
/// <param name="CustomerName">Their legal name, as Finance knows it.</param>
/// <param name="ReceivedOn">The date the money arrived.</param>
/// <param name="Method">How it arrived.</param>
/// <param name="Reference">The bank reference or cheque number.</param>
/// <param name="Amount">How much arrived.</param>
/// <param name="AllocatedAmount">How much of it has been matched.</param>
/// <param name="Unallocated">How much is still sitting on the account.</param>
/// <param name="CurrencyCode">The currency of all three.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="Allocations">What it paid.</param>
public sealed record ReceiptDto(
    Guid ReceiptId,
    string Number,
    Guid CustomerId,
    string CustomerName,
    DateOnly ReceivedOn,
    string Method,
    string? Reference,
    decimal Amount,
    decimal AllocatedAmount,
    decimal Unallocated,
    string CurrencyCode,
    string Status,
    IReadOnlyList<ReceiptAllocationDto> Allocations);

/// <summary>One document a receipt paid.</summary>
/// <param name="OpenItemId">The item paid.</param>
/// <param name="DocumentNumber">Its number, as copied at the moment of the match.</param>
/// <param name="Amount">How much of the receipt went against it.</param>
/// <param name="AllocatedOn">The date of the match.</param>
public sealed record ReceiptAllocationDto(
    Guid OpenItemId,
    string DocumentNumber,
    decimal Amount,
    DateOnly AllocatedOn);

/// <summary>What to look for when listing receipts.</summary>
/// <param name="CustomerId">Only this customer's, when given.</param>
/// <param name="From">Received on or after this date, when given.</param>
/// <param name="To">Received on or before this date, when given.</param>
/// <param name="OnlyUnallocated">Only those with money still to match.</param>
public sealed record ReceiptSearchCriteria(
    Guid? CustomerId = null,
    DateOnly? From = null,
    DateOnly? To = null,
    bool OnlyUnallocated = false);
