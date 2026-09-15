using System.Globalization;
using AutoPartsErp.Modules.Finance.Domain.Ledger;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Finance.Domain;

/// <summary>Every failure the Finance module can report, in one place.</summary>
public static class FinanceErrors
{
    /// <summary>Failures relating to an <see cref="Receivables.OpenItem"/>.</summary>
    public static class OpenItem
    {
        /// <summary>No such item.</summary>
        public static Error NotFound(string identifier) =>
            Error.NotFound("finance.open_item.not_found", $"No open item matches '{identifier}'.");

        /// <summary>A customer is required.</summary>
        public static readonly Error CustomerRequired =
            Error.Validation(
                "finance.open_item.customer_required",
                "An open item has to belong to a customer.");

        /// <summary>A document is required.</summary>
        public static readonly Error DocumentRequired =
            Error.Validation(
                "finance.open_item.document_required",
                "An open item has to come from a document.");

        /// <summary>A document number is required.</summary>
        public static readonly Error DocumentNumberRequired =
            Error.Validation(
                "finance.open_item.document_number_required",
                "The document's number is required. It is what the customer will quote back.");

        /// <summary>The kind was not said.</summary>
        public static readonly Error KindRequired =
            Error.Validation(
                "finance.open_item.kind_required",
                "Say what kind of document it is: an invoice, a credit note or a debit note.");

        /// <summary>The amount is zero or negative.</summary>
        public static readonly Error AmountNotPositive =
            Error.Validation(
                "finance.open_item.amount_not_positive",
                "An open item is for a positive amount. Which way it points is its kind, not its "
                + "sign.");

        /// <summary>The item has been cancelled.</summary>
        public static Error Cancelled(string documentNumber) =>
            Error.Conflict(
                "finance.open_item.cancelled",
                $"{documentNumber} was cancelled, so nothing can be matched against it.");

        /// <summary>The document was voided after somebody had already paid part of it.</summary>
        public static Error CancelledAfterSettlement(string documentNumber, decimal settled) =>
            Error.Conflict(
                "finance.open_item.cancelled_after_settlement",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{documentNumber} was voided, but {settled:0.00} has already been matched against it. The money and the receipt that recorded it both still exist, so this needs a credit note or a refund rather than the item quietly disappearing."));
    }

    /// <summary>Failures relating to a <see cref="Receipts.Receipt"/>.</summary>
    public static class Receipt
    {
        /// <summary>No such receipt.</summary>
        public static Error NotFound(string identifier) =>
            Error.NotFound("finance.receipt.not_found", $"No receipt matches '{identifier}'.");

        /// <summary>A number is required.</summary>
        public static readonly Error NumberRequired =
            Error.Validation(
                "finance.receipt.number_required", "A receipt number is required.");

        /// <summary>A customer is required.</summary>
        public static readonly Error CustomerRequired =
            Error.Validation(
                "finance.receipt.customer_required", "Say who the money came from.");

        /// <summary>The amount is zero or negative.</summary>
        public static readonly Error AmountNotPositive =
            Error.Validation(
                "finance.receipt.amount_not_positive",
                "A receipt is for a positive amount. Money going the other way is a refund, which "
                + "is a different document.");

        /// <summary>The method was not said.</summary>
        public static readonly Error MethodRequired =
            Error.Validation(
                "finance.receipt.method_required",
                "Say how the money arrived: cash, transfer, cheque, card or direct debit.");
    }

    /// <summary>Failures relating to matching money or credit against documents.</summary>
    public static class Settlement
    {
        /// <summary>Nothing was given to allocate.</summary>
        public static readonly Error NothingToAllocate =
            Error.Validation(
                "finance.settlement.nothing_to_allocate",
                "Say which documents are being settled and by how much.");

        /// <summary>The same document appears more than once.</summary>
        public static readonly Error DuplicateItem =
            Error.Validation(
                "finance.settlement.duplicate_item",
                "The same document appears more than once. Give each one a single amount.");

        /// <summary>An amount is zero or negative.</summary>
        public static readonly Error AmountNotPositive =
            Error.Validation(
                "finance.settlement.amount_not_positive",
                "Every amount in a settlement is positive.");

        /// <summary>The currencies do not agree.</summary>
        public static readonly Error CurrencyMismatch =
            Error.Validation(
                "finance.settlement.currency_mismatch",
                "The money and the document are in different currencies. Converting between them "
                + "is a decision with a rate behind it, and this module does not make it.");

