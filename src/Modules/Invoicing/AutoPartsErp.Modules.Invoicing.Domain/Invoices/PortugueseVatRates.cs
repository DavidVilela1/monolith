using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Invoicing.Domain.Invoices;

/// <summary>
/// Turns a bare percentage into the VAT category a document has to declare.
/// <para>
/// This exists because of a mismatch, and the mismatch is real rather than an oversight. A sales
/// order records a rate — 23 — because that is all it needs to work out what the customer owes.
/// A document has to declare a <i>category</i>, because the QR code has a pair of fields per
/// category and a SAF-T export has a TaxTable entry per one. Nothing in the number itself says
/// which: 13 is intermediate on the mainland and nothing at all in Madeira, where the
/// intermediate rate is 12.
/// </para>
/// <para>
/// So the region has to be known, and the answer has to be a lookup rather than a guess. Getting
/// it wrong does not produce a wrong total — it produces a correct total filed under the wrong
/// heading, which reconciles perfectly right up until somebody compares the VAT return with the
/// SAF-T file.
/// </para>
/// </summary>
public static class PortugueseVatRates
{
    /// <summary>The three rates in force in a region, standard first.</summary>
    /// <param name="Standard">What a brake pad is sold at.</param>
    /// <param name="Intermediate">The middle rate.</param>
    /// <param name="Reduced">The lowest rated band.</param>
    public readonly record struct RateSet(decimal Standard, decimal Intermediate, decimal Reduced);

    /// <summary>
    /// The rates in force in each region.
    /// <para>
    /// Hard-coded, and that is the right call today: these are set by the Orçamento do Estado and
    /// have moved three times in twenty years. When they next move, this table changes and every
    /// document already issued keeps the rate it was signed with, because a line snapshots its own
    /// percentage. What must not happen is a document being re-categorised retrospectively, and
    /// nothing here can do that — this is only ever consulted while a draft is being built.
    /// </para>
    /// </summary>
    /// <param name="region">Which region's rates to read.</param>
    /// <exception cref="ArgumentOutOfRangeException">The region is <see cref="TaxRegion.Unknown"/>.</exception>
    public static RateSet For(TaxRegion region) => region switch
    {
        TaxRegion.Mainland => new RateSet(23m, 13m, 6m),
        TaxRegion.Madeira => new RateSet(22m, 12m, 5m),
        TaxRegion.Azores => new RateSet(16m, 9m, 4m),
        _ => throw new ArgumentOutOfRangeException(nameof(region), region, "No VAT rates for that region."),
    };

    /// <summary>
    /// Builds the rate a document line needs from the percentage a sales order recorded.
    /// <para>
    /// A percentage that matches none of the region's three is refused rather than filed under
    /// the nearest one. It means either a rate from another region — an order taken by a branch
    /// that invoices elsewhere — or a percentage somebody typed, and both are things a person
    /// should see rather than a document should quietly absorb.
    /// </para>
    /// <para>
    /// Zero is refused too, and for a different reason: zero is not a rate, it is an exemption,
    /// and an exemption without its legal basis is a rejected SAF-T file. The line has to be
    /// added by hand with its M-code, which is the one thing an order genuinely cannot tell us.
    /// </para>
    /// </summary>
    /// <param name="region">Which region the document is issued from.</param>
    /// <param name="percent">The rate the order recorded.</param>
    public static Result<VatRate> FromPercent(TaxRegion region, decimal percent)
    {
        if (region == TaxRegion.Unknown)
        {
            return InvoicingErrors.Document.TaxRegionRequired;
        }

        if (percent == 0m)
        {
            return InvoicingErrors.Vat.ExemptNeedsReason;
        }

        RateSet rates = For(region);

        VatCategory category =
            percent == rates.Standard ? VatCategory.Standard
            : percent == rates.Intermediate ? VatCategory.Intermediate
            : percent == rates.Reduced ? VatCategory.Reduced
            : VatCategory.Unknown;

        return category == VatCategory.Unknown
            ? InvoicingErrors.Vat.PercentNotInRegion(percent, region.Code())
            : VatRate.Of(category, percent);
    }
}
