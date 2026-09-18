using AutoPartsErp.ModuleContracts.Catalog;
using AutoPartsErp.Modules.Catalog.Application.Contracts;
using AutoPartsErp.Modules.Inventory.Application.Contracts;

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
/// <param name="Replenishment">What has fallen to its reorder point.</param>
/// <param name="ReplenishmentTotal">
/// How many have, in total. The panel shows the first few; this is the number that says whether
/// those few are the whole problem or the tip of it.
/// </param>
public sealed record Dashboard(
    CatalogueCounts Parts,
    IReadOnlyList<BrandDto> Brands,
    IReadOnlyList<CategoryDto> Categories,
    IReadOnlyList<ReplenishmentRow> Replenishment,
    int ReplenishmentTotal);

/// <summary>The size of the catalogue, split the way a person reads it.</summary>
/// <param name="Total">Every part on file.</param>
/// <param name="Active">Parts that may be sold.</param>
/// <param name="Draft">Parts somebody started and has not finished.</param>
/// <param name="Retired">Discontinued and obsolete together.</param>
public sealed record CatalogueCounts(int Total, int Active, int Draft, int Retired);

/// <summary>
/// One part that has fallen to its reorder point, ready to be read.
/// </summary>
/// <param name="PartId">The part, so the row can be opened.</param>
/// <param name="Sku">Its reference, from the catalogue.</param>
/// <param name="Name">Its description, from the catalogue.</param>
/// <param name="WarehouseCode">Which warehouse is short.</param>
/// <param name="Unit">What the quantities are counted in.</param>
/// <param name="Available">What can still be sold from that shelf.</param>
/// <param name="OnOrder">What is already on its way in.</param>
/// <param name="ReorderPoint">The line it fell below.</param>
/// <param name="ReorderQuantity">How much to order, when somebody has said.</param>
public sealed record ReplenishmentRow(
    Guid PartId,
    string Sku,
    string Name,
    string WarehouseCode,
    string Unit,
    decimal Available,
    decimal OnOrder,
    decimal? ReorderPoint,
    decimal? ReorderQuantity)
{
    /// <summary>
    /// How short the shelf is: the reorder point less what is available and already coming.
    /// <para>
    /// Not "available less the point". A part with four available, six on order and a point of
    /// eight is not short by four — six are on the way, so it is not short at all, and a screen
    /// that says otherwise has somebody order it twice.
    /// </para>
    /// </summary>
    public decimal Shortfall =>
        ReorderPoint is decimal point ? Math.Max(point - (Available + OnOrder), 0m) : 0m;

    /// <summary>
    /// What Catalog says about a part it has never heard of, which is nothing.
    /// <para>
    /// Inventory can hold a balance for a part the catalogue no longer has; the row still has to
    /// render, because a shelf with stock on it is a fact whatever the catalogue thinks. The
    /// identifier is shown instead of a name, so whoever sees it can go and find out why.
    /// </para>
    /// </summary>
    public static ReplenishmentRow From(StockBalance balance, PartDescriptor? part)
    {
        ArgumentNullException.ThrowIfNull(balance);

        return new ReplenishmentRow(
            balance.PartId,
            part?.Sku ?? balance.PartId.ToString(),
            part?.Name ?? "Sem ficha no catálogo",
            balance.WarehouseCode,
            balance.Unit,
            balance.Available,
            balance.OnOrder,
            balance.ReorderPoint,
            balance.ReorderQuantity);
    }

    /// <summary>
    /// Names a page of balances from the catalogue, in one go.
    /// <para>
    /// Separate from the controller so it can be tested without a request. The order is
    /// Inventory's — furthest under the line first — and must survive being named, because that
    /// order is the whole point of the panel.
    /// </para>
    /// </summary>
    /// <param name="balances">What Inventory said, in its order.</param>
    /// <param name="parts">What Catalog said, by part.</param>
    /// <returns>One row per balance, in the same order.</returns>
    public static IReadOnlyList<ReplenishmentRow> Combine(
        IReadOnlyList<StockBalance> balances,
        IReadOnlyDictionary<Guid, PartDescriptor> parts)
    {
        ArgumentNullException.ThrowIfNull(balances);
        ArgumentNullException.ThrowIfNull(parts);

        return
        [
            .. balances.Select(balance => From(
                balance,
                parts.TryGetValue(balance.PartId, out PartDescriptor? part) ? part : null)),
        ];
    }
}
