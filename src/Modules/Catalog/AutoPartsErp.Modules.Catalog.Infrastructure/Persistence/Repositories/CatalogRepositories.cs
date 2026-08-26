using AutoPartsErp.Modules.Catalog.Domain;
using AutoPartsErp.Modules.Catalog.Domain.Brands;
using AutoPartsErp.Modules.Catalog.Domain.Categories;
using AutoPartsErp.Modules.Catalog.Domain.Parts;
using Microsoft.EntityFrameworkCore;

namespace AutoPartsErp.Modules.Catalog.Infrastructure.Persistence.Repositories;

/// <summary>
/// Write-side access to parts, backed by EF Core change tracking.
/// <para>
/// Every method here loads a whole aggregate so that its behaviour can run. Nothing here
/// returns projections or lists: those belong to <c>CatalogReadStore</c>, which does not track.
/// </para>
/// </summary>
public sealed class PartRepository : IPartRepository
{
    private readonly CatalogDbContext _context;

    /// <summary>Initializes the repository.</summary>
    public PartRepository(CatalogDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public Task<Part?> GetByIdAsync(PartId id, CancellationToken cancellationToken = default) =>
        _context.Parts.FirstOrDefaultAsync(part => part.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsAsync(PartId id, CancellationToken cancellationToken = default) =>
        _context.Parts.AnyAsync(part => part.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<Part?> GetBySkuAsync(Sku sku, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sku);
        return _context.Parts.FirstOrDefaultAsync(part => part.Sku == sku, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> SkuExistsAsync(
        Sku sku,
        PartId? excludingPartId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sku);

        IQueryable<Part> query = _context.Parts.Where(part => part.Sku == sku);

        if (excludingPartId is { } excluded)
        {
            query = query.Where(part => part.Id != excluded);
        }

        return query.AnyAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> ManufacturerPartNumberExistsAsync(
        BrandId brandId,
        string normalizedPartNumber,
        PartId? excludingPartId = null,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Part> query = _context.Parts.Where(part =>
            part.BrandId == brandId
            && part.ManufacturerPartNumber.Normalized == normalizedPartNumber);

        if (excludingPartId is { } excluded)
        {
            query = query.Where(part => part.Id != excluded);
        }

        return query.AnyAsync(cancellationToken);
    }

    /// <inheritdoc />
    public void Add(Part aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.Parts.Add(aggregate);
    }

    /// <inheritdoc />
    public void Remove(Part aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);

        // The auditing interceptor turns this into an archival update, never a physical DELETE.
        _context.Parts.Remove(aggregate);
    }
}

/// <summary>Write-side access to brands.</summary>
public sealed class BrandRepository : IBrandRepository
{
    private readonly CatalogDbContext _context;

    /// <summary>Initializes the repository.</summary>
    public BrandRepository(CatalogDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public Task<Brand?> GetByIdAsync(BrandId id, CancellationToken cancellationToken = default) =>
        _context.Brands.FirstOrDefaultAsync(brand => brand.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsAsync(BrandId id, CancellationToken cancellationToken = default) =>
        _context.Brands.AnyAsync(brand => brand.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<Brand?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        string normalized = code?.Trim().ToUpperInvariant() ?? string.Empty;
        return _context.Brands.FirstOrDefaultAsync(brand => brand.Code == normalized, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> CodeExistsAsync(
        string code,
        BrandId? excludingBrandId = null,
        CancellationToken cancellationToken = default)
    {
        string normalized = code?.Trim().ToUpperInvariant() ?? string.Empty;

        IQueryable<Brand> query = _context.Brands.Where(brand => brand.Code == normalized);

        if (excludingBrandId is { } excluded)
        {
            query = query.Where(brand => brand.Id != excluded);
        }

        return query.AnyAsync(cancellationToken);
    }

    /// <inheritdoc />
    public void Add(Brand aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.Brands.Add(aggregate);
    }

    /// <inheritdoc />
    public void Remove(Brand aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.Brands.Remove(aggregate);
    }
}

/// <summary>Write-side access to categories.</summary>
public sealed class CategoryRepository : ICategoryRepository
{
    private const int MaxTreeDepth = 32;

    private readonly CatalogDbContext _context;

    /// <summary>Initializes the repository.</summary>
    public CategoryRepository(CatalogDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public Task<PartCategory?> GetByIdAsync(CategoryId id, CancellationToken cancellationToken = default) =>
        _context.Categories.FirstOrDefaultAsync(category => category.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsAsync(CategoryId id, CancellationToken cancellationToken = default) =>
        _context.Categories.AnyAsync(category => category.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<PartCategory?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        string normalized = code?.Trim().ToUpperInvariant() ?? string.Empty;
        return _context.Categories.FirstOrDefaultAsync(
            category => category.Code == normalized, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> CodeExistsAsync(
        string code,
        CategoryId? excludingCategoryId = null,
        CancellationToken cancellationToken = default)
    {
        string normalized = code?.Trim().ToUpperInvariant() ?? string.Empty;

        IQueryable<PartCategory> query = _context.Categories.Where(category => category.Code == normalized);

        if (excludingCategoryId is { } excluded)
        {
            query = query.Where(category => category.Id != excluded);
        }

        return query.AnyAsync(cancellationToken);
    }

    /// <summary>
    /// Walks up from the candidate parent looking for the category being moved.
    /// Catalogue trees are a handful of levels deep, so a bounded walk is cheaper and far
    /// easier to read than a recursive CTE; the depth cap stops corrupt data spinning forever.
    /// </summary>
    public async Task<bool> IsDescendantAsync(
        CategoryId categoryId,
        CategoryId candidateParentId,
        CancellationToken cancellationToken = default)
    {
        CategoryId? current = candidateParentId;

        for (int depth = 0; depth < MaxTreeDepth && current is { } cursor; depth++)
        {
            if (cursor == categoryId)
            {
                return true;
            }

            current = await _context.Categories
                .Where(category => category.Id == cursor)
                .Select(category => category.ParentId)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        return false;
    }

    /// <inheritdoc />
    public void Add(PartCategory aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.Categories.Add(aggregate);
    }

    /// <inheritdoc />
    public void Remove(PartCategory aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.Categories.Remove(aggregate);
    }
}
