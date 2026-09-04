using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Pricing.Domain;

/// <summary>Every failure the Pricing module can report, in one place.</summary>
public static class PricingErrors
{
    /// <summary>Failures relating to a <see cref="PriceLists.PriceList"/>.</summary>
    public static class List
    {
        /// <summary>The list does not exist.</summary>
        public static Error NotFound(string identifier) =>
            Error.NotFound("pricing.list.not_found", $"No price list matches '{identifier}'.");

        /// <summary>A code is required.</summary>
        public static readonly Error CodeRequired =
            Error.Validation("pricing.list.code_required", "A price list code is required.");

        /// <summary>The code is too long.</summary>
        public static readonly Error CodeTooLong =
            Error.Validation(
                "pricing.list.code_too_long",
                $"A price list code cannot be longer than {PriceLists.PriceList.MaxCodeLength} characters.");

        /// <summary>A name is required.</summary>
        public static readonly Error NameRequired =
            Error.Validation("pricing.list.name_required", "A price list name is required.");

        /// <summary>The name is too long.</summary>
        public static readonly Error NameTooLong =
            Error.Validation(
                "pricing.list.name_too_long",
                $"A price list name cannot be longer than {PriceLists.PriceList.MaxNameLength} characters.");

        /// <summary>The kind was not said.</summary>
        public static readonly Error KindRequired =
            Error.Validation(
                "pricing.list.kind_required",
                "Say what the list is for: standard, customer or promotion.");

        /// <summary>That code is already in use.</summary>
        public static readonly Error CodeExists =
            Error.Conflict(
                "pricing.list.code_exists",
                "A price list with that code already exists.");

        /// <summary>The period runs backwards.</summary>
        public static readonly Error PeriodInverted =
            Error.Validation(
                "pricing.list.period_inverted",
                "A price list cannot stop applying before it starts.");

        /// <summary>A promotion with no end is a price change.</summary>
        public static readonly Error PromotionNeedsEndDate =
            Error.Validation(
                "pricing.list.promotion_needs_end",
                "A promotion needs a last day. One that never ends is a price change, and should " +
                "be made on the standard list where everyone can see it.");

        /// <summary>The list is already live.</summary>
        public static readonly Error AlreadyActive =
            Error.DomainRule("pricing.list.already_active", "That price list is already live.");

        /// <summary>The list has been withdrawn.</summary>
        public static readonly Error Archived =
            Error.DomainRule(
                "pricing.list.archived",
                "That price list has been withdrawn and cannot be changed. Open a new one.");

        /// <summary>An empty list cannot go live.</summary>
        public static readonly Error NoPrices =
            Error.DomainRule(
                "pricing.list.no_prices",
                "A price list with no prices in it cannot go live.");

        /// <summary>Something has to be the fallback.</summary>
        public static readonly Error CannotArchiveDefault =
            Error.DomainRule(
                "pricing.list.cannot_archive_default",
                "That is the default list. Make another list the default before withdrawing it, " +
                "or a customer with no agreement has nothing to buy from.");

        /// <summary>Only a live list can be the fallback.</summary>
        public static readonly Error DefaultMustBeActive =
            Error.DomainRule(
                "pricing.list.default_must_be_active",
                "Only a live price list can be the default.");

        /// <summary>Only a standard list can be the fallback.</summary>
        public static readonly Error DefaultMustBeStandard =
            Error.DomainRule(
                "pricing.list.default_must_be_standard",
                "Only a standard price list can be the default. A customer list or a promotion is " +
                "for somebody in particular, or for a while.");

        /// <summary>The fallback cannot expire.</summary>
        public static readonly Error DefaultCannotExpire =
            Error.DomainRule(
                "pricing.list.default_cannot_expire",
                "The default price list cannot have a last day. When it expired, everybody with no " +
                "agreement would stop having a price.");
    }

    /// <summary>Failures relating to a <see cref="PriceLists.PriceBreak"/>.</summary>
    public static class Break
    {
        /// <summary>A break has to start somewhere above zero.</summary>
        public static readonly Error MinimumNotPositive =
            Error.Validation(
                "pricing.break.minimum_not_positive",
                "A quantity break has to start at a quantity above zero.");

        /// <summary>A negative price is not a price.</summary>
        public static readonly Error PriceNegative =
            Error.Validation(
                "pricing.break.price_negative",
                "A price cannot be negative. Money going the other way is a credit note.");
    }

    /// <summary>Failures relating to a <see cref="PriceLists.PriceListEntry"/>.</summary>
    public static class Entry
    {
        /// <summary>The entry does not exist.</summary>
        public static Error NotFound(string identifier) =>
            Error.NotFound("pricing.entry.not_found", $"No price entry matches '{identifier}'.");

        /// <summary>A list is required.</summary>
        public static readonly Error ListRequired =
            Error.Validation("pricing.entry.list_required", "A price list is required.");

