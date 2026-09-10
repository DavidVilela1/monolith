using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Purchasing.Domain;

/// <summary>Every failure the Purchasing module can report, in one place.</summary>
public static class PurchasingErrors
{
    /// <summary>Failures relating to a <see cref="Orders.PurchaseOrder"/>.</summary>
    public static class Order
    {
        /// <summary>The order does not exist.</summary>
        public static Error NotFound(string identifier) =>
            Error.NotFound("purchasing.order.not_found", $"No purchase order matches '{identifier}'.");

        /// <summary>An order number is required.</summary>
        public static readonly Error NumberRequired =
            Error.Validation("purchasing.order.number_required", "An order number is required.");

        /// <summary>A supplier is required.</summary>
        public static readonly Error SupplierRequired =
            Error.Validation("purchasing.order.supplier_required", "A supplier is required.");

        /// <summary>A delivery warehouse is required.</summary>
        public static readonly Error WarehouseRequired =
            Error.Validation(
                "purchasing.order.warehouse_required",
                "Say which warehouse the goods are being delivered to.");

        /// <summary>No such partner exists.</summary>
        public static Error SupplierNotFound(string identifier) =>
            Error.NotFound(
                "purchasing.order.supplier_not_found",
                $"No partner matches '{identifier}'.");

        /// <summary>The supplier is not set up to be bought from.</summary>
        public static readonly Error SupplierNotPurchasable =
            Error.DomainRule(
                "purchasing.order.supplier_not_purchasable",
                "That partner is not set up as a supplier, or is on hold. Grant the supplier role first.");

        /// <summary>Only a draft may be changed.</summary>
        public static readonly Error NotEditable =
            Error.DomainRule(
                "purchasing.order.not_editable",
                "Only a draft order can be changed. Once it has gone to the supplier, amend it by " +
                "cancelling and re-raising, so both sides are looking at the same document.");

        /// <summary>An empty order cannot be sent.</summary>
        public static readonly Error NoLines =
            Error.DomainRule("purchasing.order.no_lines", "An order with no lines cannot be sent.");

        /// <summary>The order has already gone out.</summary>
        public static readonly Error AlreadySubmitted =
            Error.DomainRule("purchasing.order.already_submitted", "That order has already been sent.");

        /// <summary>Only a submitted order can be confirmed.</summary>
        public static readonly Error NotAwaitingConfirmation =
            Error.DomainRule(
                "purchasing.order.not_awaiting_confirmation",
                "Only an order that has been sent and not yet acknowledged can be confirmed.");

        /// <summary>A promised date in the past is not a promise.</summary>
        public static readonly Error ExpectedDateInPast =
            Error.Validation(
                "purchasing.order.expected_date_past",
                "The expected delivery date cannot be in the past.");

        /// <summary>Goods cannot be booked in against this order.</summary>
        public static readonly Error NotReceivable =
            Error.DomainRule(
                "purchasing.order.not_receivable",
                "Goods can only be received against an order that has been sent and is not yet complete.");

        /// <summary>The order is finished or cancelled.</summary>
        public static readonly Error AlreadyClosed =
            Error.DomainRule("purchasing.order.already_closed", "That order is already closed.");

        /// <summary>Something has already arrived.</summary>
        public static readonly Error CannotCancelAfterReceipt =
            Error.DomainRule(
                "purchasing.order.cannot_cancel_after_receipt",
                "Part of this order has already arrived. Close it short instead, so the goods " +
                "that did turn up still have a document behind them.");

        /// <summary>A cancellation needs an explanation.</summary>
        public static readonly Error CancelReasonRequired =
            Error.Validation(
                "purchasing.order.cancel_reason_required",
                "Say why the order is being cancelled. The supplier will ask.");

        /// <summary>Nothing to close short.</summary>
        public static readonly Error NothingOutstanding =
            Error.DomainRule(
                "purchasing.order.nothing_outstanding",
                "Every line on that order has been received in full; there is nothing to close short.");
    }

    /// <summary>Failures relating to a <see cref="Orders.PurchaseOrderLine"/>.</summary>
    public static class Line
    {
        /// <summary>The line is not on this order.</summary>
        public static Error NotFound(string identifier) =>
            Error.NotFound("purchasing.line.not_found", $"Line '{identifier}' is not on this order.");

        /// <summary>A part is required.</summary>
        public static readonly Error PartRequired =
            Error.Validation("purchasing.line.part_required", "A part is required.");

        /// <summary>The catalogue has never heard of that part.</summary>
        public static Error PartNotInCatalogue(string identifier) =>
            Error.NotFound(
                "purchasing.line.part_not_in_catalogue",
                $"No part in the catalogue matches '{identifier}'.");

