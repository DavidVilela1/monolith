using AutoPartsErp.Modules.Finance.Domain.Customers;
using AutoPartsErp.Modules.Finance.Domain.Ledger;
using AutoPartsErp.Modules.Finance.Domain.Payables;
using AutoPartsErp.Modules.Finance.Domain.Payments;
using AutoPartsErp.Modules.Finance.Domain.Receipts;
using AutoPartsErp.Modules.Finance.Domain.Receivables;
using AutoPartsErp.SharedKernel.Abstractions;

namespace AutoPartsErp.Modules.Finance.Domain;

/// <summary>Write-side access to the items on customers' accounts.</summary>
public interface IOpenItemRepository : IRepository<OpenItem, OpenItemId>
{
    /// <summary>
    /// Loads the item raised from a document, or null when there is none.
    /// <para>
    /// Used by the event handlers, which know a document and nothing else. It is also what makes
    /// them idempotent: the outbox delivers at least once, and an item that already exists for
    /// this document means the message has been seen before.
    /// </para>
    /// </summary>
    /// <param name="documentId">The document in Invoicing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<OpenItem?> GetByDocumentAsync(
        DocumentRef documentId,
        CancellationToken cancellationToken = default);

    /// <summary>Loads several items by identity, for settling them together.</summary>
    /// <param name="ids">The items wanted.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<OpenItem>> GetManyAsync(
        IReadOnlyCollection<OpenItemId> ids,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Everything still outstanding on one customer's account, oldest document first.
    /// </summary>
    /// <param name="customerId">The customer.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<OpenItem>> GetOutstandingForCustomerAsync(
        CustomerRef customerId,
        CancellationToken cancellationToken = default);
}

/// <summary>Write-side access to receipts. Allocations are owned, so they load with their receipt.</summary>
public interface IReceiptRepository : IRepository<Receipt, ReceiptId>
{
    /// <summary>Loads a receipt by its number, or null when there is none.</summary>
    /// <param name="number">The receipt number.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Receipt?> GetByNumberAsync(
        string number,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes the next receipt number for the given year, e.g. <c>RC-2026-00042</c>.
    /// <para>
    /// From the module's counter table, not from the highest number already used - the same
    /// mechanism Sales and Purchasing number orders with, and for the same reason.
    /// </para>
    /// </summary>
    /// <param name="year">The year to number within.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<string> NextReceiptNumberAsync(int year, CancellationToken cancellationToken = default);
}

/// <summary>Write-side access to customers' payment terms.</summary>
public interface ICustomerTermsRepository : IRepository<CustomerTerms, CustomerRef>
{
}

/// <summary>The Finance module's unit of work.</summary>
public interface IFinanceUnitOfWork : IUnitOfWork;

/// <summary>Write-side access to the purchase ledger.</summary>
public interface IPayableItemRepository : IRepository<PayableItem, PayableItemId>
{
    /// <summary>
    /// The item raised from a supplier's document, or null when there is none.
    /// <para>
    /// What makes the handler idempotent: the outbox delivers at least once, and an item that
    /// already exists for this document means the message has been seen before. Paying a supplier
    /// twice because a message was redelivered is the failure this one line prevents.
    /// </para>
    /// </summary>
    Task<PayableItem?> GetByDocumentAsync(
        SupplierInvoiceRef supplierInvoiceId,
        CancellationToken cancellationToken = default);