        /// <summary>A part is required.</summary>
        public static readonly Error PartRequired =
            Error.Validation("pricing.entry.part_required", "A part is required.");

        /// <summary>That part is already priced in this list.</summary>
        public static readonly Error DuplicatePart =
            Error.Conflict(
                "pricing.entry.duplicate_part",
                "That part already has a price in this list. Change the existing one, or add a " +
                "quantity break to it.");

        /// <summary>The break is in the wrong currency for the entry.</summary>
        public static readonly Error CurrencyMismatch =
            Error.Validation(
                "pricing.entry.currency_mismatch",
                "Every break on a price has to be in the list's currency.");

        /// <summary>Too many breaks.</summary>
        public static readonly Error TooManyBreaks =
            Error.DomainRule(
                "pricing.entry.too_many_breaks",
                $"A price cannot carry more than {PriceLists.PriceListEntry.MaxBreaks} quantity " +
                "breaks. Past that it is a formula, not a price list.");

        /// <summary>There is no break at that quantity.</summary>
        public static Error BreakNotFound(decimal minimumQuantity) =>
            Error.NotFound(
                "pricing.entry.break_not_found",
                $"There is no quantity break starting at {minimumQuantity} on that price.");

        /// <summary>The last break cannot go.</summary>
        public static readonly Error LastBreak =
            Error.DomainRule(
                "pricing.entry.last_break",
                "A price needs at least one quantity break. To stop selling the part from this " +
                "list, remove the price rather than emptying it.");
    }

    /// <summary>Failures relating to a <see cref="Customers.CustomerPricing"/> agreement.</summary>
    public static class Agreement
    {
        /// <summary>The agreement does not exist.</summary>
        public static Error NotFound(string identifier) =>
            Error.NotFound(
                "pricing.agreement.not_found",
                $"No pricing agreement matches '{identifier}'.");

        /// <summary>A customer is required.</summary>
        public static readonly Error CustomerRequired =
            Error.Validation("pricing.agreement.customer_required", "A customer is required.");

        /// <summary>A list is required.</summary>
        public static readonly Error ListRequired =
            Error.Validation("pricing.agreement.list_required", "A price list is required.");

        /// <summary>That customer already has terms.</summary>
        public static readonly Error AlreadyAgreed =
            Error.Conflict(
                "pricing.agreement.already_agreed",
                "That customer already has a pricing agreement. Renegotiate the existing one.");

        /// <summary>The discount is outside the plausible range.</summary>
        public static readonly Error DiscountOutOfRange =
            Error.Validation(
                "pricing.agreement.discount_range",
                "A discount must be between 0 and 100 percent.");

        /// <summary>The period runs backwards.</summary>
        public static readonly Error PeriodInverted =
            Error.Validation(
                "pricing.agreement.period_inverted",
                "An agreement cannot end before it starts.");

        /// <summary>The note is too long.</summary>
        public static readonly Error NoteTooLong =
            Error.Validation(
                "pricing.agreement.note_too_long",
                $"A note cannot be longer than {Customers.CustomerPricing.MaxNoteLength} characters.");

        /// <summary>The agreement is already over.</summary>
        public static readonly Error AlreadyEnded =
            Error.DomainRule("pricing.agreement.already_ended", "That agreement has already ended.");

        /// <summary>Ending it before it started would erase it.</summary>
        public static readonly Error EndBeforeStart =
            Error.DomainRule(
                "pricing.agreement.end_before_start",
                "That would end the agreement before it started. Delete it instead if it should " +
                "never have existed.");
    }

    /// <summary>Failures relating to answering "what does this cost?".</summary>
    public static class Quote
    {
        /// <summary>Nothing prices this part for this customer today.</summary>
        public static Error NoPrice(string partIdentifier) =>
            Error.NotFound(
                "pricing.quote.no_price",
                $"Nothing prices part '{partIdentifier}' for that customer today. It may be missing " +
                "from the list they buy from, or their agreement may have expired.");

        /// <summary>The quantity is below the smallest break.</summary>
        public static Error BelowMinimumQuantity(decimal requested, decimal minimum) =>
            Error.DomainRule(
                "pricing.quote.below_minimum",
                $"That part is not sold in quantities below {minimum}, and {requested} was asked " +
                "for. Somebody has to price it by hand or sell a bigger pack.");

        /// <summary>The list prices in a currency the document is not in.</summary>
        public static Error CurrencyMismatch(string wanted, string found) =>
            Error.DomainRule(
                "pricing.quote.currency_mismatch",
                $"That customer's price list is in {found} and the document is in {wanted}. " +
                "Conversion is not something to do quietly on a sales line.");

        /// <summary>There is no default list to fall back to.</summary>
        public static readonly Error NoDefaultList =
            Error.DomainRule(
                "pricing.quote.no_default_list",
                "That customer has no agreement and there is no default price list to fall back " +
                "on. Make a standard list the default.");
    }
}
