using AutoPartsErp.Modules.Catalog.Domain;
using AutoPartsErp.Modules.Catalog.Application.Abstractions;
using AutoPartsErp.Modules.Catalog.Application.Contracts;
using AutoPartsErp.Modules.Catalog.Domain.Brands;
using AutoPartsErp.Modules.Catalog.Domain.Categories;
using AutoPartsErp.Modules.Catalog.Domain.Parts;
using AutoPartsErp.SharedKernel.Paging;
using AutoPartsErp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace AutoPartsErp.Modules.Catalog.Infrastructure.Persistence.ReadStore;

/// <summary>
/// Serves the Catalog module's queries.
/// <para>
/// Everything here runs <c>AsNoTracking</c>. Each method issues one query that joins in the brand
/// and category names the UI needs, then shapes the rows into DTOs.
/// </para>
/// <para>
/// Value objects that are stored through a converter (the SKU, the unit of measure, the status)
/// are selected whole and unwrapped once the rows are in memory. Reaching inside a converted
/// property in a LINQ expression is not translatable, and discovering that at runtime in a
/// half-built ERP is a bad afternoon; keeping the SQL projection to plain columns avoids it
/// entirely and costs nothing, because the shaping happens on one page of rows.
/// </para>
/// </summary>
public sealed class CatalogReadStore : ICatalogReadStore
{
    private readonly CatalogDbContext _context;

