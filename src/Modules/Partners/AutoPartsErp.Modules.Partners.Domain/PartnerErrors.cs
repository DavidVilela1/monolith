using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Partners.Domain;

/// <summary>Every failure the Partners module can report, in one place.</summary>
public static class PartnerErrors
{
    /// <summary>Failures relating to a <see cref="Domain.Partners.Partner"/>.</summary>
    public static class Partner
    {
        /// <summary>The partner does not exist.</summary>
        public static Error NotFound(string identifier) =>
            Error.NotFound("partners.partner.not_found", $"No partner matches '{identifier}'.");

        /// <summary>A partner with this code already exists.</summary>
        public static Error CodeAlreadyExists(string code) =>
            Error.Conflict("partners.partner.code_exists", $"Partner code '{code}' is already in use.");

        /// <summary>Another partner already carries this tax number.</summary>
        public static Error TaxNumberAlreadyExists(string taxNumber) =>
            Error.Conflict(
                "partners.partner.tax_number_exists",
                $"Tax number '{taxNumber}' already belongs to another partner. " +
                "Add the missing role to that record rather than creating a second one.");

        /// <summary>A code is required.</summary>
        public static readonly Error CodeRequired =
            Error.Validation("partners.partner.code_required", "A partner code is required.");

        /// <summary>The code is too long.</summary>
        public static readonly Error CodeTooLong =
            Error.Validation("partners.partner.code_too_long", "A partner code may be at most 20 characters.");

        /// <summary>A legal name is required.</summary>
        public static readonly Error NameRequired =
            Error.Validation("partners.partner.name_required", "A legal name is required.");

        /// <summary>The name is too long.</summary>
        public static readonly Error NameTooLong =
            Error.Validation("partners.partner.name_too_long", "A legal name may be at most 200 characters.");

        /// <summary>A tax number is required.</summary>
        public static readonly Error TaxNumberRequired =
            Error.Validation("partners.partner.tax_number_required", "A tax number is required.");

        /// <summary>The tax number is not a plausible shape.</summary>
        public static readonly Error TaxNumberInvalid =
            Error.Validation("partners.partner.tax_number_invalid", "That is not a valid tax number.");

        /// <summary>The tax number failed its country's check digit.</summary>
        public static readonly Error TaxNumberFailsChecksum =
            Error.Validation(
                "partners.partner.tax_number_checksum",
                "That NIF fails its check digit. Two digits are usually transposed.");

        /// <summary>The country code is not two letters.</summary>
        public static readonly Error CountryCodeInvalid =
            Error.Validation(
                "partners.partner.country_invalid",
                "A country code must be two letters, for example PT or ES.");

        /// <summary>A customer must have somewhere to send invoices.</summary>
        public static readonly Error BillingAddressRequired =
            Error.DomainRule(
                "partners.partner.billing_address_required",
                "A customer needs a billing address before they can be invoiced.");

        /// <summary>The partner is not a customer.</summary>
        public static readonly Error NotACustomer =
            Error.DomainRule("partners.partner.not_a_customer", "That partner is not set up as a customer.");

        /// <summary>The partner is not a supplier.</summary>
        public static readonly Error NotASupplier =
            Error.DomainRule("partners.partner.not_a_supplier", "That partner is not set up as a supplier.");

        /// <summary>A hold needs an explanation.</summary>
        public static readonly Error HoldReasonRequired =
            Error.Validation(
                "partners.partner.hold_reason_required",
                "Say why the account is being held. Somebody will have to explain it to them.");

        /// <summary>The partner is not on hold.</summary>
        public static readonly Error NotOnHold =
            Error.DomainRule("partners.partner.not_on_hold", "That partner is not on hold.");

        /// <summary>A closed partner is frozen.</summary>
        public static readonly Error ClosedIsReadOnly =
            Error.DomainRule(
                "partners.partner.closed_readonly",
                "A closed partner is kept only so historical documents resolve, and cannot be changed.");
    }

    /// <summary>Failures relating to an <see cref="Domain.Partners.Address"/>.</summary>
    public static class Address
    {
        /// <summary>An address kind is required.</summary>
        public static readonly Error KindRequired =
            Error.Validation("partners.address.kind_required", "Say what the address is for.");

        /// <summary>The first line is required.</summary>
        public static readonly Error Line1Required =
            Error.Validation("partners.address.line1_required", "A street address is required.");

        /// <summary>A postcode is required.</summary>
        public static readonly Error PostcodeRequired =
            Error.Validation("partners.address.postcode_required", "A postcode is required.");

        /// <summary>A city is required.</summary>
        public static readonly Error CityRequired =
            Error.Validation("partners.address.city_required", "A city is required.");

        /// <summary>That address is already recorded.</summary>
        public static readonly Error Duplicate =
            Error.Conflict("partners.address.duplicate", "That address is already recorded against this partner.");

        /// <summary>That address is not recorded against this partner.</summary>
        public static readonly Error NotFound =
            Error.NotFound("partners.address.not_found", "That address is not recorded against this partner.");
    }

    /// <summary>Failures relating to a <see cref="Domain.Partners.ContactDetail"/>.</summary>
    public static class Contact
    {
        /// <summary>A name is required.</summary>
        public static readonly Error NameRequired =
            Error.Validation("partners.contact.name_required", "A contact name is required.");

        /// <summary>A contact with no email and no phone is not a contact.</summary>
        public static readonly Error NoWayToReachThem =
            Error.Validation(
                "partners.contact.no_contact_method",
                "Give an email address or a phone number.");

        /// <summary>The email address is not plausible.</summary>
        public static readonly Error EmailInvalid =
            Error.Validation("partners.contact.email_invalid", "That is not a valid email address.");

        /// <summary>That contact is already recorded.</summary>
        public static readonly Error Duplicate =
            Error.Conflict("partners.contact.duplicate", "That contact is already recorded against this partner.");

        /// <summary>That contact is not recorded against this partner.</summary>
        public static readonly Error NotFound =
            Error.NotFound("partners.contact.not_found", "That contact is not recorded against this partner.");
    }

    /// <summary>Failures relating to trading terms.</summary>
    public static class Terms
    {
        /// <summary>Payment days are outside the plausible range.</summary>
        public static readonly Error DueDaysOutOfRange =
            Error.Validation(
                "partners.terms.due_days_range",
                "Payment days must be between 0 and 365. Zero means payment on delivery.");

        /// <summary>A payment method is required.</summary>
        public static readonly Error PaymentMethodRequired =
            Error.Validation("partners.terms.payment_method_required", "A payment method is required.");

        /// <summary>A credit limit cannot be negative.</summary>
        public static readonly Error CreditLimitNegative =
            Error.Validation("partners.terms.credit_limit_negative", "A credit limit cannot be negative.");

        /// <summary>Credit without a due date is not a credit arrangement.</summary>
        public static readonly Error CreditWithoutPaymentPeriod =
            Error.DomainRule(
                "partners.terms.credit_without_period",
                "A customer with a credit limit needs a payment period. Terms that let someone owe money with no deadline are not terms.");

        /// <summary>The lead time is outside the plausible range.</summary>
        public static readonly Error LeadTimeOutOfRange =
            Error.Validation("partners.terms.lead_time_range", "A lead time must be between 0 and 365 days.");

        /// <summary>A minimum order value cannot be negative.</summary>
        public static readonly Error MinimumOrderNegative =
            Error.Validation(
                "partners.terms.minimum_order_negative",
                "A minimum order value cannot be negative.");
    }
}