        /// <summary>The document belongs to a different customer.</summary>
        public static Error DifferentCustomer(string documentNumber) =>
            Error.Validation(
                "finance.settlement.different_customer",
                $"{documentNumber} is on another customer's account.");

        /// <summary>The document has nothing left outstanding.</summary>
        public static Error NothingOutstanding(string documentNumber) =>
            Error.Conflict(
                "finance.settlement.nothing_outstanding",
                $"{documentNumber} has nothing left outstanding.");

        /// <summary>More was allocated to a document than it has left.</summary>
        public static Error ExceedsOutstanding(string documentNumber, decimal outstanding) =>
            Error.Validation(
                "finance.settlement.exceeds_outstanding",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{documentNumber} has only {outstanding:0.00} outstanding."));

        /// <summary>More was allocated than the receipt has left unallocated.</summary>
        public static Error ExceedsUnallocated(string receiptNumber, decimal unallocated) =>
            Error.Validation(
                "finance.settlement.exceeds_unallocated",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Receipt {receiptNumber} has only {unallocated:0.00} left to allocate."));

        /// <summary>A receipt was pointed at a credit note.</summary>
        public static Error ReceiptAgainstCredit(string documentNumber) =>
            Error.Validation(
                "finance.settlement.receipt_against_credit",
                $"{documentNumber} is a credit note. A receipt pays what the customer owes; a "
                + "credit note is owed back to them.");

        /// <summary>A credit note was pointed at another credit note.</summary>
        public static Error CreditAgainstCredit(string documentNumber) =>
            Error.Validation(
                "finance.settlement.credit_against_credit",
                $"{documentNumber} is a credit note. A credit note offsets what is owed, not "
                + "another credit.");

        /// <summary>A credit note was pointed at itself.</summary>
        public static Error CreditAgainstItself(string documentNumber) =>
            Error.Validation(
                "finance.settlement.credit_against_itself",
                $"{documentNumber} cannot be allocated against itself.");

        /// <summary>The item offered as a credit is not one.</summary>
        public static Error NotACreditNote(string documentNumber) =>
            Error.Validation(
                "finance.settlement.not_a_credit_note",
                $"{documentNumber} is not a credit note, so it has no credit to allocate.");

        /// <summary>The credit note has already been used up.</summary>
        public static Error CreditExhausted(string documentNumber) =>
            Error.Conflict(
                "finance.settlement.credit_exhausted",
                $"{documentNumber} has already been allocated in full.");
    }

    /// <summary>Failures relating to <see cref="Customers.CustomerTerms"/>.</summary>
    public static class Terms
    {
        /// <summary>No terms are on file for that customer.</summary>
        public static Error NotFound(string identifier) =>
            Error.NotFound(
                "finance.terms.not_found", $"No payment terms are on file for '{identifier}'.");

        /// <summary>A customer is required.</summary>
        public static readonly Error CustomerRequired =
            Error.Validation(
                "finance.terms.customer_required", "Terms have to belong to a customer.");

        /// <summary>A code is required.</summary>
        public static readonly Error CodeRequired =
            Error.Validation("finance.terms.code_required", "A customer code is required.");

        /// <summary>The credit period is negative or absurd.</summary>
        public static readonly Error PaymentDaysOutOfRange =
            Error.Validation(
                "finance.terms.payment_days_out_of_range",
                $"A credit period is between 0 and {Customers.CustomerTerms.MaxPaymentDueInDays} "
                + "days.");
    }

    /// <summary>Failures relating to a <see cref="Payables.PayableItem"/>.</summary>
    public static class Payable
    {
        /// <summary>The item does not exist.</summary>
        public static Error NotFound(string identifier) =>
            Error.NotFound("finance.payable.not_found", $"No payable item matches '{identifier}'.");

        /// <summary>A supplier is required.</summary>
        public static readonly Error SupplierRequired =
            Error.Validation("finance.payable.supplier_required", "A supplier is required.");

        /// <summary>A supplier code is required.</summary>
        public static readonly Error SupplierCodeRequired =
            Error.Validation("finance.payable.supplier_code_required", "A supplier code is required.");

        /// <summary>The document behind it is required.</summary>
        public static readonly Error DocumentRequired =
            Error.Validation(
                "finance.payable.document_required",
                "A payable item has to say which document it came from. A figure on a supplier's " +
                "account with nothing behind it is one nobody can check.");

        /// <summary>Their document number is required.</summary>
        public static readonly Error DocumentNumberRequired =
            Error.Validation(
                "finance.payable.document_number_required",
                "The supplier's own document number is required. It is what a payment references " +
                "and what their statement is reconciled against.");

        /// <summary>Their document number is too long.</summary>
        public static readonly Error DocumentNumberTooLong =
            Error.Validation(
                "finance.payable.document_number_too_long",
                "A supplier document number cannot be longer than 60 characters.");

        /// <summary>Invoice, credit note or debit note.</summary>
        public static readonly Error KindRequired =
            Error.Validation(
                "finance.payable.kind_required",
                "Say what kind of document this is: an invoice, a credit note or a debit note.");

        /// <summary>A document for nothing is not something a purchase ledger should carry.</summary>
        public static readonly Error AmountNotPositive =
            Error.Validation(
                "finance.payable.amount_not_positive",
                "A payable item has to be worth something. Which way it points is its kind, not " +
                "the sign of its amount.");

        /// <summary>Everything on one item is in one currency.</summary>
        public static readonly Error CurrencyMismatch =
            Error.Validation(
                "finance.payable.currency_mismatch",
                "That amount is not in the item's currency. A purchase ledger that quietly " +
                "converts is one that stops reconciling to the bank.");

        /// <summary>Nothing is left outstanding.</summary>
        public static readonly Error AlreadySettled =
            Error.DomainRule("finance.payable.already_settled", "That item is already settled.");

        /// <summary>The item was withdrawn.</summary>
        public static readonly Error Cancelled =
            Error.DomainRule(
                "finance.payable.cancelled",
                "That item was withdrawn, so nothing is owed on it.");

        /// <summary>A settlement has to be worth something.</summary>
        public static readonly Error SettlementNotPositive =
            Error.Validation(
                "finance.payable.settlement_not_positive", "A settlement has to be above zero.");

        /// <summary>More than is outstanding.</summary>
        public static readonly Error SettlementExceedsOutstanding =
            Error.DomainRule(
                "finance.payable.settlement_exceeds_outstanding",
                "That is more than is still owed on the item. Overpaying a supplier is a real " +
                "thing, and it belongs on their account as money on account rather than as a " +
                "document paid twice over.");

        /// <summary>Something has already been paid against it.</summary>
        public static readonly Error PartlyPaid =
            Error.DomainRule(
                "finance.payable.partly_paid",
                "Something has already been paid against that item. Withdrawing it now would " +
                "leave a payment pointing at a row that owes nothing, and the bank would be short " +
                "by exactly that amount with nothing to explain it.");

        /// <summary>A withdrawal needs an explanation.</summary>
        public static readonly Error CancelReasonRequired =
            Error.Validation("finance.payable.cancel_reason_required", "Say why it is being withdrawn.");

        /// <summary>A reason is too long.</summary>
        public static readonly Error ReasonTooLong =
            Error.Validation(
                "finance.payable.reason_too_long", "A reason cannot be longer than 500 characters.");
    }

    /// <summary>Failures relating to a <see cref="Payments.SupplierPayment"/>.</summary>
    public static class Payment
    {
        /// <summary>The payment does not exist.</summary>
        public static Error NotFound(string identifier) =>
            Error.NotFound("finance.payment.not_found", $"No payment matches '{identifier}'.");

        /// <summary>A payment number is required.</summary>
        public static readonly Error NumberRequired =
            Error.Validation("finance.payment.number_required", "A payment number is required.");

        /// <summary>A payment has to be worth something.</summary>
        public static readonly Error AmountNotPositive =
            Error.Validation(
                "finance.payment.amount_not_positive", "A payment has to be above zero.");

        /// <summary>Say how the money left.</summary>
        public static readonly Error MethodRequired =
            Error.Validation(
                "finance.payment.method_required",
                "Say how the money left: cash, transfer, direct debit, cheque or card. A bank " +
                "reconciliation that cannot tell a transfer from a cheque is one nobody can finish.");

        /// <summary>Everything on one payment is in one currency.</summary>
        public static readonly Error CurrencyMismatch =
            Error.Validation(
                "finance.payment.currency_mismatch",
                "That amount is not in the payment's currency.");

        /// <summary>An allocation has to be worth something.</summary>
        public static readonly Error AllocationNotPositive =
            Error.Validation(
                "finance.payment.allocation_not_positive", "An allocation has to be above zero.");

        /// <summary>A remittance with no lines settles nothing.</summary>
        public static readonly Error NothingToAllocate =
            Error.Validation(
                "finance.payment.nothing_to_allocate",
                "Say which documents the payment settled.");

        /// <summary>More than the payment has left unmatched.</summary>
        public static Error ExceedsUnallocated(string number, decimal unallocated) =>
            Error.DomainRule(
                "finance.payment.exceeds_unallocated",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Payment {number} has only {unallocated} left to match. Matching more than " +
                    $"left the bank is how a purchase ledger stops reconciling to it."));

        /// <summary>A document on somebody else's account.</summary>
        public static Error WrongSupplier(string documentNumber) =>
            Error.DomainRule(
                "finance.payment.wrong_supplier",
                $"Document '{documentNumber}' is on another supplier's account.");

        /// <summary>The same document twice on one remittance.</summary>
        public static Error DuplicateDocument(string documentNumber) =>
            Error.Validation(
                "finance.payment.duplicate_document",
                $"Document '{documentNumber}' is on this remittance twice. If it really takes two " +
                "bites of one payment, say so as one line.");

        /// <summary>Nothing is owed on it.</summary>
        public static Error NothingOwed(string documentNumber) =>
            Error.DomainRule(
                "finance.payment.nothing_owed",
                $"Nothing is owed on document '{documentNumber}'.");

        /// <summary>Money paid against a credit note.</summary>
        public static Error PaymentAgainstCredit(string documentNumber) =>
            Error.DomainRule(
                "finance.payment.against_credit",
                $"'{documentNumber}' is a credit note, which the supplier owes back. A credit note " +
                "is matched against an invoice, not paid with cash.");
    }

