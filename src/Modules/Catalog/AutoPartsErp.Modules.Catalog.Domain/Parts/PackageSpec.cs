using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Catalog.Domain.Parts;

/// <summary>
/// Physical shipping characteristics of one selling unit.
/// <para>
/// Weight and dimensions are not cosmetic catalogue data: they drive carrier rating, pallet
/// building, bin assignment in the warehouse and the dangerous-goods paperwork that has to
/// travel with brake fluid and airbags. Capturing them on the part is what lets Sales quote a
/// delivery charge that matches what the carrier eventually invoices.
/// </para>
/// </summary>
public sealed class PackageSpec : ValueObject
{
    private PackageSpec(
        decimal weightKg,
        decimal? lengthMm,
        decimal? widthMm,
        decimal? heightMm,
        bool isDangerousGoods,
        string? unNumber)
    {
        WeightKg = weightKg;
        LengthMm = lengthMm;
        WidthMm = widthMm;
        HeightMm = heightMm;
        IsDangerousGoods = isDangerousGoods;
        UnNumber = unNumber;
    }

    /// <summary>
    /// Required by EF Core, which maps this value object as an owned type and writes the
    /// backing fields directly. Domain code always goes through <see cref="Create"/>.
    /// </summary>
    private PackageSpec()
    {
    }

    /// <summary>An unmeasured part. Perfectly valid while a part is still being set up.</summary>
    public static PackageSpec Unspecified { get; } =
        new(0m, null, null, null, isDangerousGoods: false, unNumber: null);

    /// <summary>Gross weight of one selling unit, in kilograms.</summary>
    public decimal WeightKg { get; }

    /// <summary>Longest dimension in millimetres, if measured.</summary>
    public decimal? LengthMm { get; }

    /// <summary>Width in millimetres, if measured.</summary>
    public decimal? WidthMm { get; }

    /// <summary>Height in millimetres, if measured.</summary>
    public decimal? HeightMm { get; }

    /// <summary>
    /// True for goods that fall under dangerous goods rules: brake fluid, batteries,
    /// airbags and seat belt pretensioners, aerosols, paint.
    /// </summary>
    public bool IsDangerousGoods { get; }

    /// <summary>The UN number for dangerous goods, for example UN1263 for paint.</summary>
    public string? UnNumber { get; }

    /// <summary>True when all three dimensions are known.</summary>
    public bool HasDimensions => LengthMm.HasValue && WidthMm.HasValue && HeightMm.HasValue;

    /// <summary>Volume in cubic metres, or null when the part has not been measured.</summary>
    public decimal? VolumeM3 => HasDimensions
        ? LengthMm!.Value * WidthMm!.Value * HeightMm!.Value / 1_000_000_000m
        : null;

    /// <summary>Creates a package specification.</summary>
    /// <param name="weightKg">Gross weight in kilograms. Must not be negative.</param>
    /// <param name="lengthMm">Optional length in millimetres.</param>
    /// <param name="widthMm">Optional width in millimetres.</param>
    /// <param name="heightMm">Optional height in millimetres.</param>
    /// <param name="isDangerousGoods">Whether dangerous goods rules apply.</param>
    /// <param name="unNumber">UN number, required when <paramref name="isDangerousGoods"/> is true.</param>
    public static Result<PackageSpec> Create(
        decimal weightKg,
        decimal? lengthMm = null,
        decimal? widthMm = null,
        decimal? heightMm = null,
        bool isDangerousGoods = false,
        string? unNumber = null)
    {
        if (weightKg < 0m)
        {
            return CatalogErrors.Part.WeightNegative;
        }

        if (lengthMm is < 0m || widthMm is < 0m || heightMm is < 0m)
        {
            return CatalogErrors.Part.DimensionNegative;
        }

        string? un = string.IsNullOrWhiteSpace(unNumber) ? null : unNumber.Trim().ToUpperInvariant();

        if (isDangerousGoods && un is null)
        {
            return CatalogErrors.Part.DangerousGoodsNeedUnNumber;
        }

        return new PackageSpec(weightKg, lengthMm, widthMm, heightMm, isDangerousGoods, un);
    }

    /// <summary>Rehydrates a package specification already known to be valid.</summary>
    public static PackageSpec FromStorage(
        decimal weightKg,
        decimal? lengthMm,
        decimal? widthMm,
        decimal? heightMm,
        bool isDangerousGoods,
        string? unNumber) =>
        new(weightKg, lengthMm, widthMm, heightMm, isDangerousGoods, unNumber);

    /// <inheritdoc />
    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return WeightKg;
        yield return LengthMm;
        yield return WidthMm;
        yield return HeightMm;
        yield return IsDangerousGoods;
        yield return UnNumber;
    }
}