    /// <summary>Initializes the read store.</summary>
    public CatalogReadStore(CatalogDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<PartDetail?> GetPartAsync(Guid partId, CancellationToken cancellationToken = default)
    {
        var id = new PartId(partId);

        PartRow? row = await SelectRows(_context.Parts.Where(part => part.Id == id))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return row is null ? null : MapDetail(row);
    }

    /// <inheritdoc />
    public async Task<PartDetail?> GetPartBySkuAsync(string sku, CancellationToken cancellationToken = default)
    {
        Result<Sku> parsed = Sku.Create(sku);
        if (parsed.IsFailure)
        {
            return null;
        }

        Sku value = parsed.Value;

        PartRow? row = await SelectRows(_context.Parts.Where(part => part.Sku == value))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return row is null ? null : MapDetail(row);
    }

    /// <inheritdoc />
    public async Task<PagedResult<PartSummary>> SearchPartsAsync(
        PartSearchCriteria criteria,
        PageRequest page,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        ArgumentNullException.ThrowIfNull(page);

        IQueryable<Part> query = _context.Parts.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(criteria.Term))
        {
            string term = criteria.Term.Trim();
            string normalized = PartNumber.Normalize(term);

            // How a counter clerk actually searches: a number off the old part in any
            // spelling, or a couple of words from the description.
            if (normalized.Length > 0)
            {
                string prefix = normalized + "%";

                query = query.Where(part =>
                    EF.Functions.Like(part.ManufacturerPartNumber.Normalized, prefix)
                    || part.CrossReferences.Any(reference =>
                        EF.Functions.Like(reference.NormalizedNumber, prefix))
                    || EF.Functions.ILike(part.Name, $"%{term}%"));
            }
        }

        if (criteria.BrandId is { } brandId)
        {
            var id = new BrandId(brandId);
            query = query.Where(part => part.BrandId == id);
        }

        if (criteria.CategoryId is { } categoryId)
        {
            var id = new CategoryId(categoryId);
            query = query.Where(part => part.CategoryId == id);
        }

        if (!string.IsNullOrWhiteSpace(criteria.Status)
            && Enum.TryParse(criteria.Status, ignoreCase: true, out PartStatus status))
        {
            query = query.Where(part => part.Status == status);
        }

        if (criteria.RequiresCoreReturn is { } requiresCore)
        {
            query = query.Where(part => part.RequiresCoreReturn == requiresCore);
        }

        return await PageSummariesAsync(query, page, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<PagedResult<PartSummary>> FindPartsForVehicleAsync(
        VehicleCriteria vehicle,
        PageRequest page,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(vehicle);
        ArgumentNullException.ThrowIfNull(page);

        string make = vehicle.Make?.Trim().ToUpperInvariant() ?? string.Empty;
        string model = vehicle.Model?.Trim().ToUpperInvariant() ?? string.Empty;
        string? engine = string.IsNullOrWhiteSpace(vehicle.EngineCode)
            ? null
            : vehicle.EngineCode.Trim().ToUpperInvariant();
        int year = vehicle.Year;

        // This is the counter-facing lookup: a customer is standing there with a vehicle and
        // wants something they can buy today. Drafts are parts nobody has finished setting up,
        // so offering one is a promise the business cannot keep. Note that SearchPartsAsync
        // deliberately does NOT filter this way - catalogue staff need to find their own drafts.
        IQueryable<Part> query = _context.Parts
            .AsNoTracking()
            .Where(part => Part.SellableStatuses.Contains(part.Status))
            .Where(part => part.Fitments.Any(fitment =>
                fitment.Make == make
                && fitment.Model == model
                && fitment.YearFrom <= year
                && fitment.YearTo >= year
                && (engine == null || fitment.EngineCode == null || fitment.EngineCode == engine)));

        return await PageSummariesAsync(query, page, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BrandDto>> ListBrandsAsync(
        bool activeOnly,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Brand> query = _context.Brands.AsNoTracking();

        if (activeOnly)
        {
            query = query.Where(brand => brand.IsActive);
        }

        var rows = await query
            .OrderBy(brand => brand.Name)
            .Select(brand => new
            {
                brand.Id,
                brand.Code,
                brand.Name,
                brand.IsOriginalEquipment,
                brand.IsActive,
                brand.CountryCode,
                PartCount = _context.Parts.Count(part => part.BrandId == brand.Id),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. rows.Select(row => new BrandDto(
            row.Id.Value,
            row.Code,
            row.Name,
            row.IsOriginalEquipment,
            row.IsActive,
            row.CountryCode,
            row.PartCount))];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CategoryDto>> ListCategoriesAsync(
        bool activeOnly,
        CancellationToken cancellationToken = default)
    {
        IQueryable<PartCategory> query = _context.Categories.AsNoTracking();

        if (activeOnly)
        {
            query = query.Where(category => category.IsActive);
        }

        var rows = await query
            .OrderBy(category => category.SortOrder)
            .ThenBy(category => category.Name)
            .Select(category => new
            {
                category.Id,
                category.Code,
                category.Name,
                category.ParentId,
                category.SortOrder,
                category.IsActive,
                PartCount = _context.Parts.Count(part => part.CategoryId == category.Id),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. rows.Select(row => new CategoryDto(
            row.Id.Value,
            row.Code,
            row.Name,
            row.ParentId?.Value,
            row.SortOrder,
            row.IsActive,
            row.PartCount))];
    }

    private async Task<PagedResult<PartSummary>> PageSummariesAsync(
        IQueryable<Part> query,
        PageRequest page,
        CancellationToken cancellationToken)
    {
        int total = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        if (total == 0)
        {
            return PagedResult<PartSummary>.Empty(page.Page, page.PageSize);
        }

        var rows = await query
            .OrderBy(part => part.ManufacturerPartNumber.Normalized)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(part => new
            {
                part.Id,
                part.Sku,
                Mpn = part.ManufacturerPartNumber.Display,
                BrandCode = _context.Brands
                    .Where(brand => brand.Id == part.BrandId)
                    .Select(brand => brand.Code)
                    .FirstOrDefault(),
                BrandName = _context.Brands
                    .Where(brand => brand.Id == part.BrandId)
                    .Select(brand => brand.Name)
                    .FirstOrDefault(),
                CategoryName = _context.Categories
                    .Where(category => category.Id == part.CategoryId)
                    .Select(category => category.Name)
                    .FirstOrDefault(),
                part.Name,
                part.StockUnit,
                part.Status,
                part.RequiresCoreReturn,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<PartSummary> items = [.. rows.Select(row => new PartSummary
        {
            Id = row.Id.Value,
            Sku = row.Sku.Value,
            ManufacturerPartNumber = row.Mpn,
            BrandCode = row.BrandCode ?? string.Empty,
            BrandName = row.BrandName ?? string.Empty,
            CategoryName = row.CategoryName ?? string.Empty,
            Name = row.Name,
            StockUnit = row.StockUnit.Code,
            Status = row.Status.ToString(),
            RequiresCoreReturn = row.RequiresCoreReturn,
        })];

        return PagedResult<PartSummary>.Create(items, page.Page, page.PageSize, total);
    }

    private IQueryable<PartRow> SelectRows(IQueryable<Part> query) =>
        query
            .AsNoTracking()
            .Select(part => new PartRow
            {
                Part = part,
                BrandCode = _context.Brands
                    .Where(brand => brand.Id == part.BrandId)
                    .Select(brand => brand.Code)
                    .FirstOrDefault(),
                BrandName = _context.Brands
                    .Where(brand => brand.Id == part.BrandId)
                    .Select(brand => brand.Name)
                    .FirstOrDefault(),
                CategoryName = _context.Categories
                    .Where(category => category.Id == part.CategoryId)
                    .Select(category => category.Name)
                    .FirstOrDefault(),
            });

    private static PartDetail MapDetail(PartRow row)
    {
        Part part = row.Part;

        return new PartDetail
        {
            Id = part.Id.Value,
            Sku = part.Sku.Value,
            ManufacturerPartNumber = part.ManufacturerPartNumber.Display,
            NormalizedPartNumber = part.ManufacturerPartNumber.Normalized,
            BrandId = part.BrandId.Value,
            BrandCode = row.BrandCode ?? string.Empty,
            BrandName = row.BrandName ?? string.Empty,
            CategoryId = part.CategoryId.Value,
            CategoryName = row.CategoryName ?? string.Empty,
            Name = part.Name,
            Description = part.Description,
            StockUnit = part.StockUnit.Code,
            Status = part.Status.ToString(),
            WeightKg = part.Package.WeightKg,
            LengthMm = part.Package.LengthMm,
            WidthMm = part.Package.WidthMm,
            HeightMm = part.Package.HeightMm,
            IsDangerousGoods = part.Package.IsDangerousGoods,
            UnNumber = part.Package.UnNumber,
            RequiresCoreReturn = part.RequiresCoreReturn,
            CoreChargeAmount = part.CoreCharge?.Amount,
            CoreChargeCurrency = part.CoreCharge?.Currency.Code,
            SupersededByPartId = part.SupersededByPartId?.Value,
            CrossReferences = [.. part.CrossReferences.Select(reference => new CrossReferenceDto(
                reference.Kind.ToString(),
                reference.SourceBrand,
                reference.Number,
                reference.NormalizedNumber,
                reference.Notes))],
            Fitments = [.. part.Fitments.Select(fitment => new FitmentDto(
                fitment.Make,
                fitment.Model,
                fitment.EngineCode,
                fitment.YearFrom,
                fitment.YearTo,
                fitment.Position,
                fitment.Notes))],
        };
    }

    /// <summary>One part together with the names joined in from the brand and category tables.</summary>
    private sealed class PartRow
    {
        public Part Part { get; init; } = null!;

        public string? BrandCode { get; init; }

        public string? BrandName { get; init; }

        public string? CategoryName { get; init; }
    }
}
