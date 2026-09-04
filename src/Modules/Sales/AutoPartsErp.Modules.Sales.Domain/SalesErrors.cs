using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Sales.Domain;

/// <summary>Every failure the Sales module can report, in one place.</summary>
public static class SalesErrors
{
    /// <summary>Failures relating to a <see cref="Orders.SalesOrder"/>.</summary>
    public static class Order
    {
        /// <summary>The order does not exist.</summary>
        public static Error NotFound(string identifier) =>
            Error.NotFound("sales.order.not_found", $"No sales order matches '{identifier}'.");

        /// <summary>An order number is required.</summary>
        public static readonly Error NumberRequired =
            Error.Validation("sales.order.number_required", "An order number is required.");

        /// <summary>A customer is required.</summary>
        public static readonly Error CustomerRequired =
            Error.Validation("sales.order.customer_required", "A customer is required.");

        /// <summary>A warehouse is required.</summary>
        public static readonly Error WarehouseRequired =
            Error.Validation(
                "sales.order.warehouse_required",
                "Say which warehouse the goods are coming out of.");

        /// <summary>Only a draft may be changed.</summary>
        public static readonly Error NotEditable =
            Error.DomainRule(
                "sales.order.not_editable",
                "Only a draft order can be changed. Once it is confirmed the stock is spoken for " +
                "and the customer has been quoted a figure.");

        /// <summary>An empty order cannot be confirmed.</summary>
        public static readonly Error NoLines =
            Error.DomainRule("sales.order.no_lines", "An order with no lines cannot be confirmed.");

        /// <summary>The order has already been confirmed.</summary>
        public static readonly Error AlreadyConfirmed =
            Error.DomainRule("sales.order.already_confirmed", "That order has already been confirmed.");

        /// <summary>Goods cannot go out against this order.</summary>
        public static readonly Error NotDispatchable =
            Error.DomainRule(
                "sales.order.not_dispatchable",
                "Goods can only go out against a confirmed order that is not already complete.");

        /// <summary>The order is finished or cancelled.</summary>
        public static readonly Error AlreadyClosed =
            Error.DomainRule("sales.order.already_closed", "That order is already closed.");

        /// <summary>Something has already left the building.</summary>
        public static readonly Error CannotCancelAfterDispatch =
            Error.DomainRule(
                "sales.order.cannot_cancel_after_dispatch",
                "Part of this order has already gone out. Raise a credit note for what came back " +
                "instead of pretending the order never happened.");

        /// <summary>A cancellation needs an explanation.</summary>
        public static readonly Error CancelReasonRequired =
            Error.Validation(
                "sales.order.cancel_reason_required",
                "Say why the order is being cancelled.");

        /// <summary>The required-by date is in the past.</summary>
        public static readonly Error RequiredDateInPast =
            Error.Validation(
                "sales.order.required_date_past",
                "The required-by date cannot be in the past.");
    }

    /// <summary>Failures relating to a <see cref="Orders.SalesOrderLine"/>.</summary>
    public static class Line
    {
        /// <summary>The line is not on this order.</summary>
        public static Error NotFound(string identifier) =>
            Error.NotFound("sales.line.not_found", $"Line '{identifier}' is not on this order.");

        /// <summary>A part is required.</summary>
        public static readonly Error PartRequired =
            Error.Validation("sales.line.part_required", "A part is required.");

        /// <summary>The catalogue has never heard of that part.</summary>
        public static Error PartNotInCatalogue(string identifier) =>
            Error.NotFound(
                "sales.line.part_not_in_catalogue",
                $"No part in the catalogue matches '{identifier}'.");

