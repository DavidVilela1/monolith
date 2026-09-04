using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Invoicing.Domain.Invoices;

/// <summary>
/// Which of the four VAT categories a line falls into.
/// <para>
/// Not the percentage — the category. The percentages move (they have three times in twenty
/// years, and they differ between the mainland, Madeira and the Azores) but the categories are
/// fixed, and it is the category that decides which pair of fields a line's totals land in inside
/// the QR code and the SAF-T export.
/// </para>
/// </summary>
public enum VatCategory
{
    /// <summary>Unspecified. Never persisted.</summary>
    Unknown = 0,

    /// <summary><c>ISE</c> — exempt. Requires a reason, by law.</summary>
    Exempt = 1,

    /// <summary><c>RED</c> — the reduced rate. 6% on the mainland.</summary>
    Reduced = 2,

    /// <summary><c>INT</c> — the intermediate rate. 13% on the mainland.</summary>
    Intermediate = 3,

    /// <summary><c>NOR</c> — the standard rate. 23% on the mainland, and what a brake pad is.</summary>
    Standard = 4,
}

/// <summary>
/// A VAT rate as it goes onto a document: the category, the percentage, and — when exempt — why.
/// <para>
/// The exemption reason is not optional politeness. An exempt line without a stated legal basis
/// is a rejected SAF-T file and, if it reaches an inspection, an assessment for the VAT that was
/// not charged. So the type refuses to exist without one.
/// </para>
/// </summary>
public sealed class VatRate : ValueObject
{
    /// <summary>Longest permitted exemption reason.</summary>
    public const int MaxExemptionReasonLength = 60;

    /// <summary>Longest permitted exemption code, e.g. <c>M07</c>.</summary>
    public const int MaxExemptionCodeLength = 10;

    private VatRate(VatCategory category, decimal percent, string? exemptionCode, string? exemptionReason)
    {
        Category = category;
        Percent = percent;
        ExemptionCode = exemptionCode;
        ExemptionReason = exemptionReason;
    }

    /// <summary>
    /// Required by object-relational mappers that materialize this type as an owned value.
    /// Domain code always goes through <see cref="Of"/> or <see cref="ExemptWith"/>.
    /// </summary>
#pragma warning disable CS8618
    private VatRate()
    {
    }
#pragma warning restore CS8618

    /// <summary>Which category the line falls into.</summary>
    public VatCategory Category { get; }

    /// <summary>The percentage applied. Zero for an exempt line.</summary>
    public decimal Percent { get; }

    /// <summary>The AT exemption code, e.g. <c>M07</c>. Null unless exempt.</summary>
    public string? ExemptionCode { get; }

    /// <summary>The legal basis for the exemption, as it must be printed. Null unless exempt.</summary>
    public string? ExemptionReason { get; }

    /// <summary>True when no VAT is charged and a reason has to travel with the line.</summary>
    public bool IsExempt => Category == VatCategory.Exempt;

    /// <summary>The SAF-T (PT) code for the category, e.g. <c>NOR</c>.</summary>
    public string TaxCode => Category switch
    {
        VatCategory.Exempt => "ISE",
        VatCategory.Reduced => "RED",
        VatCategory.Intermediate => "INT",
        VatCategory.Standard => "NOR",
        _ => throw new InvalidOperationException("A VAT rate with no category cannot be reported."),
    };

    /// <summary>The mainland standard rate, which is what almost every part is sold at.</summary>
    public static VatRate PortugalStandard => new(VatCategory.Standard, 23m, null, null);

    /// <summary>Creates a rate that charges VAT.</summary>
    /// <param name="category">Reduced, intermediate or standard.</param>
    /// <param name="percent">The percentage, 0 to 100.</param>
    public static Result<VatRate> Of(VatCategory category, decimal percent)
    {
        if (category == VatCategory.Unknown)
        {
            return InvoicingErrors.Vat.CategoryRequired;
        }

        if (category == VatCategory.Exempt)
        {
            return InvoicingErrors.Vat.ExemptNeedsReason;
        }

        if (percent is < 0m or > 100m)
        {
            return InvoicingErrors.Vat.PercentOutOfRange;
        }

        // A rated category with a zero percentage is somebody meaning "exempt" and reaching for
        // the wrong constructor. It would sail through every arithmetic check and land in the
        // wrong pair of QR fields, where nothing would ever notice it.
        return percent == 0m
            ? InvoicingErrors.Vat.RatedCategoryNeedsPercent
            : new VatRate(category, percent, null, null);
    }

    /// <summary>Creates an exempt rate, with the legal basis that has to accompany it.</summary>
    /// <param name="exemptionCode">The AT code, e.g. <c>M07</c>.</param>
    /// <param name="exemptionReason">The wording printed on the document.</param>
    public static Result<VatRate> ExemptWith(string? exemptionCode, string? exemptionReason)
    {
        if (string.IsNullOrWhiteSpace(exemptionCode))
        {
            return InvoicingErrors.Vat.ExemptionCodeRequired;
        }

        if (string.IsNullOrWhiteSpace(exemptionReason))
        {
            return InvoicingErrors.Vat.ExemptNeedsReason;
        }

        string code = exemptionCode.Trim().ToUpperInvariant();
        string reason = exemptionReason.Trim();

        if (code.Length > MaxExemptionCodeLength)
        {
            return InvoicingErrors.Vat.ExemptionCodeTooLong;
        }

        return reason.Length > MaxExemptionReasonLength
            ? InvoicingErrors.Vat.ExemptionReasonTooLong
            : new VatRate(VatCategory.Exempt, 0m, code, reason);
    }

    /// <inheritdoc />
    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Category;
        yield return Percent;
        yield return ExemptionCode;
    }

    /// <inheritdoc />
    public override string ToString() => IsExempt ? $"{TaxCode} ({ExemptionCode})" : $"{TaxCode} {Percent}%";
}
