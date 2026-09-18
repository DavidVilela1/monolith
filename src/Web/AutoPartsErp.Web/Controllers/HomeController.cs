using AutoPartsErp.ModuleContracts.Catalog;
using AutoPartsErp.Modules.Catalog.Application.Brands;
using AutoPartsErp.Modules.Catalog.Application.Categories;
using AutoPartsErp.Modules.Catalog.Application.Contracts;
using AutoPartsErp.Modules.Catalog.Application.Parts.Queries;
using AutoPartsErp.Modules.Inventory.Application.Contracts;
using AutoPartsErp.Modules.Inventory.Application.Stock.Queries;
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
        // Each panel is behind the permission for the module it reads. Somebody given only
        // Inventory sees the replenishment panel and no catalogue figures, and somebody given
        // only Catalog sees the reverse — rather than being thrown off the page they land on.
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

        (IReadOnlyList<ReplenishmentRow> replenishment, int shortTotal) =
            User.Can(Permissions.Inventory.Read)
                ? await ReplenishmentAsync(cancellationToken)
                : ([], 0);

        return View(new Dashboard(counts, brands, categories, replenishment, shortTotal));
    }

    /// <summary>
    /// What has fallen to its reorder point, with the catalogue's words on it.
    /// <para>
    /// Inventory orders the list by how far under the line each part has fallen, and knows
    /// nothing about what any of them is called. Catalog knows the names and nothing about the
    /// shelves. Two questions, one round trip each, joined here.
    /// </para>
    /// </summary>
    private async Task<(IReadOnlyList<ReplenishmentRow> Rows, int Total)> ReplenishmentAsync(
        CancellationToken cancellationToken)
    {
        Result<PagedResult<StockBalance>> shortages = await _dispatcher.SendAsync(
            new GetReplenishmentListQuery(WarehouseId: null, Page: 1, PageSize: PanelRows),
            cancellationToken);

        if (shortages.IsFailure || shortages.Value.Items.Count == 0)
        {
            return ([], shortages.IsSuccess ? shortages.Value.TotalCount : 0);
        }

        IReadOnlyList<StockBalance> balances = shortages.Value.Items;

        IReadOnlyDictionary<Guid, PartDescriptor> parts = await _catalogue.GetManyAsync(
            [.. balances.Select(balance => balance.PartId).Distinct()], cancellationToken);

        return (ReplenishmentRow.Combine(balances, parts), shortages.Value.TotalCount);
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
