using AutoPartsErp.Modules.Catalog.Domain.Brands;
using AutoPartsErp.Modules.Catalog.Domain.Categories;
using AutoPartsErp.Modules.Catalog.Domain.Parts;
using AutoPartsErp.SharedKernel.Abstractions;

namespace AutoPartsErp.Modules.Catalog.Domain;

/// <summary>
/// Write-side access to parts.
/// <para>
/// Note what is missing: there is no <c>Search</c> and no <c>GetAll</c>. Repositories load whole
/// aggregates so that behaviour can run against them, and loading a page of full aggregates to
/// render a grid is exactly the pattern that makes ERP screens slow. Read paths live in the
/// application layer and project straight to DTOs.
/// </para>
/// </summary>
public interface IPartRepository : IRepository<Part, PartId>
{
    /// <summary>Loads a part by SKU, or null when there is no such part.</summary>
    Task<Part?> GetBySkuAsync(Sku sku, CancellationToken cancellationToken = default);

    /// <summary>True when the SKU is already taken.</summary>
    /// <param name="sku">The SKU to check.</param>
    /// <param name="excludingPartId">A part to ignore, used when renaming an existing part.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> SkuExistsAsync(
        Sku sku,
        PartId? excludingPartId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// True when this brand already has a part carrying this manufacturer part number.
    /// The check runs against the normalized form, so spacing differences cannot create a duplicate.
    /// </summary>
    Task<bool> ManufacturerPartNumberExistsAsync(
        BrandId brandId,
        string normalizedPartNumber,
        PartId? excludingPartId = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The Catalog module's unit of work. Each module gets its own so that a handler cannot
/// accidentally commit another module's pending changes in the same transaction.
/// </summary>
public interface ICatalogUnitOfWork : IUnitOfWork;

/// <summary>Write-side access to brands.</summary>
public interface IBrandRepository : IRepository<Brand, BrandId>
{
    /// <summary>Loads a brand by its code, or null when there is no such brand.</summary>
    Task<Brand?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>True when the brand code is already taken.</summary>
    Task<bool> CodeExistsAsync(
        string code,
        BrandId? excludingBrandId = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Write-side access to categories.</summary>
public interface ICategoryRepository : IRepository<PartCategory, CategoryId>
{
    /// <summary>Loads a category by its code, or null when there is no such category.</summary>
    Task<PartCategory?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>True when the category code is already taken.</summary>
    Task<bool> CodeExistsAsync(
        string code,
        CategoryId? excludingCategoryId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// True when <paramref name="candidateParentId"/> sits somewhere below
    /// <paramref name="categoryId"/> in the tree. Used to stop a move creating a cycle.
    /// </summary>
    Task<bool> IsDescendantAsync(
        CategoryId categoryId,
        CategoryId candidateParentId,
        CancellationToken cancellationToken = default);
}
