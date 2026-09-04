using AutoPartsErp.Modules.Invoicing.Domain.Invoices;
using static AutoPartsErp.Modules.Invoicing.Tests.InvoicingTestData;

namespace AutoPartsErp.Modules.Invoicing.Tests;

/// <summary>
/// The four characters printed on a document, taken from the 1st, 11th, 21st and 31st positions
/// of the base64 signature exactly as Portaria 363/2010 article 6 states.
/// </summary>
public sealed class DocumentSignatureTests
{
    [Fact]
    public void The_printed_characters_come_from_the_positions_the_law_states()
    {
        // 1234567890 1234567890 1234567890 1
        const string signature = "ABCDEFGHIJ" + "KLMNOPQRST" + "UVWXYZabcd" + "e" + "fghijklmnop";

        DocumentSignature.PrintedPositions.Should().Equal(1, 11, 21, 31);
        DocumentSignature.Create(signature).Value.Printed.Should().Be("AKUe");
    }

    [Fact]
    public void The_full_signature_is_kept_whole_for_the_chain_and_the_export()
    {
        string signature = PredictableSignature;

        DocumentSignature.Create(signature).Value.Value.Should().Be(signature);
    }

    /// <summary>
    /// A 1024-bit RSA signature is 172 base64 characters, so position 31 always exists. Anything
    /// shorter means a key below the legal size or a signer returning something that is not a
    /// signature — and taking four characters out of it would put plausible symbols on a document
    /// that could never be verified.
    /// </summary>
    [Fact]
    public void A_signature_too_short_to_reach_position_31_is_refused()
    {
        DocumentSignature.Create(new string('x', 30))
            .Error.Code.Should().Be("invoicing.document.signature_too_short");
    }

    [Fact]
    public void Nothing_at_all_is_refused()
    {
        DocumentSignature.Create("   ")
            .Error.Code.Should().Be("invoicing.document.signature_required");
    }
}

/// <summary>The document's unique code: validation code, hyphen, number.</summary>
public sealed class AtcudTests
{
    [Theory]
    [InlineData("CSDF7T5H", 35, "CSDF7T5H-35")]
    [InlineData("TES123TE", 4561, "TES123TE-4561")]
    [InlineData("TES123TE", 1, "TES123TE-1")]
    public void The_code_is_the_validation_code_a_hyphen_and_the_number(
        string validationCode, int number, string expected)
    {
        Atcud.Create(validationCode, number).Value.Value.Should().Be(expected);
    }

    /// <summary>
    /// Not zero-padded. The tax authority's own worked examples show it plain, and padding would
    /// produce a code that does not match the one the series would generate.
    /// </summary>
    [Fact]
    public void The_number_is_not_padded()
    {
        Atcud.Create("CSDF7T5H", 7).Value.Value.Should().Be("CSDF7T5H-7");
    }

    [Fact]
    public void The_validation_code_is_normalized()
    {
        Atcud.Create(" abc123 ", 7).Value.Value.Should().Be("ABC123-7");
    }

    [Fact]
    public void There_is_no_code_without_a_validated_series()
    {
        Atcud.Create(null, 1).Error.Code.Should().Be("invoicing.document.atcud_needs_validation_code");
    }

    [Fact]
    public void Numbers_within_a_series_start_at_one()
    {
        Atcud.Create("CSDF7T5H", 0).Error.Code.Should().Be("invoicing.document.atcud_number_not_positive");
    }
}

/// <summary>VAT categories, and the rules that keep an exempt line explaining itself.</summary>
public sealed class VatRateTests
{
    [Fact]
    public void The_mainland_standard_rate_is_twenty_three_percent()
    {
        VatRate.PortugalStandard.TaxCode.Should().Be("NOR");
        VatRate.PortugalStandard.Percent.Should().Be(23m);
        VatRate.PortugalStandard.IsExempt.Should().BeFalse();
    }

    [Theory]
    [InlineData(VatCategory.Reduced, 6, "RED")]
    [InlineData(VatCategory.Intermediate, 13, "INT")]
    [InlineData(VatCategory.Standard, 23, "NOR")]
    public void Rated_categories_carry_their_saft_code(VatCategory category, decimal percent, string code)
    {
        VatRate.Of(category, percent).Value.TaxCode.Should().Be(code);
    }

    /// <summary>
    /// It would sail through every arithmetic check and land in the wrong pair of QR fields, where
    /// nothing would ever notice it.
    /// </summary>
    [Fact]
    public void A_rated_category_at_zero_percent_is_somebody_meaning_exempt()
    {
        VatRate.Of(VatCategory.Standard, 0m)
            .Error.Code.Should().Be("invoicing.vat.rated_needs_percent");
    }

    /// <summary>
    /// An exempt line without a stated legal basis is a rejected SAF-T file and, at an inspection,
    /// an assessment for the VAT that was not charged.
    /// </summary>
    [Fact]
    public void An_exempt_line_cannot_exist_without_its_legal_basis()
    {
        VatRate.Of(VatCategory.Exempt, 0m)
            .Error.Code.Should().Be("invoicing.vat.exempt_needs_reason");

        VatRate.ExemptWith(null, "Isento artigo 9. do CIVA")
            .Error.Code.Should().Be("invoicing.vat.exemption_code_required");

        VatRate.ExemptWith("M07", "  ")
            .Error.Code.Should().Be("invoicing.vat.exempt_needs_reason");
    }

    [Fact]
    public void An_exemption_with_both_is_accepted_and_normalized()
    {
        VatRate rate = VatRate.ExemptWith("m07", " Isento artigo 9. do CIVA ").Value;

        rate.TaxCode.Should().Be("ISE");
        rate.ExemptionCode.Should().Be("M07");
        rate.ExemptionReason.Should().Be("Isento artigo 9. do CIVA");
        rate.Percent.Should().Be(0m);
        rate.IsExempt.Should().BeTrue();
    }

    [Theory]
    [InlineData(TaxRegion.Mainland, "PT")]
    [InlineData(TaxRegion.Azores, "PT-AC")]
    [InlineData(TaxRegion.Madeira, "PT-MA")]
    public void Tax_regions_use_the_saft_values(TaxRegion region, string code)
    {
        region.Code().Should().Be(code);
    }
}
