using AutoPartsErp.Modules.Catalog.Application.Contracts;
using AutoPartsErp.SharedKernel.Paging;

namespace AutoPartsErp.Modules.Catalog.Application.Abstractions;

/// <summary>
/// The read side of the Catalog module.
/// <para>
/// Commands go through repositories and aggregates, because they need rules enforced.
/// Queries come through here and project straight to DTOs, because a grid of 50 rows should be
/// one indexed SELECT, not 50 aggregates with their cross-references and fitments loaded.
/// Splitting the two is what keeps a catalogue of half a million parts responsive.
/// </para>
/// </summary>
public interface ICatalogReadStore
{
    /// <summary>Loads the full detail of one part, or null when it does not exist.</summary>
    Task<PartDetail?> GetPartAsync(Guid partId, CancellationToken cancellationToken = default);

    /// <summary>Loads the full detail of one part by SKU, or null when it does not exist.</summary>
    Task<PartDetail?> GetPartBySkuAsync(string sku, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches parts by number, description, brand, category and status.
    /// </summary>
    /// <param name="criteria">What to search for.</param>
    /// <param name="page">Which page to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<PagedResult<PartSummary>> SearchPartsAsync(
        PartSearchCriteria criteria,
        PageRequest page,
        CancellationToken cancellationToken = default);

    /// <summary>Finds every part recorded as fitting the supplied vehicle.</summary>
    Task<PagedResult<PartSummary>> FindPartsForVehicleAsync(
        VehicleCriteria vehicle,
        PageRequest page,
        CancellationToken cancellationToken = default);

    /// <summary>Lists brands.</summary>
    Task<IReadOnlyList<BrandDto>> ListBrandsAsync(
        bool activeOnly,
        CancellationToken cancellationToken = default);

    /// <summary>Lists categories, ordered by parent then sort order.</summary>
    Task<IReadOnlyList<CategoryDto>> ListCategoriesAsync(
        bool activeOnly,
        CancellationToken cancellationToken = default);
}

/// <summary>What to look for when searching parts.</summary>
public sealed record PartSearchCriteria
{
    /// <summary>
    /// Free text typed at the counter. Matched against the SKU, the manufacturer part number
    /// and every cross-reference, all in normalized form, plus the description.
    /// </summary>
    public string? Term { get; init; }

    /// <summary>Restrict to one brand.</summary>
    public Guid? BrandId { get; init; }

    /// <summary>Restrict to one category.</summary>
    public Guid? CategoryId { get; init; }

    /// <summary>Restrict to one status, for example only Active parts.</summary>
    public string? Status { get; init; }

    /// <summary>Restrict to parts sold against a returnable core.</summary>
    public bool? RequiresCoreReturn { get; init; }
}

/// <summary>The vehicle a customer is trying to find parts for.</summary>
/// <param name="Make">Vehicle manufacturer.</param>
/// <param name="Model">Model designation.</param>
/// <param name="Year">Model year.</param>
/// <param name="EngineCode">Optional engine or type code, which narrows the result sharply.</param>
public sealed record VehicleCriteria(string Make, string Model, int Year, string? EngineCode = null);
