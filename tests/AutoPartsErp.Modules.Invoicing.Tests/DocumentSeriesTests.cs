using AutoPartsErp.Modules.Invoicing.Domain.Series;
using static AutoPartsErp.Modules.Invoicing.Tests.InvoicingTestData;

namespace AutoPartsErp.Modules.Invoicing.Tests;

/// <summary>
/// Gapless numbering, and the rules that stop a series issuing something the tax authority has
/// never heard of.
/// </summary>
public sealed class DocumentSeriesTests
{
    [Fact]
    public void A_series_needs_a_type_and_a_code()
    {
        DocumentSeries.Open(DocumentType.Unknown, "S2026", 2026)
            .Error.Code.Should().Be("invoicing.series.type_required");

        DocumentSeries.Open(DocumentType.Invoice, "  ", 2026)
            .Error.Code.Should().Be("invoicing.series.code_required");
    }

    /// <summary>
    /// The code goes straight into the document number, which goes into the QR code and the SAF-T
    /// export. A space or a slash there breaks the field that separates the series from the
    /// number, and the AT rejects the file rather than guessing where one ends.
    /// </summary>
    [Theory]
    [InlineData("S/2026")]
    [InlineData("S 2026")]
    [InlineData("S-2026")]
    public void A_code_that_would_break_a_document_number_is_refused(string code)
    {
        DocumentSeries.Open(DocumentType.Invoice, code, 2026)
            .Error.Code.Should().Be("invoicing.series.code_invalid_characters");
    }

    [Fact]
    public void A_new_series_is_registered_but_cannot_issue_anything()
    {
        DocumentSeries series = DocumentSeries.Open(DocumentType.Invoice, "serie2026", 2026).Value;

        series.Code.Should().Be("SERIE2026");
        series.Status.Should().Be(SeriesStatus.Registered);
        series.CanIssue.Should().BeFalse();
        series.TakeNextNumber().Error.Code.Should().Be("invoicing.series.not_active");
    }

    /// <summary>
    /// Without the validation code there is no ATCUD, and without an ATCUD there is no legal
    /// document. Refusing here is what stops somebody finding that out at the first audit.
    /// </summary>
    [Fact]
    public void A_series_cannot_go_live_before_the_tax_authority_has_validated_it()
    {
        DocumentSeries series = DocumentSeries.Open(DocumentType.Invoice, "SERIE2026", 2026).Value;

        series.Activate().Error.Code.Should().Be("invoicing.series.not_validated");
    }

    /// <summary>
    /// A hyphen would be indistinguishable from the one that separates the code from the number
    /// inside an ATCUD.
    /// </summary>
    [Fact]
    public void A_validation_code_is_letters_and_digits_only()
    {
        DocumentSeries series = DocumentSeries.Open(DocumentType.Invoice, "SERIE2026", 2026).Value;

        series.Validate("CSDF-7T5H", EntryUtc)
            .Error.Code.Should().Be("invoicing.series.validation_code_invalid_characters");
    }

    /// <summary>
    /// It is baked into every ATCUD the series has already produced, so changing it would
    /// silently invalidate every document issued so far.
    /// </summary>
    [Fact]
    public void The_validation_code_is_recorded_once_and_never_changed()
    {
        DocumentSeries series = DocumentSeries.Open(DocumentType.Invoice, "SERIE2026", 2026).Value;

        series.Validate("csdf7t5h", EntryUtc).IsSuccess.Should().BeTrue();
        series.ValidationCode.Should().Be("CSDF7T5H");

        series.Validate("OTHER123", EntryUtc)
            .Error.Code.Should().Be("invoicing.series.already_validated");
    }

    [Fact]
    public void Numbers_come_out_one_at_a_time_and_in_order()
    {
        DocumentSeries series = ActiveSeries();

        series.TakeNextNumber().Value.Formatted.Should().Be("FT SERIE2026/1");
        series.TakeNextNumber().Value.Formatted.Should().Be("FT SERIE2026/2");
        series.TakeNextNumber().Value.Formatted.Should().Be("FT SERIE2026/3");

        series.IssuedCount.Should().Be(3);
        series.NextNumber.Should().Be(4);
    }

    [Fact]
    public void A_closed_series_issues_nothing_and_cannot_be_reopened()
    {
        DocumentSeries series = ActiveSeries();
        series.Close(EntryUtc).IsSuccess.Should().BeTrue();

        series.TakeNextNumber().Error.Code.Should().Be("invoicing.series.closed");
        series.Activate().Error.Code.Should().Be("invoicing.series.closed");
    }

    [Theory]
    [InlineData(DocumentType.Invoice, "FT")]
    [InlineData(DocumentType.SimplifiedInvoice, "FS")]
    [InlineData(DocumentType.InvoiceReceipt, "FR")]
    [InlineData(DocumentType.CreditNote, "NC")]
    [InlineData(DocumentType.DebitNote, "ND")]
    public void Document_types_use_the_codes_the_tax_authority_expects(DocumentType type, string code)
    {
        type.Code().Should().Be(code);

        DocumentTypeCodes.TryFromCode(code.ToLowerInvariant(), out DocumentType parsed).Should().BeTrue();
        parsed.Should().Be(type);
    }

    [Fact]
    public void Only_a_credit_note_reduces_what_the_customer_owes()
    {
        DocumentType.CreditNote.IsCredit().Should().BeTrue();
        DocumentType.Invoice.IsCredit().Should().BeFalse();
        DocumentType.DebitNote.IsCredit().Should().BeFalse();
    }
}
