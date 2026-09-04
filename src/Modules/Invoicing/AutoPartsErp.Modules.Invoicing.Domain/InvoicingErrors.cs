using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Invoicing.Domain;

/// <summary>Every failure the Invoicing module can report, in one place.</summary>
public static class InvoicingErrors
{
    /// <summary>
    /// Failures relating to a <see cref="Domain.Series.DocumentSeries"/>.
    /// <para>
    /// The cref is qualified because this class is itself called <c>Series</c> and shadows the
    /// namespace of the same name.
    /// </para>
    /// </summary>
    public static class Series
    {
        /// <summary>The series does not exist.</summary>
        public static Error NotFound(string identifier) =>
            Error.NotFound("invoicing.series.not_found", $"No document series matches '{identifier}'.");

        /// <summary>The document type was not said.</summary>
        public static readonly Error TypeRequired =
            Error.Validation(
                "invoicing.series.type_required",
                "Say what kind of document the series numbers: FT, FS, FR, NC or ND.");

        /// <summary>A code is required.</summary>
        public static readonly Error CodeRequired =
            Error.Validation("invoicing.series.code_required", "A series code is required.");

        /// <summary>The code is too long.</summary>
        public static readonly Error CodeTooLong =
            Error.Validation(
                "invoicing.series.code_too_long",
                $"A series code cannot be longer than {Domain.Series.DocumentSeries.MaxCodeLength} characters.");

        /// <summary>The code has characters that would break a document number.</summary>
        public static readonly Error CodeInvalidCharacters =
            Error.Validation(
                "invoicing.series.code_invalid_characters",
                "A series code may only contain letters and digits. Spaces and slashes would break " +
                "the document number they appear in.");

        /// <summary>That code is already in use for that type and year.</summary>
        public static readonly Error CodeExists =
            Error.Conflict(
                "invoicing.series.code_exists",
                "A series with that code already exists for that document type and year.");

        /// <summary>The year is not a plausible one.</summary>
        public static readonly Error YearImplausible =
            Error.Validation("invoicing.series.year_implausible", "That is not a plausible year.");

        /// <summary>A validation code is required.</summary>
        public static readonly Error ValidationCodeRequired =
            Error.Validation(
                "invoicing.series.validation_code_required",
                "The validation code the tax authority returned is required.");

        /// <summary>The validation code is too long.</summary>
        public static readonly Error ValidationCodeTooLong =
            Error.Validation(
                "invoicing.series.validation_code_too_long",
                "That validation code is longer than any the tax authority issues.");

        /// <summary>The validation code has characters the AT does not issue.</summary>
        public static readonly Error ValidationCodeInvalidCharacters =
            Error.Validation(
                "invoicing.series.validation_code_invalid_characters",
                "A validation code is letters and digits only. A hyphen in particular would be " +
                "indistinguishable from the one that separates it from the document number.");

        /// <summary>It already has one.</summary>
        public static readonly Error AlreadyValidated =
            Error.DomainRule(
                "invoicing.series.already_validated",
                "That series already has a validation code. It is part of every ATCUD the series " +
                "has produced, so it cannot be changed. Open a new series instead.");

        /// <summary>It has no validation code yet.</summary>
        public static readonly Error NotValidated =
            Error.DomainRule(
                "invoicing.series.not_validated",
                "That series has not been declared to the tax authority yet. Without its validation " +
                "code there is no ATCUD, and without an ATCUD there is no legal document.");

        /// <summary>It is already live.</summary>
        public static readonly Error AlreadyActive =
            Error.DomainRule("invoicing.series.already_active", "That series is already live.");

        /// <summary>It is not live.</summary>
        public static readonly Error NotActive =
            Error.DomainRule(
                "invoicing.series.not_active",
                "That series is not live, so it cannot issue a document.");

        /// <summary>It is closed.</summary>
        public static readonly Error Closed =
            Error.DomainRule(
                "invoicing.series.closed",
                "That series is closed. Everything it issued stays as it is, and nothing further " +
                "comes out of it.");
    }

    /// <summary>Failures relating to an <see cref="Invoices.Invoice"/>.</summary>
    public static class Document
    {
        /// <summary>The document does not exist.</summary>
        public static Error NotFound(string identifier) =>
            Error.NotFound("invoicing.document.not_found", $"No document matches '{identifier}'.");

