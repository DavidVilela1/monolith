using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Catalog.Domain.Parts;

/// <summary>
/// A vehicle application: the statement that this part fits this vehicle.
/// <para>
/// Fitment is the heart of parts distribution. A customer almost never walks in asking for
/// part <c>BP-1188</c>; they ask for front brake pads for a 2014 Golf 2.0 TDI. Everything the
/// counter, the webshop and the returns desk do is driven by this relationship, and getting it
/// wrong is the most expensive mistake in the business: wrong-fit parts come back, often fitted.
/// </para>
/// <para>
/// This is a deliberately flat first cut. The industry standard shapes are ACES/PIES in North
/// America and TecDoc in Europe, both of which model a normalized vehicle tree (make to model
/// to type to engine) with thousands of qualifier types. Growing into one of those is a planned
/// step; encoding make, model, engine and a year range now keeps the aggregate honest and gives
/// the search something real to work against.
/// </para>
/// </summary>
public sealed class Fitment : ValueObject
{
    /// <summary>The earliest model year the system accepts.</summary>
    public const int EarliestYear = 1900;

    private Fitment(
        string make,
        string model,
        string? engineCode,
        int yearFrom,
        int yearTo,
        string? position,
        string? notes)
    {
        Make = make;
        Model = model;
        EngineCode = engineCode;
        YearFrom = yearFrom;
        YearTo = yearTo;
        Position = position;
        Notes = notes;
    }

    /// <summary>
    /// Required by EF Core, which maps fitments as an owned collection and writes the
    /// backing fields directly. Domain code always goes through <see cref="Create"/>.
    /// </summary>
#pragma warning disable CS8618
    private Fitment()
    {
    }
#pragma warning restore CS8618

    /// <summary>Vehicle manufacturer, uppercased: VOLKSWAGEN, BMW, RENAULT.</summary>
    public string Make { get; } = string.Empty;

    /// <summary>Model designation, uppercased: GOLF VII, 3 SERIES F30.</summary>
    public string Model { get; } = string.Empty;

    /// <summary>Engine or type code where it matters, for example CJAA or N47D20.</summary>
    public string? EngineCode { get; }

    /// <summary>First model year covered, inclusive.</summary>
    public int YearFrom { get; }

    /// <summary>Last model year covered, inclusive.</summary>
    public int YearTo { get; }

    /// <summary>
    /// Where on the vehicle the part goes: FRONT, REAR, FRONT LEFT, and so on.
    /// The most common source of wrong-fit returns after the vehicle itself.
    /// </summary>
    public string? Position { get; }

    /// <summary>Free-text qualifier, for example "with sport chassis" or "to chassis no. 1K-8W-123456".</summary>
    public string? Notes { get; }

    /// <summary>Creates a fitment, validating the year range.</summary>
    /// <param name="make">Vehicle manufacturer.</param>
    /// <param name="model">Model designation.</param>
    /// <param name="yearFrom">First model year, inclusive.</param>
    /// <param name="yearTo">Last model year, inclusive.</param>
    /// <param name="engineCode">Optional engine or type code.</param>
    /// <param name="position">Optional fitting position.</param>
    /// <param name="notes">Optional qualifier.</param>
    public static Result<Fitment> Create(
        string? make,
        string? model,
        int yearFrom,
        int yearTo,
        string? engineCode = null,
        string? position = null,
        string? notes = null)
    {
        if (string.IsNullOrWhiteSpace(make))
        {
            return CatalogErrors.Fitment.MakeRequired;
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            return CatalogErrors.Fitment.ModelRequired;
        }

        int maxYear = DateTime.UtcNow.Year + 2; // Model years run ahead of the calendar.

        if (yearFrom < EarliestYear || yearFrom > maxYear)
        {
            return CatalogErrors.Fitment.YearOutOfRange;
        }

        if (yearTo < EarliestYear || yearTo > maxYear)
        {
            return CatalogErrors.Fitment.YearOutOfRange;
        }

        if (yearTo < yearFrom)
        {
            return CatalogErrors.Fitment.YearRangeInverted;
        }

        return new Fitment(
            make.Trim().ToUpperInvariant(),
            model.Trim().ToUpperInvariant(),
            Clean(engineCode)?.ToUpperInvariant(),
            yearFrom,
            yearTo,
            Clean(position)?.ToUpperInvariant(),
            Clean(notes));
    }

    /// <summary>Rehydrates a fitment already known to be valid.</summary>
    public static Fitment FromStorage(
        string make,
        string model,
        string? engineCode,
        int yearFrom,
        int yearTo,
        string? position,
        string? notes) =>
        new(make, model, engineCode, yearFrom, yearTo, position, notes);

    /// <summary>True when this fitment covers the supplied model year.</summary>
    public bool CoversYear(int year) => year >= YearFrom && year <= YearTo;

    /// <summary>
    /// True when two fitments describe the same vehicle application and therefore must not
    /// both be recorded against one part.
    /// </summary>
    public bool DescribesSameApplicationAs(Fitment other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return string.Equals(Make, other.Make, StringComparison.Ordinal)
            && string.Equals(Model, other.Model, StringComparison.Ordinal)
            && string.Equals(EngineCode, other.EngineCode, StringComparison.Ordinal)
            && string.Equals(Position, other.Position, StringComparison.Ordinal)
            && YearFrom == other.YearFrom
            && YearTo == other.YearTo;
    }

    /// <inheritdoc />
    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Make;
        yield return Model;
        yield return EngineCode;
        yield return YearFrom;
        yield return YearTo;
        yield return Position;
        yield return Notes;
    }

    /// <inheritdoc />
    public override string ToString()
    {
        string years = YearFrom == YearTo ? YearFrom.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : $"{YearFrom}-{YearTo}";
        string engine = EngineCode is null ? string.Empty : $" {EngineCode}";
        string position = Position is null ? string.Empty : $" [{Position}]";
        return $"{Make} {Model}{engine} ({years}){position}";
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
