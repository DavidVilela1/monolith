using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Catalog.Domain.Brands;

/// <summary>
/// Who makes the part.
/// <para>
/// A distributor's brand list mixes two different things, and the difference matters
/// commercially. <see cref="IsOriginalEquipment"/> marks vehicle manufacturers and their
/// original-equipment suppliers, whose numbers customers quote from dealer systems and
/// workshop manuals. Everything else is an aftermarket brand, which is what the distributor
/// actually stocks and sells. Cross-references almost always run from the first group to the
/// second, and margin lives in that translation.
/// </para>
/// </summary>
public sealed class Brand : AggregateRoot<BrandId>, IAuditable, ISoftDeletable, ITenantScoped
{
    /// <summary>Longest permitted brand code.</summary>
    public const int MaxCodeLength = 20;

    /// <summary>Longest permitted brand name.</summary>
    public const int MaxNameLength = 120;

    private Brand(BrandId id, string code, string name, bool isOriginalEquipment)
        : base(id)
    {
        Code = code;
        Name = name;
        IsOriginalEquipment = isOriginalEquipment;
        IsActive = true;
        CreatedBy = string.Empty;
    }

    /// <summary>Required by EF Core materialization.</summary>
#pragma warning disable CS8618
    private Brand()
    {
    }
#pragma warning restore CS8618

    /// <summary>Short uppercase code used on documents and in imports: BOSCH, VW, FEBI.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>Full brand name as it should be printed.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>True for vehicle manufacturers and their original-equipment suppliers.</summary>
    public bool IsOriginalEquipment { get; private set; }

    /// <summary>Whether new parts may be created under this brand.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Optional country of origin, useful for customs and marketing.</summary>
    public string? CountryCode { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <inheritdoc />
    public string CreatedBy { get; set; } = string.Empty;

    /// <inheritdoc />
    public DateTimeOffset? ModifiedAtUtc { get; set; }

    /// <inheritdoc />
    public string? ModifiedBy { get; set; }

    /// <inheritdoc />
    public bool IsDeleted { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? DeletedAtUtc { get; set; }

    /// <inheritdoc />
    public string? DeletedBy { get; set; }

    /// <summary>Registers a new brand.</summary>
    /// <param name="code">Short code, uppercased automatically.</param>
    /// <param name="name">Full brand name.</param>
    /// <param name="isOriginalEquipment">True for vehicle manufacturers and OE suppliers.</param>
    /// <param name="countryCode">Optional ISO country code.</param>
    public static Result<Brand> Create(
        string? code,
        string? name,
        bool isOriginalEquipment = false,
        string? countryCode = null)
    {
        Result<string> validatedCode = ValidateCode(code);
        if (validatedCode.IsFailure)
        {
            return Result.Failure<Brand>(validatedCode.Error);
        }

        Result<string> validatedName = ValidateName(name);
        if (validatedName.IsFailure)
        {
            return Result.Failure<Brand>(validatedName.Error);
        }

        return new Brand(BrandId.New(), validatedCode.Value, validatedName.Value, isOriginalEquipment)
        {
            CountryCode = string.IsNullOrWhiteSpace(countryCode)
                ? null
                : countryCode.Trim().ToUpperInvariant(),
        };
    }

    /// <summary>Changes the printed name of the brand.</summary>
    public Result Rename(string? name)
    {
        Result<string> validatedName = ValidateName(name);
        if (validatedName.IsFailure)
        {
            return Result.Failure(validatedName.Error);
        }

        Name = validatedName.Value;
        return Result.Success();
    }

    /// <summary>Stops new parts being created under this brand. Existing parts are untouched.</summary>
    public void Deactivate() => IsActive = false;

    /// <summary>Allows new parts under this brand again.</summary>
    public void Reactivate() => IsActive = true;

    private static Result<string> ValidateCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return CatalogErrors.Brand.CodeRequired;
        }

        string normalized = code.Trim().ToUpperInvariant();

        return normalized.Length > MaxCodeLength
            ? CatalogErrors.Brand.CodeTooLong
            : normalized;
    }

    private static Result<string> ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return CatalogErrors.Brand.NameRequired;
        }

        string trimmed = name.Trim();

        return trimmed.Length > MaxNameLength
            ? CatalogErrors.Brand.NameTooLong
            : trimmed;
    }
}
