using AutoPartsErp.Modules.Catalog.Application.Contracts;

namespace AutoPartsErp.Web.Models;

/// <summary>
/// What the front page shows.
/// <para>
/// Everything on it is a figure this system actually holds. The panels the design calls for that
/// are not here — what is going out today, what is overdue — are absent rather than drawn with
/// placeholder numbers, because a panel of invented figures on a screen somebody opens every
/// morning is the worst thing an ERP can contain.
/// </para>
/// </summary>
/// <param name="Parts">How many parts exist, by status.</param>
/// <param name="Brands">The brands carrying the most parts.</param>
/// <param name="Categories">The families holding the most parts.</param>
/// <param name="Replenishment">What the buyer still has to decide about.</param>
/// <param name="ReplenishmentTotal">
/// How many have, in total. The panel shows the first few; this is the number that says whether
/// those few are the whole problem or the tip of it.
/// </param>
public sealed record Dashboard(
    CatalogueCounts Parts,
    IReadOnlyList<BrandDto> Brands,
    IReadOnlyList<CategoryDto> Categories,
    IReadOnlyList<SuggestionRow> Replenishment,
    int ReplenishmentTotal);

/// <summary>The size of the catalogue, split the way a person reads it.</summary>
/// <param name="Total">Every part on file.</param>
/// <param name="Active">Parts that may be sold.</param>
/// <param name="Draft">Parts somebody started and has not finished.</param>
/// <param name="Retired">Discontinued and obsolete together.</param>
public sealed record CatalogueCounts(int Total, int Active, int Draft, int Retired);
