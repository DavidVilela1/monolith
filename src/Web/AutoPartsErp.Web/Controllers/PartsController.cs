using AutoPartsErp.ModuleContracts.Inventory;
using AutoPartsErp.Modules.Catalog.Application.Brands;
using AutoPartsErp.Modules.Catalog.Application.Categories;
using AutoPartsErp.Modules.Catalog.Application.Contracts;
using AutoPartsErp.Modules.Catalog.Application.Parts.Queries;
using AutoPartsErp.Modules.Inventory.Application.Contracts;
using AutoPartsErp.Modules.Inventory.Application.Warehouses;
using AutoPartsErp.SharedKernel.Authorization;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Paging;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.Web.Models;
using AutoPartsErp.Web.Security;
using Microsoft.AspNetCore.Mvc;

namespace AutoPartsErp.Web.Controllers;

/// <summary>
/// The parts counter: the screen this business spends its day on.
/// <para>
/// It asks two modules and joins their answers here, in memory, rather than joining their tables.
/// Catalog says what the part is; Inventory says how many there are. Neither knows the other
/// exists, and the question to Inventory goes through the published contract — one call for the
/// whole page, not one per row, because forty round trips is fine on a laptop and is not fine on
/// a counter.
/// </para>
/// </summary>
[RequirePermission(Permissions.Catalog.Read)]
public sealed class PartsController : Controller
{
    private readonly IDispatcher _dispatcher;
    private readonly IInventoryAvailability _availability;

    /// <summary>Initializes the controller.</summary>
    public PartsController(IDispatcher dispatcher, IInventoryAvailability availability)
    {
        _dispatcher = dispatcher;
        _availability = availability;
    }

    /// <summary>Searches the catalogue.</summary>
    /// <param name="search">What was typed and how the list is filtered.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet]
    public async Task<IActionResult> Index(
        PartSearchForm search,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(search);

        // An empty box lists the catalogue rather than nothing. A counter clerk opening the
        // screen to browse is a real thing, and a blank page with "type something" is a screen
        // that looks broken.
        Result<PagedResult<PartSummary>> found = await _dispatcher.SendAsync(
            new SearchPartsQuery(
                search.Term,
                search.BrandId,
                search.CategoryId,
                search.Status,
                RequiresCoreReturn: null,
                search.Page,
                PartSearchForm.PageSize),
            cancellationToken);

        Result<IReadOnlyList<BrandDto>> brands =
            await _dispatcher.SendAsync(new ListBrandsQuery(), cancellationToken);

        Result<IReadOnlyList<CategoryDto>> categories =
            await _dispatcher.SendAsync(new ListCategoriesQuery(), cancellationToken);

        Result<IReadOnlyList<WarehouseDto>> warehouses =
            await _dispatcher.SendAsync(new ListWarehousesQuery(), cancellationToken);

        if (found.IsFailure)
        {
            ModelState.AddModelError(string.Empty, found.Error.Description);
        }

        PagedResult<PartSummary> page = found.IsSuccess
            ? found.Value
            : PagedResult<PartSummary>.Empty(1, PartSearchForm.PageSize);

        IReadOnlyList<WarehouseDto> places = Sorted(warehouses, warehouse => warehouse.Code);

        search.WarehouseId = PartSearchResults.ChooseWarehouse(search.WarehouseId, places);

        IReadOnlyDictionary<Guid, StockAvailability> stock =
            await StockForAsync(page.Items, search.WarehouseId, cancellationToken);

        return View(new PartSearchResults(
            search,
            PartSearchResults.Combine(page.Items, stock),
            page,
            Sorted(brands, brand => brand.Name),
            Sorted(categories, category => category.Name),
            places));
    }

    /// <summary>Shows one part.</summary>
    /// <param name="id">The part.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet]
    public async Task<IActionResult> Details(Guid id, CancellationToken cancellationToken)
    {
        Result<PartDetail> part = await _dispatcher.SendAsync(
            new GetPartByIdQuery(id), cancellationToken);

        return part.IsFailure ? NotFound() : View(part.Value);
    }

    /// <summary>
    /// Asks Inventory about every part on the page at once.
    /// <para>
    /// Nothing is asked when there is no warehouse to ask about — a deployment whose warehouses
    /// have not been set up yet shows a catalogue with no stock columns, which is exactly what it
    /// has.
    /// </para>
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, StockAvailability>> StockForAsync(
        IReadOnlyList<PartSummary> parts,
        Guid? warehouseId,
        CancellationToken cancellationToken)
    {
        if (warehouseId is not Guid warehouse || parts.Count == 0)
        {
            return new Dictionary<Guid, StockAvailability>();
        }

        return await _availability.GetManyAsync(
            [.. parts.Select(part => part.Id)], warehouse, cancellationToken);
    }

    /// <summary>
    /// A filter list, in the order a person reads one.
    /// <para>
    /// By name, not by whatever order the database handed back. A dropdown of four hundred brands
    /// in insertion order is a dropdown nobody finds anything in.
    /// </para>
    /// </summary>
    private static IReadOnlyList<T> Sorted<T>(Result<IReadOnlyList<T>> result, Func<T, string> by) =>
        result.IsFailure
            ? []
            : [.. result.Value.OrderBy(by, StringComparer.CurrentCulture)];
}