    /// <summary>Everything still owed to one supplier, oldest due date first.</summary>
    Task<IReadOnlyList<PayableItem>> GetOutstandingForAsync(
        SupplierRef supplierId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Everything due on or before a day, oldest first: the payment run.
    /// </summary>
    Task<IReadOnlyList<PayableItem>> GetDueByAsync(
        DateOnly on,
        CancellationToken cancellationToken = default);
}

/// <summary>Write-side access to money paid to suppliers.</summary>
public interface ISupplierPaymentRepository : IRepository<SupplierPayment, SupplierPaymentId>
{
    /// <summary>Loads a payment by our number for it.</summary>
    Task<SupplierPayment?> GetByNumberAsync(
        string number,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes the next payment number for the year.
    /// <para>
    /// From the database counter, in one statement, like every other run of numbers in this
    /// system. Reading the highest and adding one is unique only while two people never press the
    /// button at the same moment, and two payments sharing a number is a remittance nobody can
    /// reconcile.
    /// </para>
    /// </summary>
    Task<string> NextPaymentNumberAsync(int year, CancellationToken cancellationToken = default);

    /// <summary>
    /// Payments to a supplier with money still matched to nothing, oldest first.
    /// <para>
    /// The screen that explains why a statement disagrees: money left the bank and is sitting on
    /// their account against no document.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<SupplierPayment>> GetUnallocatedForAsync(
        SupplierRef supplierId,
        CancellationToken cancellationToken = default);
}

/// <summary>Write-side access to the chart of accounts.</summary>
public interface IAccountRepository : IRepository<Account, AccountId>
{
    /// <summary>Loads an account by the code the accountant refers to it by.</summary>
    Task<Account?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>True when an account already uses that code.</summary>
    Task<bool> CodeExistsAsync(
        string code,
        AccountId? excluding = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads several accounts by code at once.
    /// <para>
    /// What building an entry needs: a posting of four lines asks about four accounts, and asking
    /// one at a time is four round trips to answer one question.
    /// </para>
    /// </summary>
    Task<IReadOnlyDictionary<string, Account>> GetByCodesAsync(
        IReadOnlyCollection<string> codes,
        CancellationToken cancellationToken = default);

    /// <summary>The whole chart, by code.</summary>
    Task<IReadOnlyList<Account>> GetAllAsync(CancellationToken cancellationToken = default);
}

/// <summary>Write-side access to the general ledger.</summary>
public interface IJournalEntryRepository : IRepository<JournalEntry, JournalEntryId>
{
    /// <summary>Loads an entry by our number for it.</summary>
    Task<JournalEntry?> GetByNumberAsync(
        string number,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes the next entry number for the year.
    /// <para>
    /// From the database counter, like every other run of numbers here. Two entries sharing a
    /// number is a ledger where "which one was that?" has two answers.
    /// </para>
    /// </summary>
    Task<string> NextEntryNumberAsync(int year, CancellationToken cancellationToken = default);

    /// <summary>Posted entries falling inside a stretch of days, oldest first.</summary>
    Task<IReadOnlyList<JournalEntry>> GetPostedBetweenAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default);
}

/// <summary>Write-side access to the accounting calendar.</summary>
public interface IAccountingPeriodRepository : IRepository<AccountingPeriod, AccountingPeriodId>
{
    /// <summary>The period covering a day, or null when none has been opened for it.</summary>
    Task<AccountingPeriod?> GetForAsync(DateOnly on, CancellationToken cancellationToken = default);

    /// <summary>The period for a year and month, or null.</summary>
    Task<AccountingPeriod?> GetForAsync(
        int year,
        int month,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// True when every month before this one is closed, or there is none before it.
    /// <para>
    /// The question the aggregate cannot answer for itself: "every earlier period" is a statement
    /// about other rows.
    /// </para>
    /// </summary>
    Task<bool> EveryEarlierIsClosedAsync(
        int year,
        int month,
        CancellationToken cancellationToken = default);

    /// <summary>True when any month after this one is already closed.</summary>
    Task<bool> AnyLaterIsClosedAsync(
        int year,
        int month,
        CancellationToken cancellationToken = default);
}

/// <summary>Write-side access to the mapping from facts to account codes.</summary>
public interface IPostingRuleRepository : IRepository<PostingRule, PostingRuleId>
{
    /// <summary>
    /// The rule mapping a fact, or null when nothing maps it.
    /// <para>
    /// Returns the rule whether or not it is active, so a caller can tell "nobody has mapped this"
    /// from "somebody mapped it and turned it off". Those are different sentences on a screen.
    /// </para>
    /// </summary>
    Task<PostingRule?> GetForFactAsync(
        string factType,
        CancellationToken cancellationToken = default);

    /// <summary>True when a fact already has a rule.</summary>
    Task<bool> IsMappedAsync(string factType, CancellationToken cancellationToken = default);

    /// <summary>Every rule, for showing somebody what is wired and what is not.</summary>
    Task<IReadOnlyList<PostingRule>> GetAllAsync(CancellationToken cancellationToken = default);
}

/// <summary>Write-side access to the record of every fact handed to the ledger.</summary>
public interface IFactPostingRepository : IRepository<FactPosting, FactPostingId>
{
    /// <summary>
    /// The record of one fact about one document, or null when it has never arrived.
    /// <para>
    /// The natural key, and what makes every posting handler idempotent: a row already here means
    /// the outbox has delivered this message before.
    /// </para>
    /// </summary>
    Task<FactPosting?> GetForAsync(
        string factType,
        string reference,
        CancellationToken cancellationToken = default);

    /// <summary>Everything still waiting to be posted, oldest first.</summary>
    Task<IReadOnlyList<FactPosting>> GetWaitingAsync(
        CancellationToken cancellationToken = default);

    /// <summary>How many facts are waiting, for a badge on a screen.</summary>
    Task<int> CountWaitingAsync(CancellationToken cancellationToken = default);
}
