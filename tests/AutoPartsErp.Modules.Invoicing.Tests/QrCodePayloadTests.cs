using AutoPartsErp.Modules.Invoicing.Domain.Invoices;
using AutoPartsErp.Modules.Invoicing.Domain.Series;
using static AutoPartsErp.Modules.Invoicing.Tests.InvoicingTestData;

namespace AutoPartsErp.Modules.Invoicing.Tests;

/// <summary>
/// The string inside the QR code, checked field by field against the tax authority's technical
/// specification.
/// <para>
/// This is the one artefact an inspector can scan off a printed page, so it is worth testing
/// against the published worked example rather than against what looks right.
/// </para>
/// </summary>
public sealed class QrCodePayloadTests
{
    private static DocumentSignature Signature => DocumentSignature.Create(PredictableSignature).Value;

    /// <summary>
    /// The figures from the specification's own worked example, for the fields this builder
    /// produces. The example predates the final ATCUD format and shows field H without its
    /// hyphen; the tax authority's current guidance is <c>validationCode-number</c>, which is
    /// what <see cref="Atcud"/> produces and what appears here.
    /// </summary>
    [Fact]
    public void The_payload_matches_the_published_example_field_for_field()
    {
        var taxes = new TaxSummary(
            ExemptBase: 12000.00m,
            ReducedBase: 15000.00m,
            ReducedVat: 900.00m,
            IntermediateBase: 50000.00m,
            IntermediateVat: 6500.00m,
            StandardBase: 80000.00m,
            StandardVat: 18400.00m);

        string payload = QrCodePayload.Build(
            issuerNif: "123456789",
            customerNif: null,
            customerCountry: "PT",
            type: DocumentType.Invoice,
            status: InvoiceStatus.Normal,
            documentDate: new DateOnly(2019, 12, 31),
            documentNumber: "FT AB2019/0035",
            atcud: Atcud.Create("CSDF7T5H", 35).Value,
            region: TaxRegion.Mainland,
            taxes: taxes,
            signature: Signature,
            certificateNumber: "9999");

        payload.Should().Be(
            "A:123456789*B:999999990*C:PT*D:FT*E:N*F:20191231*G:FT AB2019/0035*H:CSDF7T5H-35*I1:PT*"
            + "I2:12000.00*I3:15000.00*I4:900.00*I5:50000.00*I6:6500.00*I7:80000.00*I8:18400.00*"
            + "N:25800.00*O:182800.00*Q:kLp0*R:9999");
    }

    /// <summary>
    /// An omitted field means "no lines in this category". <c>I4:0.00</c> would mean "lines at
    /// the reduced rate that somehow produced no VAT", which is a different and suspicious claim.
    /// </summary>
    [Fact]
    public void Categories_with_nothing_in_them_are_omitted_rather_than_written_as_zero()
    {
        string payload = QrCodePayload.Build(
            issuerNif: "501234567",
            customerNif: "298765432",
            customerCountry: "PT",
            type: DocumentType.InvoiceReceipt,
            status: InvoiceStatus.Normal,
            documentDate: Today,
            documentNumber: "FR SERIE2026/12",
            atcud: Atcud.Create("JFF2AKKV", 12).Value,
            region: TaxRegion.Mainland,
            taxes: new TaxSummary(0m, 0m, 0m, 0m, 0m, 100m, 23m),
            signature: Signature,
            certificateNumber: "0");

        payload.Should().Be(
            "A:501234567*B:298765432*C:PT*D:FR*E:N*F:20260904*G:FR SERIE2026/12*H:JFF2AKKV-12*I1:PT*"
            + "I7:100.00*I8:23.00*N:23.00*O:123.00*Q:kLp0*R:0");
    }

    /// <summary>Most of a trade counter's morning.</summary>
    [Fact]
    public void An_unidentified_customer_gets_the_final_consumer_number()
    {
        string payload = QrCodePayload.Build(
            "501234567", null, "PT", DocumentType.SimplifiedInvoice, InvoiceStatus.Normal,
            Today, "FS SERIE2026/1", Atcud.Create("JFF2AKKV", 1).Value, TaxRegion.Mainland,
            new TaxSummary(0m, 0m, 0m, 0m, 0m, 10m, 2.30m), Signature, "0");

        payload.Should().Contain($"*B:{QrCodePayload.FinalConsumerNif}*");
    }

    /// <summary>
    /// A credit note for the full value of an invoice legitimately nets to nothing, and leaving
    /// the fields out would make it look like a document with no totals rather than one that
    /// balances.
    /// </summary>
    [Fact]
    public void The_two_mandatory_totals_are_written_even_when_they_are_zero()
    {
        string payload = QrCodePayload.Build(
            "501234567", "298765432", "PT", DocumentType.CreditNote, InvoiceStatus.Normal,
            Today, "NC SERIE2026/1", Atcud.Create("JFF2AKKV", 1).Value, TaxRegion.Mainland,
            default, Signature, "0");

        payload.Should().Contain("*N:0.00*O:0.00*");
    }

    [Theory]
    [InlineData(InvoiceStatus.Normal, "N")]
    [InlineData(InvoiceStatus.Voided, "A")]
    [InlineData(InvoiceStatus.Billed, "F")]
    public void Statuses_use_the_saft_codes(InvoiceStatus status, string code)
    {
        status.Code().Should().Be(code);
    }
}
