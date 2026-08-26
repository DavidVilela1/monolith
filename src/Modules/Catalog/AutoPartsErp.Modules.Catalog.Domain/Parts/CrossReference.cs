using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Catalog.Domain.Parts;

/// <summary>Why one part number points at another.</summary>
public enum CrossReferenceKind
{
    /// <summary>Unspecified. Never persisted.</summary>
    Unknown = 0,

    /// <summary>The vehicle manufacturer's own number for this part.</summary>
    Oem = 1,

    /// <summary>The equivalent part from a competing aftermarket brand.</summary>
    Competitor = 2,

    /// <summary>An older number this part replaces.</summary>
    Supersedes = 3,

    /// <summary>A number that can be used interchangeably without a formal supersession.</summary>
    Interchange = 4,

    /// <summary>The number a trading partner uses for this part in EDI or price files.</summary>
    TradingPartner = 5,
}

/// <summary>
/// A link from some other party's number to this part.
/// <para>
/// This is the second half of what a parts distributor sells. A mechanic reads the number off
/// the old part, which is almost always the OEM number, and expects the counter to find the
/// aftermarket equivalent on the shelf. Without a cross-reference table that lookup fails and
/// the sale goes to whoever has one.
/// </para>
/// <para>
/// The number is held as two flat strings rather than a nested <see cref="PartNumber"/> so that
/// it maps to two indexed columns with no nesting. <see cref="NormalizedNumber"/> is the one
/// every lookup uses; <see cref="Number"/> is what gets printed.
/// </para>
/// </summary>
public sealed class CrossReference : ValueObject
{
    private CrossReference(
        CrossReferenceKind kind,
        string? sourceBrand,
        string number,
        string normalizedNumber,
        string? notes)
    {
        Kind = kind;
        SourceBrand = sourceBrand;
        Number = number;
        NormalizedNumber = normalizedNumber;
        Notes = notes;
    }

    /// <summary>Required by EF Core materialization.</summary>
#pragma warning disable CS8618 // EF Core assigns every property during materialization.
    private CrossReference()
    {
    }
#pragma warning restore CS8618

    /// <summary>Why the numbers are linked.</summary>
    public CrossReferenceKind Kind { get; }

    /// <summary>
    /// Whose number this is: VW, BOSCH, FEBI. Null when the source is not known, which happens
    /// often with numbers taken off a customer's old part.
    /// </summary>
    public string? SourceBrand { get; }

    /// <summary>The foreign number as printed, including spaces and separators.</summary>
    public string Number { get; } = string.Empty;

    /// <summary>Uppercase, letters and digits only. Every lookup and index uses this.</summary>
    public string NormalizedNumber { get; } = string.Empty;

    /// <summary>Optional qualifier, for example "up to 05/2012".</summary>
    public string? Notes { get; }

    /// <summary>Creates a cross-reference.</summary>
    /// <param name="kind">Why the numbers are linked.</param>
    /// <param name="number">The foreign number, as printed.</param>
    /// <param name="sourceBrand">Optional owner of the foreign number.</param>
    /// <param name="notes">Optional qualifier.</param>
    public static Result<CrossReference> Create(
        CrossReferenceKind kind,
        string? number,
        string? sourceBrand = null,
        string? notes = null)
    {
        if (kind == CrossReferenceKind.Unknown)
        {
            return CatalogErrors.CrossReference.KindRequired;
        }

        Result<PartNumber> partNumber = PartNumber.Create(number);
        if (partNumber.IsFailure)
        {
            return Result.Failure<CrossReference>(partNumber.Error);
        }

        string? brand = string.IsNullOrWhiteSpace(sourceBrand)
            ? null
            : sourceBrand.Trim().ToUpperInvariant();

        return new CrossReference(
            kind,
            brand,
            partNumber.Value.Display,
            partNumber.Value.Normalized,
            string.IsNullOrWhiteSpace(notes) ? null : notes.Trim());
    }

    /// <summary>Rehydrates a cross-reference already known to be valid.</summary>
    public static CrossReference FromStorage(
        CrossReferenceKind kind,
        string? sourceBrand,
        string number,
        string normalizedNumber,
        string? notes) =>
        new(kind, sourceBrand, number, normalizedNumber, notes);

    /// <summary>The number as a <see cref="PartNumber"/>, for code that works in those terms.</summary>
    public PartNumber AsPartNumber() => PartNumber.FromStorage(Number, NormalizedNumber);

    /// <summary>
    /// True when two cross-references say the same thing. Two brands can legitimately use the
    /// same digits, so the source brand is part of the comparison.
    /// </summary>
    public bool IsSameReferenceAs(CrossReference other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return Kind == other.Kind
            && string.Equals(SourceBrand, other.SourceBrand, StringComparison.Ordinal)
            && string.Equals(NormalizedNumber, other.NormalizedNumber, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Kind;
        yield return SourceBrand;
        yield return NormalizedNumber;
        yield return Notes;
    }

    /// <inheritdoc />
    public override string ToString() =>
        SourceBrand is null ? $"{Kind}: {Number}" : $"{Kind}: {SourceBrand} {Number}";
}
