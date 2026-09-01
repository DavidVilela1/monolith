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
}