        /// <summary>The type was not said.</summary>
        public static readonly Error TypeRequired =
            Error.Validation("invoicing.document.type_required", "Say what kind of document this is.");

        /// <summary>A customer is required.</summary>
        public static readonly Error CustomerRequired =
            Error.Validation("invoicing.document.customer_required", "A customer is required.");

        /// <summary>A customer name is required.</summary>
        public static readonly Error CustomerNameRequired =
            Error.Validation("invoicing.document.customer_name_required", "A customer name is required.");

        /// <summary>The name is too long.</summary>
        public static readonly Error CustomerNameTooLong =
            Error.Validation(
                "invoicing.document.customer_name_too_long",
                $"A customer name cannot be longer than {Invoices.Invoice.MaxCustomerNameLength} characters.");

        /// <summary>The country is not a two-letter code.</summary>
        public static readonly Error CustomerCountryInvalid =
            Error.Validation(
                "invoicing.document.customer_country_invalid",
                "A customer country must be a two-letter ISO code.");

        /// <summary>An invoice has to name its customer.</summary>
        public static readonly Error InvoiceNeedsCustomerTaxNumber =
            Error.Validation(
                "invoicing.document.invoice_needs_tax_number",
                "An invoice has to identify its customer by tax number. A sale to somebody who is " +
                "not identified is a simplified invoice, which is a different document type.");

        /// <summary>The tax region was not said.</summary>
        public static readonly Error TaxRegionRequired =
            Error.Validation(
                "invoicing.document.tax_region_required",
                "Say which tax region the document is issued from: mainland, Azores or Madeira. " +
                "The three have different rates for the same category.");

        /// <summary>An empty document cannot be issued.</summary>
        public static readonly Error NoLines =
            Error.DomainRule("invoicing.document.no_lines", "A document with no lines cannot be issued.");

        /// <summary>It has already been issued.</summary>
        public static readonly Error AlreadyIssued =
            Error.DomainRule(
                "invoicing.document.already_issued",
                "That document has been issued. It has a number in a gapless series and a signature " +
                "over its total, so nothing on it can change. Void it, or raise a credit note.");

        /// <summary>It has not been issued.</summary>
        public static readonly Error NotIssued =
            Error.DomainRule(
                "invoicing.document.not_issued",
                "That document is still a draft. There is nothing to void.");

        /// <summary>The series numbers a different kind of document.</summary>
        public static readonly Error SeriesTypeMismatch =
            Error.DomainRule(
                "invoicing.document.series_type_mismatch",
                "That series numbers a different kind of document. A series is declared to the tax " +
                "authority for one type and cannot carry another.");

        /// <summary>The series belongs to a different year.</summary>
        public static readonly Error SeriesYearMismatch =
            Error.DomainRule(
                "invoicing.document.series_year_mismatch",
                "That series belongs to a different year than the document date.");

        /// <summary>An ATCUD needs the series' validation code.</summary>
        public static readonly Error AtcudNeedsValidationCode =
            Error.DomainRule(
                "invoicing.document.atcud_needs_validation_code",
                "An ATCUD cannot be built without the series' validation code.");

        /// <summary>An ATCUD number starts at one.</summary>
        public static readonly Error AtcudNumberNotPositive =
            Error.Validation(
                "invoicing.document.atcud_number_not_positive",
                "A document number within a series starts at 1.");

        /// <summary>The signer returned nothing.</summary>
        public static readonly Error SignatureRequired =
            Error.Unexpected(
                "invoicing.document.signature_required",
                "The document signer returned nothing. A document cannot be issued unsigned.");

        /// <summary>The signature is too short to take the printed characters from.</summary>
        public static readonly Error SignatureTooShort =
            Error.Unexpected(
                "invoicing.document.signature_too_short",
                "That signature is too short to take the four printed characters from, which means " +
                "the signing key is smaller than the law allows or the signer returned something " +
                "that is not a signature.");

        /// <summary>It is already voided.</summary>
        public static readonly Error AlreadyVoided =
            Error.DomainRule("invoicing.document.already_voided", "That document has already been voided.");