        /// <summary>
        /// The part exists but is not something to be buying.
        /// <para>
        /// Stricter than the sales rule, deliberately: a discontinued part may still be sold down
        /// off the shelf, but ordering more of it is how dead stock is bought on purpose.
        /// </para>
        /// </summary>
        public static Error PartNotPurchasable(string sku, Guid? supersededBy) =>
            Error.DomainRule(
                "purchasing.line.part_not_purchasable",
                supersededBy is null
                    ? $"{sku} is not available to order. It may be a draft, or withdrawn from " +
                      "purchasing and being sold down."
                    : $"{sku} has been withdrawn from purchasing. The catalogue replaces it with " +
                      $"part {supersededBy.Value}.");

        /// <summary>Ordering nothing is not ordering.</summary>
        public static readonly Error QuantityNotPositive =
            Error.Validation("purchasing.line.quantity_not_positive", "An order quantity must be above zero.");

        /// <summary>A negative price is not a price.</summary>
        public static readonly Error PriceNegative =
            Error.Validation("purchasing.line.price_negative", "A unit price cannot be negative.");

        /// <summary>The line currency does not match the order.</summary>
        public static readonly Error CurrencyMismatch =
            Error.Validation(
                "purchasing.line.currency_mismatch",
                "A line must be priced in the order's currency.");

        /// <summary>The unit does not match the line being received.</summary>
        public static readonly Error UnitMismatch =
            Error.Validation(
                "purchasing.line.unit_mismatch",
                "The received quantity must be in the same unit the line was ordered in.");

        /// <summary>That part is already on the order.</summary>
        public static readonly Error DuplicatePart =
            Error.Conflict(
                "purchasing.line.duplicate_part",
                "That part is already on this order. Change the quantity of the existing line instead.");

        /// <summary>Receiving nothing is not receiving.</summary>
        public static readonly Error ReceiptNotPositive =
            Error.Validation("purchasing.line.receipt_not_positive", "A received quantity must be above zero.");

        /// <summary>More arrived than was ordered.</summary>
        public static Error OverReceipt(decimal outstanding) =>
            Error.DomainRule(
                "purchasing.line.over_receipt",
                $"Only {outstanding} is still outstanding on that line. Raise a second order for the " +
                "surplus rather than booking in more than was agreed.");

        /// <summary>That line is complete.</summary>
        public static readonly Error AlreadyFullyReceived =
            Error.DomainRule(
                "purchasing.line.already_received",
                "That line has already been received in full.");

        /// <summary>The last line cannot be removed from an order that is about to be sent.</summary>
        public static readonly Error QuantityBelowReceived =
            Error.DomainRule(
                "purchasing.line.quantity_below_received",
                "The order quantity cannot be reduced below what has already arrived.");
    }

    /// <summary>Failures relating to a <see cref="Replenishment.ReplenishmentSuggestion"/>.</summary>
    public static class Suggestion
    {
        /// <summary>The suggestion does not exist.</summary>
        public static Error NotFound(string identifier) =>
            Error.NotFound("purchasing.suggestion.not_found", $"No replenishment suggestion matches '{identifier}'.");

        /// <summary>A part is required.</summary>
        public static readonly Error PartRequired =
            Error.Validation("purchasing.suggestion.part_required", "A part is required.");

        /// <summary>A warehouse is required.</summary>
        public static readonly Error WarehouseRequired =
            Error.Validation("purchasing.suggestion.warehouse_required", "A warehouse is required.");

        /// <summary>Suggesting an order for nothing is not a suggestion.</summary>
        public static readonly Error QuantityNotPositive =
            Error.Validation(
                "purchasing.suggestion.quantity_not_positive",
                "A suggested order quantity must be above zero.");

        /// <summary>A dismissal needs an explanation.</summary>
        public static readonly Error DismissReasonRequired =
            Error.Validation(
                "purchasing.suggestion.dismiss_reason_required",
                "Say why the suggestion is being dismissed, so the next person does not raise it again.");

        /// <summary>The suggestion has already been dealt with.</summary>
        public static readonly Error NotOpen =
            Error.DomainRule(
                "purchasing.suggestion.not_open",
                "That suggestion has already been ordered or dismissed.");
    }

    /// <summary>Failures relating to what was agreed with a supplier.</summary>
    public static class Agreement
    {
        /// <summary>The agreement does not exist.</summary>
        public static Error NotFound(string identifier) =>
            Error.NotFound(
                "purchasing.agreement.not_found",
                $"No supplier agreement matches '{identifier}'.");

        /// <summary>A supplier is required.</summary>
        public static readonly Error SupplierRequired =
            Error.Validation("purchasing.agreement.supplier_required", "A supplier is required.");