    /// <summary>Failures relating to an <see cref="Ledger.Account"/>.</summary>
    public static class Account
    {
        /// <summary>The account does not exist.</summary>
        public static Error NotFound(string identifier) =>
            Error.NotFound("finance.account.not_found", $"No account matches '{identifier}'.");

        /// <summary>An account code is required.</summary>
        public static readonly Error CodeRequired =
            Error.Validation("finance.account.code_required", "An account code is required.");

        /// <summary>The code is too long.</summary>
        public static readonly Error CodeTooLong =
            Error.Validation(
                "finance.account.code_too_long",
                "An account code cannot be longer than 20 characters.");

        /// <summary>An account name is required.</summary>
        public static readonly Error NameRequired =
            Error.Validation("finance.account.name_required", "An account name is required.");

        /// <summary>The name is too long.</summary>
        public static readonly Error NameTooLong =
            Error.Validation(
                "finance.account.name_too_long",
                "An account name cannot be longer than 160 characters.");

        /// <summary>Say what kind of thing the account measures.</summary>
        public static readonly Error TypeRequired =
            Error.Validation(
                "finance.account.type_required",
                "Say what the account measures: an asset, a liability, equity, income or an " +
                "expense. It is what decides the side it grows on, and nothing downstream can " +
                "guess it.");