        /// <summary>
        /// The part exists but is not something that may be sold today.
        /// <para>
        /// A draft part is half set up; an obsolete one is kept only so old invoices still
        /// resolve. Either way the answer is no, and where the catalogue knows what replaces it,
        /// saying so is the difference between a refusal and a dead end.
        /// </para>
        /// </summary>
        public static Error PartNotSellable(string sku, Guid? supersededBy) =>
            Error.DomainRule(
                "sales.line.part_not_sellable",
                supersededBy is null
                    ? $"{sku} is not available for sale."
                    : $"{sku} is not available for sale. The catalogue replaces it with part " +
                      $"{supersededBy.Value}.");

        /// <summary>Selling nothing is not selling.</summary>
        public static readonly Error QuantityNotPositive =
            Error.Validation("sales.line.quantity_not_positive", "A quantity must be above zero.");

        /// <summary>A negative price is not a price.</summary>
        public static readonly Error PriceNegative =
            Error.Validation("sales.line.price_negative", "A unit price cannot be negative.");

        /// <summary>The discount is outside the plausible range.</summary>
        public static readonly Error DiscountOutOfRange =
            Error.Validation(
                "sales.line.discount_range",
                "A discount must be between 0 and 100 percent.");

        /// <summary>The VAT rate is outside the plausible range.</summary>
        public static readonly Error VatRateOutOfRange =
            Error.Validation("sales.line.vat_rate_range", "A VAT rate must be between 0 and 100 percent.");

        /// <summary>The line currency does not match the order.</summary>
        public static readonly Error CurrencyMismatch =
            Error.Validation("sales.line.currency_mismatch", "A line must be priced in the order's currency.");

        /// <summary>The unit does not match the line.</summary>
        public static readonly Error UnitMismatch =
            Error.Validation(
                "sales.line.unit_mismatch",
                "The dispatched quantity must be in the same unit the line was sold in.");

        /// <summary>That part is already on the order.</summary>
        public static readonly Error DuplicatePart =
            Error.Conflict(
                "sales.line.duplicate_part",
                "That part is already on this order. Change the quantity of the existing line instead.");

        /// <summary>Dispatching nothing is not dispatching.</summary>
        public static readonly Error DispatchNotPositive =
            Error.Validation("sales.line.dispatch_not_positive", "A dispatched quantity must be above zero.");

        /// <summary>More went out than was sold.</summary>
        public static Error OverDispatch(decimal outstanding) =>
            Error.DomainRule(
                "sales.line.over_dispatch",
                $"Only {outstanding} is still outstanding on that line. Add it to the order first " +
                "if the customer is taking more than they asked for.");

        /// <summary>That line is complete.</summary>
        public static readonly Error AlreadyDispatched =
            Error.DomainRule("sales.line.already_dispatched", "That line has already gone out in full.");

        /// <summary>
        /// There is not enough on the shelf to promise this line.
        /// <para>
        /// Refused at confirmation, where somebody is standing there and can do something about
        /// it — reduce the line, try another warehouse, or take it as a back-order deliberately.
        /// Before Sales could ask Inventory this question, the order was confirmed anyway and
        /// the reservation failed silently in a background sweep an hour later.
        /// </para>
        /// </summary>
        public static Error InsufficientStock(
            string sku,
            decimal requested,
            decimal available,
            string unitCode) =>
            Error.DomainRule(
                "sales.line.insufficient_stock",
                $"Only {available} {unitCode} of {sku} is available and this order needs {requested}. " +
                "Reduce the line, ship from another warehouse, or confirm it as a back-order.");

        /// <summary>
        /// The line is sold in one unit and stocked in another.
        /// <para>
        /// Not a shortfall — a different question. Comparing the numbers would be comparing
        /// litres to boxes, and Inventory refuses the reservation outright when it arrives.
        /// </para>
        /// </summary>
        public static Error UnitDiffersFromStock(string sku, string lineUnit, string stockUnit) =>
            Error.Validation(
                "sales.line.unit_differs_from_stock",
                $"{sku} is sold on this line in {lineUnit} but stocked in {stockUnit}. " +
                "Raise the line in the stocking unit.");

