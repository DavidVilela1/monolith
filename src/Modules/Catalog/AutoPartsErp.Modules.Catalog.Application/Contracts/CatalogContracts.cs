namespace AutoPartsErp.Modules.Catalog.Application.Contracts;

/// <summary>
/// One row in a parts grid or search result.
/// <para>
/// Read models are separate types from the domain model on purpose. The aggregate exists to
/// enforce rules during a change; a grid needs a flat, denormalized row with the brand name
/// already joined in. Keeping them apart means the API surface can stay stable while the
/// domain model evolves, and a wider grid never turns into a wider aggregate.
/// </para>
/// </summary>
public sealed record PartSummary
{
    /// <summary>The part's identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>The distributor's stock keeping unit.</summary>
    public required string Sku { get; init; }

    /// <summary>The manufacturer's number, as printed.</summary>
    public required string ManufacturerPartNumber { get; init; }

    /// <summary>The brand's code.</summary>
    public required string BrandCode { get; init; }

    /// <summary>The brand's display name.</summary>
    public required string BrandName { get; init; }

    /// <summary>The category's display name.</summary>
    public required string CategoryName { get; init; }

    /// <summary>Short description shown on documents.</summary>
    public required string Name { get; init; }

    /// <summary>The unit stock is counted in.</summary>
    public required string StockUnit { get; init; }

    /// <summary>Where the part sits in its commercial life.</summary>
    public required string Status { get; init; }

    /// <summary>True when the part is sold against a returnable core.</summary>
    public required bool RequiresCoreReturn { get; init; }
}

/// <summary>The full picture of one part, as returned by the detail endpoint.</summary>
public sealed record PartDetail
{
    /// <summary>The part's identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>The distributor's stock keeping unit.</summary>
    public required string Sku { get; init; }

    /// <summary>The manufacturer's number, as printed.</summary>
    public required string ManufacturerPartNumber { get; init; }

    /// <summary>The manufacturer's number in searchable form.</summary>
    public required string NormalizedPartNumber { get; init; }

    /// <summary>The brand's identifier.</summary>
    public required Guid BrandId { get; init; }

    /// <summary>The brand's code.</summary>
    public required string BrandCode { get; init; }

    /// <summary>The brand's display name.</summary>
    public required string BrandName { get; init; }

    /// <summary>The category's identifier.</summary>
    public required Guid CategoryId { get; init; }

    /// <summary>The category's display name.</summary>
    public required string CategoryName { get; init; }

    /// <summary>Short description shown on documents.</summary>
    public required string Name { get; init; }

    /// <summary>Long description for the webshop.</summary>
    public string? Description { get; init; }

    /// <summary>The unit stock is counted in.</summary>
    public required string StockUnit { get; init; }

    /// <summary>Where the part sits in its commercial life.</summary>
    public required string Status { get; init; }

    /// <summary>Gross weight of one selling unit, in kilograms.</summary>
    public required decimal WeightKg { get; init; }

    /// <summary>Length in millimetres, when measured.</summary>
    public decimal? LengthMm { get; init; }

    /// <summary>Width in millimetres, when measured.</summary>
    public decimal? WidthMm { get; init; }

    /// <summary>Height in millimetres, when measured.</summary>
    public decimal? HeightMm { get; init; }

    /// <summary>True when dangerous goods rules apply.</summary>
    public required bool IsDangerousGoods { get; init; }

    /// <summary>UN number for dangerous goods.</summary>
    public string? UnNumber { get; init; }

    /// <summary>True when the part is sold against a returnable core.</summary>
    public required bool RequiresCoreReturn { get; init; }

    /// <summary>The refundable core deposit.</summary>
    public decimal? CoreChargeAmount { get; init; }

    /// <summary>Currency of the core deposit.</summary>
    public string? CoreChargeCurrency { get; init; }

    /// <summary>The part that replaces this one, once discontinued.</summary>
    public Guid? SupersededByPartId { get; init; }

    /// <summary>Foreign numbers that resolve to this part.</summary>
    public required IReadOnlyList<CrossReferenceDto> CrossReferences { get; init; }

    /// <summary>Vehicle applications this part fits.</summary>
    public required IReadOnlyList<FitmentDto> Fitments { get; init; }
}

/// <summary>A foreign number linked to a part.</summary>
/// <param name="Kind">Why the numbers are linked.</param>
/// <param name="SourceBrand">Whose number it is, when known.</param>
/// <param name="Number">The number as printed.</param>
/// <param name="NormalizedNumber">The number in searchable form.</param>
/// <param name="Notes">Optional qualifier.</param>
public sealed record CrossReferenceDto(
    string Kind,
    string? SourceBrand,
    string Number,
    string NormalizedNumber,
    string? Notes);

/// <summary>A vehicle application recorded against a part.</summary>
/// <param name="Make">Vehicle manufacturer.</param>
/// <param name="Model">Model designation.</param>
/// <param name="EngineCode">Engine or type code, when it matters.</param>
/// <param name="YearFrom">First model year covered.</param>
/// <param name="YearTo">Last model year covered.</param>
/// <param name="Position">Fitting position, such as FRONT or REAR.</param>
/// <param name="Notes">Optional qualifier.</param>
public sealed record FitmentDto(
    string Make,
    string Model,
    string? EngineCode,
    int YearFrom,
    int YearTo,
    string? Position,
    string? Notes);

/// <summary>A brand, as returned by the brand endpoints.</summary>
/// <param name="Id">The brand's identifier.</param>
/// <param name="Code">Short uppercase code.</param>
/// <param name="Name">Display name.</param>
/// <param name="IsOriginalEquipment">True for vehicle manufacturers and OE suppliers.</param>
/// <param name="IsActive">Whether new parts may be created under this brand.</param>
/// <param name="CountryCode">Optional ISO country code.</param>
/// <param name="PartCount">How many parts currently carry this brand.</param>
public sealed record BrandDto(
    Guid Id,
    string Code,
    string Name,
    bool IsOriginalEquipment,
    bool IsActive,
    string? CountryCode,
    int PartCount);

/// <summary>A category, as returned by the category endpoints.</summary>
/// <param name="Id">The category's identifier.</param>
/// <param name="Code">Short uppercase code.</param>
/// <param name="Name">Display name.</param>
/// <param name="ParentId">The parent category, or null for a top-level group.</param>
/// <param name="SortOrder">Sort order among siblings.</param>
/// <param name="IsActive">Whether new parts may be filed here.</param>
/// <param name="PartCount">How many parts are filed directly under this category.</param>
public sealed record CategoryDto(
    Guid Id,
    string Code,
    string Name,
    Guid? ParentId,
    int SortOrder,
    bool IsActive,
    int PartCount);
