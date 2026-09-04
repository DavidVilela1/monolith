using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Invoicing.Domain.Invoices;

/// <summary>
/// A document's totals, split by VAT category.
/// <para>
/// Every prescribed output wants the split rather than the grand total: the QR code has a pair of
/// fields per category, a SAF-T export has a <c>TaxTable</c> entry per rate, and a VAT return is
/// nothing but this summed over a period. Computing it once on the aggregate and handing the same
/// object to all three is what stops the printed document and the submitted file disagreeing.
/// </para>
/// </summary>
/// <param name="ExemptBase">Value of exempt lines. No VAT against it, by definition.</param>
/// <param name="ReducedBase">Value of lines at the reduced rate.</param>
/// <param name="ReducedVat">VAT on those lines.</param>
/// <param name="IntermediateBase">Value of lines at the intermediate rate.</param>
/// <param name="IntermediateVat">VAT on those lines.</param>
/// <param name="StandardBase">Value of lines at the standard rate.</param>
/// <param name="StandardVat">VAT on those lines.</param>
public readonly record struct TaxSummary(
    decimal ExemptBase,
    decimal ReducedBase,
    decimal ReducedVat,
    decimal IntermediateBase,
    decimal IntermediateVat,
    decimal StandardBase,
    decimal StandardVat)
{
    /// <summary>The net value of the document, before VAT.</summary>
    public decimal NetTotal => ExemptBase + ReducedBase + IntermediateBase + StandardBase;

    /// <summary>All the VAT on the document.</summary>
    public decimal VatTotal => ReducedVat + IntermediateVat + StandardVat;

    /// <summary>What the customer is asked to pay.</summary>
    public decimal GrossTotal => NetTotal + VatTotal;

    /// <summary>Adds one line's contribution to the running split.</summary>
    /// <param name="rate">The line's VAT rate.</param>
    /// <param name="netAmount">The line's value before VAT.</param>
    /// <param name="vatAmount">The VAT on the line.</param>
    public TaxSummary Add(VatRate rate, Money netAmount, Money vatAmount)
    {
        ArgumentNullException.ThrowIfNull(rate);
        ArgumentNullException.ThrowIfNull(netAmount);
        ArgumentNullException.ThrowIfNull(vatAmount);

        return rate.Category switch
        {
            VatCategory.Exempt => this with { ExemptBase = ExemptBase + netAmount.Amount },
            VatCategory.Reduced => this with
            {
                ReducedBase = ReducedBase + netAmount.Amount,
                ReducedVat = ReducedVat + vatAmount.Amount,
            },
            VatCategory.Intermediate => this with
            {
                IntermediateBase = IntermediateBase + netAmount.Amount,
                IntermediateVat = IntermediateVat + vatAmount.Amount,
            },
            VatCategory.Standard => this with
            {
                StandardBase = StandardBase + netAmount.Amount,
                StandardVat = StandardVat + vatAmount.Amount,
            },
            _ => throw new InvalidOperationException("A line with no VAT category cannot be totalled."),
        };
    }
}

/// <summary>
/// Which Portuguese tax region a document is issued from.
/// <para>
/// The three have different rates for the same category — 23/13/6 on the mainland, 22/12/5 in
/// Madeira, 16/9/4 in the Azores — and the region travels with the document because the rate
/// alone does not say which one it is.
/// </para>
/// </summary>
public enum TaxRegion
{
    /// <summary>Unspecified. Never persisted.</summary>
    Unknown = 0,

    /// <summary>Mainland Portugal. <c>PT</c>.</summary>
    Mainland = 1,

    /// <summary>The Azores. <c>PT-AC</c>.</summary>
    Azores = 2,

    /// <summary>Madeira. <c>PT-MA</c>.</summary>
    Madeira = 3,
}

/// <summary>Turns a <see cref="TaxRegion"/> into the code the tax authority expects.</summary>
public static class TaxRegionCodes
{
    /// <summary>The SAF-T (PT) <c>TaxCountryRegion</c> value.</summary>
    /// <param name="region">The region.</param>
    /// <exception cref="ArgumentOutOfRangeException">The region is <see cref="TaxRegion.Unknown"/>.</exception>
    public static string Code(this TaxRegion region) => region switch
    {
        TaxRegion.Mainland => "PT",
        TaxRegion.Azores => "PT-AC",
        TaxRegion.Madeira => "PT-MA",
        _ => throw new ArgumentOutOfRangeException(nameof(region), region, "No tax region code for that value."),
    };
}
