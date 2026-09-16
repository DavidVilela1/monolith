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

/// <summary>
/// One account on a trial balance.
/// </summary>
/// <param name="AccountId">The account.</param>
/// <param name="Code">The code the accountant refers to it by.</param>
/// <param name="Name">What it is called.</param>
/// <param name="Type">Asset, Liability, Equity, Income or Expense.</param>
/// <param name="Debits">Everything that landed on the debit side, in the window.</param>
/// <param name="Credits">Everything that landed on the credit side, in the window.</param>
/// <param name="Balance">
/// The difference, signed the way the account grows: positive when an asset has been debited more
/// than credited, and positive when a liability has been credited more than debited. The column
/// somebody reads down, rather than doing the subtraction in their head twelve times.
/// </param>
/// <param name="CurrencyCode">The currency.</param>
public sealed record TrialBalanceRow(
    Guid AccountId,
    string Code,
    string Name,
    string Type,
    decimal Debits,
    decimal Credits,
    decimal Balance,
    string CurrencyCode);

/// <summary>
/// A trial balance: every account that moved, and whether the whole thing still balances.
/// </summary>
/// <param name="From">The first day counted, or null when it is everything up to <paramref name="To"/>.</param>
/// <param name="To">The last day counted.</param>
/// <param name="Rows">The accounts, in code order.</param>
/// <param name="TotalDebits">Every debit on it.</param>
/// <param name="TotalCredits">Every credit on it.</param>
/// <param name="IsBalanced">
/// Whether the two agree. It always should — nothing unbalanced can be posted — so a false here
/// means something reached the database without going through the ledger, and that is worth
/// printing at the top of the report rather than leaving somebody to notice.
/// </param>
/// <param name="CurrencyCode">The currency.</param>
public sealed record TrialBalance(
    DateOnly? From,
    DateOnly To,
    IReadOnlyList<TrialBalanceRow> Rows,
    decimal TotalDebits,
    decimal TotalCredits,
    bool IsBalanced,
    string CurrencyCode);

/// <summary>One line on an account's statement.</summary>
/// <param name="JournalEntryId">The entry it belongs to.</param>
/// <param name="EntryNumber">Our number for that entry.</param>
/// <param name="EntryDate">The day it belongs to.</param>
/// <param name="Source">Which journal it came from.</param>
/// <param name="Description">What the entry was for.</param>
/// <param name="Narrative">What this line in particular was for.</param>
/// <param name="Reference">The document behind it.</param>
/// <param name="Debit">The amount, when it landed on the debit side.</param>
/// <param name="Credit">The amount, when it landed on the credit side.</param>
/// <param name="RunningBalance">
/// The account's balance after this line, signed the way the account grows. What makes a statement
/// readable: the question is almost never "what was this line?" but "when did it get to that?".
/// </param>
public sealed record AccountStatementLine(
    Guid JournalEntryId,
    string EntryNumber,
    DateOnly EntryDate,
    string Source,
    string Description,
    string? Narrative,
    string? Reference,
    decimal Debit,
    decimal Credit,
    decimal RunningBalance);

/// <summary>
/// Everything that landed on one account in a stretch of days, with what it started at.
/// </summary>
/// <param name="AccountId">The account.</param>
/// <param name="Code">Its code.</param>
/// <param name="Name">Its name.</param>
/// <param name="Type">What it measures.</param>
/// <param name="From">The first day shown.</param>
/// <param name="To">The last day shown.</param>
/// <param name="OpeningBalance">
/// What it stood at the day before <paramref name="From"/>. Computed from every earlier posting
/// rather than stored, because nothing here carries opening balances forward yet — and a figure
/// summed from the postings is one that cannot disagree with them.
/// </param>
/// <param name="ClosingBalance">What it stood at on <paramref name="To"/>.</param>
/// <param name="Lines">The lines, oldest first.</param>
/// <param name="CurrencyCode">The currency.</param>
public sealed record AccountStatement(
    Guid AccountId,
    string Code,
    string Name,
    string Type,
    DateOnly From,
    DateOnly To,
    decimal OpeningBalance,
    decimal ClosingBalance,
    IReadOnlyList<AccountStatementLine> Lines,
    string CurrencyCode);

/// <summary>One entry in the journal listing.</summary>
/// <param name="JournalEntryId">The entry.</param>
/// <param name="Number">Our number for it.</param>
/// <param name="EntryDate">The day it belongs to.</param>
/// <param name="Source">Where it came from.</param>
/// <param name="Description">What it is for.</param>
/// <param name="Reference">The document behind it.</param>
/// <param name="Total">The total of one side; the two are equal.</param>
/// <param name="LineCount">How many lines it has.</param>
/// <param name="CurrencyCode">The currency.</param>
/// <param name="PostedAtUtc">When it was posted.</param>
public sealed record JournalEntryRow(
    Guid JournalEntryId,
    string Number,
    DateOnly EntryDate,
    string Source,
    string Description,
    string? Reference,
    decimal Total,
    int LineCount,
    string CurrencyCode,
    DateTimeOffset? PostedAtUtc);