        /// <summary>That code is already taken.</summary>
        public static readonly Error CodeExists =
            Error.Conflict(
                "finance.account.code_exists",
                "An account with that code already exists. Two accounts sharing a code is a trial " +
                "balance with two rows nobody can tell apart.");
    }

    /// <summary>Failures relating to a <see cref="Ledger.JournalEntry"/>.</summary>
    public static class Journal
    {
        /// <summary>The entry does not exist.</summary>
        public static Error NotFound(string identifier) =>
            Error.NotFound("finance.journal.not_found", $"No journal entry matches '{identifier}'.");

        /// <summary>The line is not on this entry.</summary>
        public static Error LineNotFound(string identifier) =>
            Error.NotFound(
                "finance.journal.line_not_found", $"Line '{identifier}' is not on this entry.");

        /// <summary>An entry number is required.</summary>
        public static readonly Error NumberRequired =
            Error.Validation("finance.journal.number_required", "An entry number is required.");

        /// <summary>Say where the entry came from.</summary>
        public static readonly Error SourceRequired =
            Error.Validation(
                "finance.journal.source_required",
                "Say where the entry came from: sales, purchases, cash, inventory, or a person.");

        /// <summary>A description is required.</summary>
        public static readonly Error DescriptionRequired =
            Error.Validation(
                "finance.journal.description_required",
                "Say what the entry is for. A trial balance of forty lines all reading " +
                "'Adjustment' is one nobody can audit.");

