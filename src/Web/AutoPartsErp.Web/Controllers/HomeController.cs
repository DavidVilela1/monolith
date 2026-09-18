using AutoPartsErp.ModuleContracts.Catalog;
using AutoPartsErp.Modules.Catalog.Application.Brands;
using AutoPartsErp.Modules.Catalog.Application.Categories;
using AutoPartsErp.Modules.Catalog.Application.Contracts;
using AutoPartsErp.Modules.Catalog.Application.Parts.Queries;
using AutoPartsErp.Modules.Inventory.Application.Contracts;
using AutoPartsErp.Modules.Inventory.Application.Warehouses;
using AutoPartsErp.Modules.Purchasing.Application.Contracts;
using AutoPartsErp.Modules.Purchasing.Application.Replenishment;
using AutoPartsErp.SharedKernel.Authorization;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Paging;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.Web.Models;
using AutoPartsErp.Web.Security;
using Microsoft.AspNetCore.Mvc;

namespace AutoPartsErp.Web.Controllers;

/// <summary>Where a signed-in person lands.</summary>
public sealed class HomeController : Controller
{
    /// <summary>How many rows each panel shows.</summary>
    private const int PanelRows = 8;

    private readonly IDispatcher _dispatcher;
    private readonly ICatalogDirectory _catalogue;

    /// <summary>Initializes the controller.</summary>
    public HomeController(IDispatcher dispatcher, ICatalogDirectory catalogue)
    {
        _dispatcher = dispatcher;
        _catalogue = catalogue;
    }

    /// <summary>The front page.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        // Each panel is behind the permission for the module it reads. A buyer sees the
        // replenishment queue and no catalogue figures, a counter clerk sees the reverse — rather
        // than either being thrown off the page they land on when they sign in.
        CatalogueCounts counts = new(0, 0, 0, 0);
        IReadOnlyList<BrandDto> brands = [];
        IReadOnlyList<CategoryDto> categories = [];

        if (User.Can(Permissions.Catalog.Read))
        {
            counts = new CatalogueCounts(
                await CountAsync(status: null, cancellationToken),
                await CountAsync("Active", cancellationToken),
                await CountAsync("Draft", cancellationToken),
                await CountAsync("Discontinued", cancellationToken)
                    + await CountAsync("Obsolete", cancellationToken));

            brands = Top(
                await _dispatcher.SendAsync(new ListBrandsQuery(), cancellationToken),
                brand => brand.PartCount);

            categories = Top(
                await _dispatcher.SendAsync(new ListCategoriesQuery(), cancellationToken),
                category => category.PartCount);
        }

        // Purchasing's queue, not Inventory's balances. Inventory says what is below its line;
        // Purchasing says what nobody has decided about yet, which is what a person can act on.
        // The panel used to read the first and nagged about parts the buyer had already
        // dismissed.
        (IReadOnlyList<SuggestionRow> replenishment, int shortTotal) =
            User.Can(Permissions.Purchasing.Read)
                ? await ReplenishmentAsync(cancellationToken)
                : ([], 0);

        return View(new Dashboard(counts, brands, categories, replenishment, shortTotal));
    }

    /// <summary>
    /// What the buyer has not dealt with, with the catalogue's words and the warehouse's code on it.
    /// <para>
    /// Purchasing orders the list by how far under the line each part has fallen and knows nothing
    /// about what any of them is called; Catalog knows the names; Inventory knows the warehouses.
    /// Three questions, one round trip each, joined here.
    /// </para>
    /// </summary>
    private async Task<(IReadOnlyList<SuggestionRow> Rows, int Total)> ReplenishmentAsync(
        CancellationToken cancellationToken)
    {
        Result<PagedResult<ReplenishmentSuggestionDto>> open = await _dispatcher.SendAsync(
            new ListReplenishmentSuggestionsQuery(
                WarehouseId: null, PartId: null, Status: "Open", Page: 1, PageSize: PanelRows),
            cancellationToken);

        if (open.IsFailure || open.Value.Items.Count == 0)
        {
            return ([], open.IsSuccess ? open.Value.TotalCount : 0);
        }

        IReadOnlyList<ReplenishmentSuggestionDto> suggestions = open.Value.Items;

        IReadOnlyDictionary<Guid, PartDescriptor> parts = await _catalogue.GetManyAsync(
            [.. suggestions.Select(suggestion => suggestion.PartId).Distinct()], cancellationToken);

        Result<IReadOnlyList<WarehouseDto>> warehouses =
            await _dispatcher.SendAsync(new ListWarehousesQuery(ActiveOnly: false), cancellationToken);

        Dictionary<Guid, string> codes = warehouses.IsFailure
            ? []
            : warehouses.Value.ToDictionary(place => place.Id, place => place.Code);

        return (SuggestionRow.Combine(suggestions, parts, codes), open.Value.TotalCount);
    }

    /// <summary>
    /// Asks for one row and reads the total off the page.
    /// <para>
    /// Five of these is five counts against one table, which is what a summary costs when the
    /// read side has no summary of its own. The day this page grows a panel per module it wants
    /// a single query that returns the counts together, and this is the note saying so.
    /// </para>
    /// </summary>
    private async Task<int> CountAsync(string? status, CancellationToken cancellationToken)
    {
        Result<PagedResult<PartSummary>> page = await _dispatcher.SendAsync(
            new SearchPartsQuery(Status: status, Page: 1, PageSize: 1), cancellationToken);

        return page.IsSuccess ? page.Value.TotalCount : 0;
    }

    private static IReadOnlyList<T> Top<T>(Result<IReadOnlyList<T>> result, Func<T, int> by) =>
        result.IsFailure
            ? []
            : [.. result.Value.OrderByDescending(by).Take(PanelRows)];
}