        /// <summary>A supplier code is required.</summary>
        public static readonly Error SupplierCodeRequired =
            Error.Validation(
                "purchasing.agreement.supplier_code_required", "A supplier code is required.");

        /// <summary>The supplier code is too long.</summary>
        public static readonly Error SupplierCodeTooLong =
            Error.Validation(
                "purchasing.agreement.supplier_code_too_long",
                "A supplier code cannot be longer than 30 characters.");

        /// <summary>That supplier already has a live agreement.</summary>
        public static readonly Error AlreadyAgreed =
            Error.Conflict(
                "purchasing.agreement.already_agreed",
                "There is already a live agreement with that supplier. End it before opening " +
                "another, so a delivery is never priced by whichever of two rows came back first.");

        /// <summary>The rebate scale is in a different currency from the agreement.</summary>
        public static readonly Error RappelCurrencyMismatch =
            Error.Validation(
                "purchasing.agreement.rappel_currency_mismatch",
                "The rebate thresholds are not in the agreement's currency. A threshold whose " +
                "currency is assumed is a contract that changes meaning without anybody editing it.");

        /// <summary>A period rebate has to say what period.</summary>
        public static readonly Error RappelPeriodRequired =
            Error.Validation(
                "purchasing.agreement.rappel_period_required",
                "A rebate settled by credit note has to say what it is measured over: a month, a " +
                "quarter or a year.");

        /// <summary>The end date is before the start.</summary>
        public static readonly Error PeriodInverted =
            Error.Validation(
                "purchasing.agreement.period_inverted",
                "An agreement cannot end before it starts.");

        /// <summary>The note is too long.</summary>
        public static readonly Error NoteTooLong =
            Error.Validation(
                "purchasing.agreement.note_too_long", "A note cannot be longer than 500 characters.");

        /// <summary>An agreed price has to be worth something.</summary>
        public static readonly Error PriceNotPositive =
            Error.Validation(
                "purchasing.agreement.price_not_positive",
                "An agreed price has to be above zero. A line at nothing is almost always a price " +
                "nobody filled in, and a shelf costed at zero reports every sale off it as pure " +
                "margin.");

        /// <summary>A correction cannot change the currency.</summary>
        public static readonly Error PriceCurrencyMismatch =
            Error.Validation(
                "purchasing.agreement.price_currency_mismatch",
                "A correction cannot change the currency of a price. A price in another currency " +
                "is another price.");

        /// <summary>That supplier already has a price for that part from that day.</summary>
        public static readonly Error PriceAlreadyAgreed =
            Error.Conflict(
                "purchasing.agreement.price_already_agreed",
                "That supplier already has a price for that part from that day. Correct the one " +
                "that is there, or record the change from a different day.");
    }

    /// <summary>Failures relating to a <see cref="Agreements.RappelScale"/>.</summary>
    public static class Rappel
    {
        /// <summary>A scale with no steps pays nothing and says nothing.</summary>
        public static readonly Error NoSteps =
            Error.Validation(
                "purchasing.rappel.no_steps",
                "A rebate scale needs at least one step. A flat percentage is one step starting " +
                "at nothing.");

        /// <summary>Too many steps.</summary>
        public static readonly Error TooManySteps =
            Error.Validation(
                "purchasing.rappel.too_many_steps",
                "A rebate scale cannot carry more than 12 steps. Past that it is a formula, not a " +
                "negotiated contract.");

        /// <summary>The thresholds are not all in one currency.</summary>
        public static readonly Error MixedCurrencies =
            Error.Validation(
                "purchasing.rappel.mixed_currencies",
                "Every threshold in a rebate scale has to be in the same currency.");

        /// <summary>A threshold cannot be negative.</summary>
        public static readonly Error ThresholdNegative =
            Error.Validation(
                "purchasing.rappel.threshold_negative", "A rebate threshold cannot be negative.");

        /// <summary>A rebate rate has to be a real percentage.</summary>
        public static readonly Error RateOutOfRange =
            Error.Validation(
                "purchasing.rappel.rate_out_of_range",
                "A rebate rate has to be above zero and below 100. A hundred per cent back means " +
                "the goods were free.");

        /// <summary>Two steps at the same threshold.</summary>
        public static readonly Error DuplicateThreshold =
            Error.Validation(
                "purchasing.rappel.duplicate_threshold",
                "Two steps cannot start at the same value. At that figure the rebate would be " +
                "both rates, and nothing in the contract says which.");

        /// <summary>A higher threshold paying less.</summary>
        public static readonly Error StepsNotIncreasing =
            Error.DomainRule(
                "purchasing.rappel.steps_not_increasing",
                "Each step of a rebate scale has to pay more than the one below it. A scale that " +
                "pays less for buying more is percentages typed into the wrong rows, and it stops " +
                "a buyer ordering at exactly the wrong moment.");
    }