        /// <summary>The description is too long.</summary>
        public static readonly Error DescriptionTooLong =
            Error.Validation(
                "finance.journal.description_too_long",
                "A description cannot be longer than 300 characters.");

        /// <summary>Debit or credit.</summary>
        public static readonly Error SideRequired =
            Error.Validation("finance.journal.side_required", "Say which side the line lands on.");

        /// <summary>Everything in one entry is in one currency.</summary>
        public static readonly Error CurrencyMismatch =
            Error.Validation(
                "finance.journal.currency_mismatch",
                "That amount is not in the entry's currency. An entry that balanced across two " +
                "currencies would not balance at all.");

        /// <summary>A line has to be worth something, on one side.</summary>
        public static readonly Error AmountNotPositive =
            Error.Validation(
                "finance.journal.amount_not_positive",
                "A journal line has to be above zero. A credit written as a negative debit is the " +
                "same fact in a form the other half of the ledger cannot see.");

        /// <summary>Nothing posts to a group account.</summary>
        public static Error AccountIsAGroup(string code) =>
            Error.DomainRule(
                "finance.journal.account_is_a_group",
                $"Account '{code}' is a group. A balance that is partly its own postings and " +
                "partly the total of its children is one nobody can take apart again.");

        /// <summary>The account is no longer in use.</summary>
        public static Error AccountInactive(string code) =>
            Error.DomainRule(
                "finance.journal.account_inactive",
                $"Account '{code}' is no longer in use. What is already on it stays where it is; " +
                "nothing new lands there.");

        /// <summary>An entry with no lines posts nothing.</summary>
        public static readonly Error NoLines =
            Error.DomainRule("finance.journal.no_lines", "An entry with no lines posts nothing.");

        /// <summary>All debits and no credits, or the other way round.</summary>
        public static readonly Error OneSidedEntry =
            Error.DomainRule(
                "finance.journal.one_sided",
                "An entry needs both sides. Debits balancing debits is arithmetic, not " +
                "bookkeeping.");

        /// <summary>The two sides do not meet.</summary>
        public static Error OutOfBalance(decimal debits, decimal credits) =>
            Error.DomainRule(
                "finance.journal.out_of_balance",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The entry does not balance: {debits} of debits against {credits} of " +
                    $"credits. An unbalanced ledger is one that no longer proves anything, and " +
                    $"every report built on it inherits the doubt."));

        /// <summary>It has already been posted.</summary>
        public static readonly Error AlreadyPosted =
            Error.DomainRule(
                "finance.journal.already_posted",
                "That entry is posted and does not change again. A correction is a second entry " +
                "that reverses it, so the month somebody has already reported keeps saying what " +
                "it said.");

        /// <summary>It has not been posted.</summary>
        public static readonly Error NotPosted =
            Error.DomainRule(
                "finance.journal.not_posted",
                "That entry has not been posted, so there is nothing to reverse. Delete the draft.");

        /// <summary>A reversal needs an explanation.</summary>
        public static readonly Error ReversalReasonRequired =
            Error.Validation(
                "finance.journal.reversal_reason_required",
                "Say why the entry is being reversed. It is the only thing that will explain the " +
                "pair to whoever reads them next.");

        /// <summary>A reversal dated before what it reverses.</summary>
        public static readonly Error ReversalBeforeOriginal =
            Error.Validation(
                "finance.journal.reversal_before_original",
                "A reversal cannot be dated before the entry it reverses. It would change a " +
                "period that closed before the mistake was made.");
    }

    /// <summary>Failures relating to an <see cref="Ledger.AccountingPeriod"/>.</summary>
    public static class Period
    {
        /// <summary>The period does not exist.</summary>
        public static Error NotFound(string identifier) =>
            Error.NotFound(
                "finance.period.not_found", $"No accounting period matches '{identifier}'.");

        /// <summary>That year is not one this system will keep books for.</summary>
        public static readonly Error YearOutOfRange =
            Error.Validation("finance.period.year_out_of_range", "A year has to be between 2000 and 2999.");

        /// <summary>A month is 1 to 12.</summary>
        public static readonly Error MonthOutOfRange =
            Error.Validation("finance.period.month_out_of_range", "A month has to be between 1 and 12.");