        /// <summary>A void needs an explanation.</summary>
        public static readonly Error VoidReasonRequired =
            Error.Validation(
                "invoicing.document.void_reason_required",
                "Say why the document is being voided. It goes on the SAF-T export and it is the " +
                "only thing that will explain the gap in the paperwork later.");

        /// <summary>The reason is too long.</summary>
        public static readonly Error VoidReasonTooLong =
            Error.Validation(
                "invoicing.document.void_reason_too_long",
                $"A void reason cannot be longer than {Invoices.Invoice.MaxVoidReasonLength} characters.");
    }

    /// <summary>Failures relating to an <see cref="Invoices.InvoiceLine"/>.</summary>
    public static class Line
    {
        /// <summary>Line numbers start at one.</summary>
        public static readonly Error NumberNotPositive =
            Error.Validation("invoicing.line.number_not_positive", "A line number starts at 1.");

        /// <summary>A part is required.</summary>
        public static readonly Error PartRequired =
            Error.Validation("invoicing.line.part_required", "A part is required.");

        /// <summary>A description is required.</summary>
        public static readonly Error DescriptionRequired =
            Error.Validation(
                "invoicing.line.description_required",
                "A line has to describe what was sold. A document that does not say is not a " +
                "document anybody can act on.");

        /// <summary>Invoicing nothing is not invoicing.</summary>
        public static readonly Error QuantityNotPositive =
            Error.Validation("invoicing.line.quantity_not_positive", "A quantity must be above zero.");

        /// <summary>A negative price is not a price.</summary>
        public static readonly Error PriceNegative =
            Error.Validation(
                "invoicing.line.price_negative",
                "A unit price cannot be negative. Money going the other way is a credit note.");

        /// <summary>The discount is outside the plausible range.</summary>
        public static readonly Error DiscountOutOfRange =
            Error.Validation("invoicing.line.discount_range", "A discount must be between 0 and 100 percent.");

        /// <summary>The line currency does not match the document.</summary>
        public static readonly Error CurrencyMismatch =
            Error.Validation(
                "invoicing.line.currency_mismatch",
                "A line must be priced in the document's currency.");
    }

    /// <summary>Failures relating to a <see cref="Invoices.VatRate"/>.</summary>
    public static class Vat
    {
        /// <summary>The category was not said.</summary>
        public static readonly Error CategoryRequired =
            Error.Validation(
                "invoicing.vat.category_required",
                "Say which VAT category applies: exempt, reduced, intermediate or standard.");

        /// <summary>The percentage is outside the plausible range.</summary>
        public static readonly Error PercentOutOfRange =
            Error.Validation("invoicing.vat.percent_range", "A VAT rate must be between 0 and 100 percent.");

        /// <summary>A rated category needs a rate.</summary>
        public static readonly Error RatedCategoryNeedsPercent =
            Error.Validation(
                "invoicing.vat.rated_needs_percent",
                "A reduced, intermediate or standard rate cannot be zero percent. A line that " +
                "charges no VAT is exempt, and an exempt line has to say why.");

        /// <summary>An exempt line needs a reason.</summary>
        public static readonly Error ExemptNeedsReason =
            Error.Validation(
                "invoicing.vat.exempt_needs_reason",
                "An exempt line has to state the legal basis for the exemption. Without it the " +
                "SAF-T file is rejected and the VAT is assessed anyway.");

        /// <summary>An exempt line needs the AT's code for the exemption.</summary>
        public static readonly Error ExemptionCodeRequired =
            Error.Validation(
                "invoicing.vat.exemption_code_required",
                "An exempt line needs the tax authority's exemption code, e.g. M07.");

        /// <summary>The exemption code is too long.</summary>
        public static readonly Error ExemptionCodeTooLong =
            Error.Validation(
                "invoicing.vat.exemption_code_too_long",
                "That is longer than any exemption code the tax authority issues.");

        /// <summary>The exemption reason is too long.</summary>
        public static readonly Error ExemptionReasonTooLong =
            Error.Validation(
                "invoicing.vat.exemption_reason_too_long",
                $"An exemption reason cannot be longer than " +
                $"{Invoices.VatRate.MaxExemptionReasonLength} characters.");
    }
}
