using AutoPartsErp.Modules.Catalog.Application.Contracts;

namespace AutoPartsErp.Web.Models;

/// <summary>
/// What the front page shows.
/// <para>
/// Everything on it is a figure this system actually holds. The panels the design calls for —
/// what is going out today, what has to be ordered, what is overdue — are not here because the
/// modules that answer them are not wired to this project yet, and a panel of invented numbers on
/// a screen somebody opens every morning is the worst thing an ERP can contain.
/// </para>
/// </summary>
/// <param name="Parts">How many parts exist, by status.</param>
/// <param name="Brands">The brands carrying the most parts.</param>
/// <param name="Categories">The families holding the most parts.</param>
public sealed record Dashboard(
    CatalogueCounts Parts,
    IReadOnlyList<BrandDto> Brands,
    IReadOnlyList<CategoryDto> Categories);

/// <summary>The size of the catalogue, split the way a person reads it.</summary>
/// <param name="Total">Every part on file.</param>
/// <param name="Active">Parts that may be sold.</param>
/// <param name="Draft">Parts somebody started and has not finished.</param>
/// <param name="Retired">Discontinued and obsolete together.</param>
public sealed record CatalogueCounts(int Total, int Active, int Draft, int Retired);