        /// <summary>That period already exists.</summary>
        public static readonly Error AlreadyExists =
            Error.Conflict(
                "finance.period.already_exists",
                "That month already has a period. Two rows for one month would each have their " +
                "own answer to whether it is closed.");

        /// <summary>It is already closed.</summary>
        public static readonly Error AlreadyClosed =
            Error.DomainRule("finance.period.already_closed", "That period is already closed.");

        /// <summary>It is already open.</summary>
        public static readonly Error AlreadyOpen =
            Error.DomainRule("finance.period.already_open", "That period is already open.");

        /// <summary>The month has not ended.</summary>
        public static readonly Error NotOverYet =
            Error.DomainRule(
                "finance.period.not_over",
                "That month is still running. Closing it now would leave the entries belonging to " +
                "its last days with nowhere to go, and somebody would post them into the next " +
                "month to get the work done.");

        /// <summary>An earlier month is still open.</summary>
        public static readonly Error EarlierPeriodOpen =
            Error.DomainRule(
                "finance.period.earlier_open",
                "An earlier month is still open. Closing out of order leaves a month somebody can " +
                "post into after the ones after it have been reported, and the year-to-date " +
                "figures on those reports would change afterwards.");

        /// <summary>A later month is already closed.</summary>
        public static readonly Error LaterPeriodClosed =
            Error.DomainRule(
                "finance.period.later_closed",
                "A later month is already closed, and its year-to-date figures were computed over " +
                "this one. Reopen the later months first, in order.");

        /// <summary>Reopening needs an explanation.</summary>
        public static readonly Error ReopenReasonRequired =
            Error.Validation(
                "finance.period.reopen_reason_required",
                "Say why the period is being reopened. Closing is routine; reopening is somebody " +
                "deciding a reported figure was wrong, and the sentence is what explains it later.");

        /// <summary>A reason is too long.</summary>
        public static readonly Error ReasonTooLong =
            Error.Validation(
                "finance.period.reason_too_long", "A reason cannot be longer than 500 characters.");

        /// <summary>The month an entry falls in has been closed.</summary>
        public static Error Closed(int year, int month) =>
            Error.DomainRule(
                "finance.period.closed",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{year}-{month:D2} is closed. Post the entry into an open month, or reopen " +
                    $"that one and say why — an entry landing in a month already reported would " +
                    $"quietly change what it said."));
    }

    /// <summary>Things that go wrong mapping a fact to account codes.</summary>
    public static class Posting
    {
        /// <summary>No rule is mapped for that fact.</summary>
        public static Error NotFound(string factType) =>
            Error.NotFound(
                "finance.posting.not_found",
                $"Nothing is mapped for '{factType}'. Until it is, that fact reaches no account.");

        /// <summary>This system does not raise a fact by that name.</summary>
        public static Error UnknownFact(string factType) =>
            Error.Validation(
                "finance.posting.unknown_fact",
                $"'{factType}' is not a fact this system raises. Mapping one it never produces " +
                "would look configured and post nothing.");

        /// <summary>That fact does not carry an amount under that name.</summary>
        public static Error UnknownAmount(string factType, string amountKey) =>
            Error.Validation(
                "finance.posting.unknown_amount",
                $"'{factType}' carries no amount called '{amountKey}'. It carries: " +
                $"{string.Join(", ", PostingFacts.AmountKeysFor(factType))}.");

        /// <summary>A fact arrived without an amount its rule names.</summary>
        public static Error AmountMissing(string factType, string amountKey) =>
            Error.DomainRule(
                "finance.posting.amount_missing",
                $"The rule for '{factType}' posts '{amountKey}' and the fact arrived without it. " +
                "Nothing is posted, because a half-posted entry is worse than none.");

        /// <summary>That fact already has a rule.</summary>
        public static readonly Error AlreadyMapped =
            Error.Conflict(
                "finance.posting.already_mapped",
                "That fact already has a rule. Two rules for one fact would each post their own " +
                "version of it, and the ledger would carry it twice.");

        /// <summary>The rule is out of use.</summary>
        public static Error RuleInactive(string factType) =>
            Error.DomainRule(
                "finance.posting.rule_inactive",
                $"The rule for '{factType}' is out of use, so the fact reaches no account.");

        /// <summary>The rule maps nothing.</summary>
        public static Error RuleHasNoLines(string factType) =>
            Error.DomainRule(
                "finance.posting.rule_has_no_lines",
                $"The rule for '{factType}' has no lines. It names a fact and sends it nowhere.");

