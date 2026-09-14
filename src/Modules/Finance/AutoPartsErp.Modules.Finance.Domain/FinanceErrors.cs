using System.Globalization;
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
}
