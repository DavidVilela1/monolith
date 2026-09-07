using AutoPartsErp.Modules.Invoicing.Domain.Invoices;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Invoicing.Tests;

/// <summary>
/// Turning the percentage a sales order recorded into the category a document has to declare.
/// <para>
/// The mistake this guards against does not produce a wrong total. It produces a correct total
/// filed under the wrong heading, which reconciles perfectly right up until somebody compares the
/// VAT return with the SAF-T file.
/// </para>
/// </summary>
public sealed class PortugueseVatRatesTests
{
    [Theory]
    [InlineData(TaxRegion.Mainland, 23, VatCategory.Standard)]
    [InlineData(TaxRegion.Mainland, 13, VatCategory.Intermediate)]
    [InlineData(TaxRegion.Mainland, 6, VatCategory.Reduced)]
    [InlineData(TaxRegion.Madeira, 22, VatCategory.Standard)]
    [InlineData(TaxRegion.Madeira, 12, VatCategory.Intermediate)]
    [InlineData(TaxRegion.Madeira, 5, VatCategory.Reduced)]
    [InlineData(TaxRegion.Azores, 16, VatCategory.Standard)]
    [InlineData(TaxRegion.Azores, 9, VatCategory.Intermediate)]
    [InlineData(TaxRegion.Azores, 4, VatCategory.Reduced)]
    public void Each_regions_three_rates_map_to_their_categories(
        TaxRegion region,
        decimal percent,
        VatCategory expected)
    {
        Result<VatRate> rate = PortugueseVatRates.FromPercent(region, percent);

        rate.IsSuccess.Should().BeTrue();
        rate.Value.Category.Should().Be(expected);
        rate.Value.Percent.Should().Be(percent);
    }

    /// <summary>
    /// The case that makes the region necessary rather than decorative: the same number means
    /// two different things, and in one of the two it means nothing at all.
    /// </summary>
    [Fact]
    public void The_same_percentage_is_a_different_category_in_a_different_region()
    {
        PortugueseVatRates.FromPercent(TaxRegion.Mainland, 6m)
            .Value.Category.Should().Be(VatCategory.Reduced);

        PortugueseVatRates.FromPercent(TaxRegion.Azores, 6m)
            .Error.Code.Should().Be("invoicing.vat.percent_not_in_region");
    }

    /// <summary>
    /// A rate from somewhere else, or one somebody typed. Filing it under the nearest category
    /// would be the system inventing a fact about tax.
    /// </summary>
    [Theory]
    [InlineData(23.5)]
    [InlineData(21)]
    [InlineData(19)]
    [InlineData(100)]
    public void A_percentage_the_region_does_not_use_is_refused_and_says_which_region(decimal percent)
    {
        Result<VatRate> rate = PortugueseVatRates.FromPercent(TaxRegion.Mainland, percent);

        rate.Error.Code.Should().Be("invoicing.vat.percent_not_in_region");
        rate.Error.Description.Should().Contain("PT");
    }

    /// <summary>
    /// Zero is not a rate, it is an exemption — and an exemption without its legal basis is a
    /// rejected SAF-T file and the VAT assessed anyway. The M-code is the one thing an order
    /// genuinely cannot tell us, so the line has to be added by hand.
    /// </summary>
    [Fact]
    public void A_zero_rate_is_an_exemption_and_an_exemption_needs_its_reason()
    {
        PortugueseVatRates.FromPercent(TaxRegion.Mainland, 0m)
            .Error.Code.Should().Be("invoicing.vat.exempt_needs_reason");
    }

    [Fact]
    public void A_document_with_no_region_cannot_categorise_anything()
    {
        PortugueseVatRates.FromPercent(TaxRegion.Unknown, 23m)
            .Error.Code.Should().Be("invoicing.document.tax_region_required");
    }

    [Fact]
    public void The_mainland_rates_are_the_ones_a_brake_pad_is_sold_at()
    {
        PortugueseVatRates.RateSet rates = PortugueseVatRates.For(TaxRegion.Mainland);

        rates.Standard.Should().Be(23m);
        rates.Intermediate.Should().Be(13m);
        rates.Reduced.Should().Be(6m);
    }
}
