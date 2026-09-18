using AutoPartsErp.ModuleContracts.Inventory;
using AutoPartsErp.Modules.Catalog.Application.Contracts;
using AutoPartsErp.Modules.Inventory.Application.Contracts;
using AutoPartsErp.SharedKernel.Paging;

namespace AutoPartsErp.Web.Models;

/// <summary>What the search box and its filters hold.</summary>
public sealed class PartSearchForm
{
    /// <summary>How many rows a page of the counter screen shows.</summary>
    public const int PageSize = 40;

    /// <summary>What was typed: a SKU, any number off the old part, or words from the name.</summary>
    public string? Term { get; set; }

    /// <summary>One lifecycle status only, when chosen.</summary>
    public string? Status { get; set; }

    /// <summary>One brand only, when chosen.</summary>
    public Guid? BrandId { get; set; }

    /// <summary>One family only, when chosen.</summary>
    public Guid? CategoryId { get; set; }

    /// <summary>
    /// Which warehouse the stock figures are for.
    /// <para>
    /// Stock is always stock somewhere. A column headed "stock" that quietly added up every
    /// warehouse would have somebody promise a part that is two hours away.
    /// </para>
    /// </summary>
    public Guid? WarehouseId { get; set; }

    /// <summary>Which page, one-based.</summary>
    public int Page { get; set; } = 1;

    /// <summary>True when anything at all is narrowing the list.</summary>
    public bool IsFiltered =>
        !string.IsNullOrWhiteSpace(Term)
        || !string.IsNullOrWhiteSpace(Status)
        || BrandId is not null
        || CategoryId is not null;
}

/// <summary>
/// One row of the counter screen: a part, and what the chosen warehouse has of it.
/// </summary>
/// <param name="Part">The catalogue entry.</param>
/// <param name="Stock">
/// What Inventory holds, or null when Inventory has never heard of this part — which is the
/// ordinary state of a part still in draft, not an error.
/// </param>
public sealed record PartRow(PartSummary Part, StockAvailability? Stock);

/// <summary>
/// The search form, what it found, and the lists the filters are chosen from.
/// <para>
/// One model rather than four pieces of <c>ViewData</c>: a filter that renders the brand it was
/// given but cannot name it is the sort of thing that survives review because it only shows up
/// once somebody actually picks a brand.
/// </para>
/// </summary>
/// <param name="Search">What was asked.</param>
/// <param name="Rows">What came back, with stock against it.</param>
/// <param name="Page">The paging figures the footer needs.</param>
/// <param name="Brands">Every active brand, for the filter.</param>
/// <param name="Categories">Every active family, for the filter.</param>
/// <param name="Warehouses">Every active warehouse, for the filter.</param>
public sealed record PartSearchResults(
    PartSearchForm Search,
    IReadOnlyList<PartRow> Rows,
    PagedResult<PartSummary> Page,
    IReadOnlyList<BrandDto> Brands,
    IReadOnlyList<CategoryDto> Categories,
    IReadOnlyList<WarehouseDto> Warehouses)
{
    /// <summary>The warehouse the stock columns are counted in, when there is one.</summary>
    public WarehouseDto? Warehouse =>
        Warehouses.FirstOrDefault(warehouse => warehouse.Id == Search.WarehouseId);

    /// <summary>
    /// Which warehouse the figures are for.
    /// <para>
    /// The one asked for, if it is real. A warehouse identifier off a stale link or a typed URL
    /// must not silently become "no warehouse", because that renders a catalogue with empty stock
    /// columns and nothing saying why — so an unrecognized one falls back to the first, and the
    /// chip then shows which warehouse is actually being counted.
    /// </para>
    /// </summary>
    /// <param name="asked">What the query string said, if anything.</param>
    /// <param name="warehouses">The warehouses that exist.</param>
    /// <returns>The warehouse to count in, or null when there are none.</returns>
    public static Guid? ChooseWarehouse(Guid? asked, IReadOnlyList<WarehouseDto> warehouses)
    {
        ArgumentNullException.ThrowIfNull(warehouses);

        if (warehouses.Count == 0)
        {
            return null;
        }

        return warehouses.Any(warehouse => warehouse.Id == asked)
            ? asked
            : warehouses[0].Id;
    }

    /// <summary>
    /// Zips a page of parts together with the stock that was asked for in one go.
    /// <para>
    /// Separate here rather than inside the controller so it can be tested without a request: the
    /// case that matters is a part Inventory has no record of, which has to come back as a row
    /// with no figures rather than as a missing row or a zero. A zero would read as "none left".
    /// </para>
    /// </summary>
    /// <param name="parts">The page of parts, in the order they will be shown.</param>
    /// <param name="stock">What Inventory answered, by part.</param>
    /// <returns>One row per part, in the same order.</returns>
    public static IReadOnlyList<PartRow> Combine(
        IReadOnlyList<PartSummary> parts,
        IReadOnlyDictionary<Guid, StockAvailability> stock)
    {
        ArgumentNullException.ThrowIfNull(parts);
        ArgumentNullException.ThrowIfNull(stock);

        return
        [
            .. parts.Select(part => new PartRow(
                part,
                stock.TryGetValue(part.Id, out StockAvailability? found) ? found : null)),
        ];
    }
}