        /// <summary>
        /// Nothing prices this part for this customer today.
        /// <para>
        /// Not the same as a part that is missing or withdrawn — the catalogue is happy to sell
        /// it and no price list mentions it, or the quantity is below the smallest pack. Pricing's
        /// own endpoint says which; from here the useful thing is to name the part and let
        /// somebody either price it or type a figure deliberately.
        /// </para>
        /// </summary>
        public static Error NoPrice(string sku) =>
            Error.NotFound(
                "sales.line.no_price",
                $"Nothing prices {sku} for this customer at that quantity. Add it to their price " +
                "list, or set the price on the line by hand.");

        /// <summary>The price came back in a currency the order is not in.</summary>
        public static Error PriceCurrencyMismatch(string sku, string orderCurrency, string priceCurrency) =>
            Error.DomainRule(
                "sales.line.price_currency_mismatch",
                $"{sku} is priced in {priceCurrency} and this order is in {orderCurrency}. " +
                "Converting quietly on a sales line is how exchange-rate losses go missing.");

        /// <summary>Inventory has never heard of this part in this warehouse.</summary>
        public static Error NoStockRecord(string sku) =>
            Error.DomainRule(
                "sales.line.no_stock_record",
                $"There is no stock record for {sku} in that warehouse, so none of it can be " +
                "promised. The part may never have been activated in the catalogue.");

        /// <summary>The quantity cannot drop below what has already gone.</summary>
        public static readonly Error QuantityBelowDispatched =
            Error.DomainRule(
                "sales.line.quantity_below_dispatched",
                "The quantity cannot be reduced below what has already been dispatched.");
    }

    /// <summary>Failures relating to a <see cref="Customers.CustomerAccount"/>.</summary>
    public static class Customer
    {
        /// <summary>Sales has no account for that partner.</summary>
        public static Error NotFound(string identifier) =>
            Error.NotFound(
                "sales.customer.not_found",
                $"No customer account matches '{identifier}'. They may be a partner without the " +
                "customer role, or the account has not caught up yet.");

        /// <summary>A code is required.</summary>
        public static readonly Error CodeRequired =
            Error.Validation("sales.customer.code_required", "A customer code is required.");

        /// <summary>A name is required.</summary>
        public static readonly Error NameRequired =
            Error.Validation("sales.customer.name_required", "A customer name is required.");

        /// <summary>A credit limit cannot be negative.</summary>
        public static readonly Error CreditLimitNegative =
            Error.Validation("sales.customer.credit_limit_negative", "A credit limit cannot be negative.");

        /// <summary>The customer is on hold.</summary>
        public static Error OnHold(string reason) =>
            Error.DomainRule(
                "sales.customer.on_hold",
                $"That account is on hold: {reason}. Somebody has to release it before they can " +
                "order again.");

        /// <summary>The relationship has ended.</summary>
        public static readonly Error Closed =
            Error.DomainRule("sales.customer.closed", "That account is closed.");

        /// <summary>The order would take them over their limit.</summary>
        public static Error CreditLimitExceeded(decimal available, decimal required, string currency) =>
            Error.DomainRule(
                "sales.customer.credit_limit_exceeded",
                $"That order needs {required} {currency} of credit and only {available} {currency} " +
                "is left. Take payment, raise the limit, or split the order.");

        /// <summary>A hold needs an explanation.</summary>
        public static readonly Error HoldReasonRequired =
            Error.Validation("sales.customer.hold_reason_required", "Say why the account is being held.");

        /// <summary>Releasing more than was committed would invent credit.</summary>
        public static readonly Error ReleaseExceedsCommitment =
            Error.DomainRule(
                "sales.customer.release_exceeds_commitment",
                "Cannot release more credit than is committed. Something has double-counted.");

        /// <summary>The amount is in the wrong currency for this account.</summary>
        public static readonly Error CurrencyMismatch =
            Error.Validation(
                "sales.customer.currency_mismatch",
                "That amount is not in the currency this account trades in.");
    }
}