        /// <summary>Everything the rule would have posted was zero.</summary>
        public static Error NothingToPost(string factType) =>
            Error.DomainRule(
                "finance.posting.nothing_to_post",
                $"Every amount '{factType}' carried was zero, so there is no entry to write.");

        /// <summary>A rule posts one currency.</summary>
        public static readonly Error MixedCurrencies =
            Error.Validation(
                "finance.posting.mixed_currencies",
                "That fact carries amounts in more than one currency. An entry that balanced " +
                "across two would not balance at all.");

        /// <summary>Say what the entries are for.</summary>
        public static readonly Error DescriptionRequired =
            Error.Validation(
                "finance.posting.description_required",
                "Say what the entries this rule writes are for. A trial balance of forty lines " +
                "all reading 'Adjustment' is one nobody can audit.");

        /// <summary>A description is too long.</summary>
        public static readonly Error DescriptionTooLong =
            Error.Validation(
                "finance.posting.description_too_long",
                "A description cannot be longer than 200 characters.");

        /// <summary>A narrative is too long.</summary>
        public static readonly Error NarrativeTooLong =
            Error.Validation(
                "finance.posting.narrative_too_long",
                "A narrative cannot be longer than 200 characters.");

        /// <summary>A line lands somewhere.</summary>
        public static readonly Error AccountCodeRequired =
            Error.Validation(
                "finance.posting.account_code_required", "Say which account the line lands on.");

        /// <summary>The same amount, side and account twice.</summary>
        public static readonly Error DuplicateLine =
            Error.Validation(
                "finance.posting.duplicate_line",
                "That amount already lands on that account on that side. A second identical line " +
                "is a duplication, not a split, and the entry would not balance.");

        /// <summary>A rule this long is a program, not a mapping.</summary>
        public static readonly Error TooManyLines =
            Error.Validation(
                "finance.posting.too_many_lines", "A rule cannot have more than 20 lines.");

        /// <summary>No such line on this rule.</summary>
        public static readonly Error LineNotFound =
            Error.NotFound("finance.posting.line_not_found", "That rule has no such line.");

        /// <summary>A fact is about a document.</summary>
        public static readonly Error ReferenceRequired =
            Error.Validation(
                "finance.posting.reference_required",
                "Say which document the fact is about. It is half the key that stops the same " +
                "sale being posted twice when the outbox redelivers it.");

        /// <summary>A reference is too long.</summary>
        public static readonly Error ReferenceTooLong =
            Error.Validation(
                "finance.posting.reference_too_long",
                "A reference cannot be longer than 60 characters.");

        /// <summary>A fact carries something.</summary>
        public static readonly Error NoAmounts =
            Error.Validation(
                "finance.posting.no_amounts", "A fact with no amounts is not a fact to post.");

        /// <summary>No such fact on the waiting list.</summary>
        public static Error FactNotFound(string identifier) =>
            Error.NotFound(
                "finance.posting.fact_not_found", $"No recorded fact matches '{identifier}'.");

        /// <summary>It is already in the ledger.</summary>
        public static readonly Error AlreadyPosted =
            Error.DomainRule(
                "finance.posting.already_posted",
                "That fact is already in the ledger. Posting it again would carry it twice.");

        /// <summary>It was taken off the list.</summary>
        public static readonly Error WasDismissed =
            Error.DomainRule(
                "finance.posting.was_dismissed",
                "That fact was taken off the waiting list. Put it back before posting it.");

        /// <summary>It is already off the list.</summary>
        public static readonly Error AlreadyDismissed =
            Error.DomainRule(
                "finance.posting.already_dismissed", "That fact is already off the waiting list.");

        /// <summary>It is not off the list.</summary>
        public static readonly Error NotDismissed =
            Error.DomainRule(
                "finance.posting.not_dismissed", "That fact is not off the waiting list.");

        /// <summary>Taking something off the list needs an explanation.</summary>
        public static readonly Error DismissReasonRequired =
            Error.Validation(
                "finance.posting.dismiss_reason_required",
                "Say why the fact does not belong in the ledger. \"Why is there no entry for " +
                "invoice 4471?\" has to have an answer.");

        /// <summary>A reason is too long.</summary>
        public static readonly Error ReasonTooLong =
            Error.Validation(
                "finance.posting.reason_too_long", "A reason cannot be longer than 500 characters.");
    }
}