    /// <summary>Failures relating to a <see cref="Invoices.SupplierInvoice"/>.</summary>
    public static class Invoice
    {
        /// <summary>The document does not exist.</summary>
        public static Error NotFound(string identifier) =>
            Error.NotFound(
                "purchasing.supplier_invoice.not_found",
                $"No supplier invoice matches '{identifier}'.");

        /// <summary>The line is not on this document.</summary>
        public static Error LineNotFound(string identifier) =>
            Error.NotFound(
                "purchasing.supplier_invoice.line_not_found",
                $"Line '{identifier}' is not on this supplier invoice.");

        /// <summary>Only a draft takes receipts or corrections.</summary>
        public static readonly Error NotOpen =
            Error.DomainRule(
                "purchasing.supplier_invoice.not_open",
                "Only a draft supplier invoice can be changed. Once it is settled the company owes " +
                "the money, and the way to undo that is a credit note rather than an edit.");

        /// <summary>A document with no lines has nothing to agree or disagree with.</summary>
        public static readonly Error NoLines =
            Error.DomainRule(
                "purchasing.supplier_invoice.no_lines",
                "There is nothing on this document to check their figure against. Nothing has been " +
                "received against it.");

        /// <summary>Their document number is required.</summary>
        public static readonly Error DocumentNumberRequired =
            Error.Validation(
                "purchasing.supplier_invoice.document_number_required",
                "The supplier's own document number is required. It is what a payment references " +
                "and what their statement will be reconciled against.");

        /// <summary>Their document number is too long.</summary>
        public static readonly Error DocumentNumberTooLong =
            Error.Validation(
                "purchasing.supplier_invoice.document_number_too_long",
                "A supplier document number cannot be longer than 60 characters.");

        /// <summary>A document cannot be dated before the goods arrived.</summary>
        public static readonly Error DocumentBeforeDelivery =
            Error.Validation(
                "purchasing.supplier_invoice.document_before_delivery",
                "The supplier's document is dated before the goods were counted. Either the date " +
                "was typed wrong or this document belongs to a different delivery.");

        /// <summary>Everything on one document is in one currency.</summary>
        public static readonly Error CurrencyMismatch =
            Error.Validation(
                "purchasing.supplier_invoice.currency_mismatch",
                "That figure is not in the document's currency. A purchase document that quietly " +
                "converts is where exchange-rate losses go to hide.");

        /// <summary>A VAT rate has to be a real percentage.</summary>
        public static readonly Error VatRateOutOfRange =
            Error.Validation(
                "purchasing.supplier_invoice.vat_rate_out_of_range",
                "A VAT rate has to be between 0 and 100 per cent.");

        /// <summary>Nothing is in dispute on this document.</summary>
        public static readonly Error NotDisputed =
            Error.DomainRule(
                "purchasing.supplier_invoice.not_disputed",
                "That document is not in dispute. There is nothing to accept or send back.");

        /// <summary>Accepting a difference needs a sentence somebody wrote.</summary>
        public static readonly Error AcceptReasonRequired =
            Error.Validation(
                "purchasing.supplier_invoice.accept_reason_required",
                "Say why the supplier's figure is being accepted. In a year's time it is the only " +
                "thing that will explain paying more than was counted at prices that were agreed.");

        /// <summary>Sending a document back needs to say what is being corrected.</summary>
        public static readonly Error ReopenReasonRequired =
            Error.Validation(
                "purchasing.supplier_invoice.reopen_reason_required",
                "Say what is being corrected before sending the document back to draft.");

        /// <summary>A reason is too long.</summary>
        public static readonly Error ReasonTooLong =
            Error.Validation(
                "purchasing.supplier_invoice.reason_too_long",
                "A reason cannot be longer than 500 characters.");

        /// <summary>A cancellation needs an explanation.</summary>
        public static readonly Error CancelReasonRequired =
            Error.Validation(
                "purchasing.supplier_invoice.cancel_reason_required",
                "Say why the draft is being withdrawn.");

        /// <summary>Money is already owed against it.</summary>
        public static readonly Error AlreadySettled =
            Error.DomainRule(
                "purchasing.supplier_invoice.already_settled",
                "That document is settled and the company owes the money. It goes away by being " +
                "credited, not by being cancelled.");

        /// <summary>It has already been withdrawn.</summary>
        public static readonly Error AlreadyCancelled =
            Error.DomainRule(
                "purchasing.supplier_invoice.already_cancelled",
                "That draft has already been withdrawn.");
    }
}
