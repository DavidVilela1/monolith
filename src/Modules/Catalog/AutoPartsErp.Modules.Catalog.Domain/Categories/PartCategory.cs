using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Catalog.Domain.Categories;

/// <summary>
/// A node in the product hierarchy: Braking, then Brake Pads, then Brake Pads - Front.
/// <para>
/// The hierarchy is what drives webshop navigation, margin analysis by product group,
/// stock policy by family and the reporting every purchasing decision leans on.
/// It is kept as a simple parent link rather than a nested set: catalogue trees are shallow,
/// read constantly and restructured rarely, so the simpler shape wins.
/// </para>
/// </summary>
public sealed class PartCategory : AggregateRoot<CategoryId>, IAuditable, ISoftDeletable, ITenantScoped
{
    /// <summary>Longest permitted category code.</summary>
    public const int MaxCodeLength = 20;

    /// <summary>Longest permitted category name.</summary>
    public const int MaxNameLength = 120;

    private PartCategory(CategoryId id, string code, string name, CategoryId? parentId)
        : base(id)
    {
        Code = code;
        Name = name;
        ParentId = parentId;
        IsActive = true;
        CreatedBy = string.Empty;
    }

    /// <summary>Required by EF Core materialization.</summary>
#pragma warning disable CS8618
    private PartCategory()
    {
    }
#pragma warning restore CS8618

    /// <summary>Short uppercase code, unique within the tenant.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>Display name.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>The parent category, or null for a top-level group.</summary>
    public CategoryId? ParentId { get; private set; }

    /// <summary>Sort order among siblings. Lower appears first.</summary>
    public int SortOrder { get; private set; }

    /// <summary>Whether new parts may be filed under this category.</summary>
    public bool IsActive { get; private set; }

    /// <summary>True when this is a top-level group.</summary>
    public bool IsRoot => ParentId is null;

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

    /// <summary>Creates a category.</summary>
    /// <param name="code">Short code, uppercased automatically.</param>
    /// <param name="name">Display name.</param>
    /// <param name="parentId">Optional parent category.</param>
    /// <param name="sortOrder">Sort order among siblings.</param>
    public static Result<PartCategory> Create(
        string? code,
        string? name,
        CategoryId? parentId = null,
        int sortOrder = 0)
    {
        Result<string> validatedCode = ValidateCode(code);
        if (validatedCode.IsFailure)
        {
            return Result.Failure<PartCategory>(validatedCode.Error);
        }

        Result<string> validatedName = ValidateName(name);
        if (validatedName.IsFailure)
        {
            return Result.Failure<PartCategory>(validatedName.Error);
        }

        return new PartCategory(CategoryId.New(), validatedCode.Value, validatedName.Value, parentId)
        {
            SortOrder = sortOrder,
        };
    }

    /// <summary>Changes the display name.</summary>
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

    /// <summary>
    /// Moves the category under a different parent.
    /// The caller is responsible for checking that the new parent is not a descendant;
    /// this method only rejects the trivial cycle.
    /// </summary>
    public Result MoveTo(CategoryId? parentId)
    {
        if (parentId is { } parent && parent == Id)
        {
            return CatalogErrors.Category.CannotParentItself;
        }

        ParentId = parentId;
        return Result.Success();
    }

    /// <summary>Changes the sort order among siblings.</summary>
    public void Reorder(int sortOrder) => SortOrder = sortOrder;

    /// <summary>Stops new parts being filed here.</summary>
    public void Deactivate() => IsActive = false;

    /// <summary>Allows new parts here again.</summary>
    public void Reactivate() => IsActive = true;

    private static Result<string> ValidateCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return CatalogErrors.Category.CodeRequired;
        }

        string normalized = code.Trim().ToUpperInvariant();

        return normalized.Length > MaxCodeLength
            ? CatalogErrors.Category.CodeTooLong
            : normalized;
    }

    private static Result<string> ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return CatalogErrors.Category.NameRequired;
        }

        string trimmed = name.Trim();

        return trimmed.Length > MaxNameLength
            ? CatalogErrors.Category.NameTooLong
            : trimmed;
    }
}
