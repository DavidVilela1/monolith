using AutoPartsErp.Modules.Catalog.Application.Brands;
using AutoPartsErp.Modules.Catalog.Application.Categories;
using AutoPartsErp.Modules.Catalog.Application.Contracts;
using AutoPartsErp.Modules.Catalog.Application.Parts.Queries;
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
    /// <summary>How many rows the two side panels show.</summary>
    private const int PanelRows = 8;

    private readonly IDispatcher _dispatcher;

    /// <summary>Initializes the controller.</summary>
    public HomeController(IDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    /// <summary>The front page.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        // Somebody without the catalogue permission gets the page and none of its panels,
        // rather than a refusal: the front page is where being signed in lands you, and being
        // thrown off it is a system that looks broken to whoever was only given Sales.
        if (!User.Can(Permissions.Catalog.Read))
        {
            return View(new Dashboard(new CatalogueCounts(0, 0, 0, 0), [], []));
        }

        CatalogueCounts counts = new(
            await CountAsync(status: null, cancellationToken),
            await CountAsync("Active", cancellationToken),
            await CountAsync("Draft", cancellationToken),
            await CountAsync("Discontinued", cancellationToken)
                + await CountAsync("Obsolete", cancellationToken));

        Result<IReadOnlyList<BrandDto>> brands =
            await _dispatcher.SendAsync(new ListBrandsQuery(), cancellationToken);

        Result<IReadOnlyList<CategoryDto>> categories =
            await _dispatcher.SendAsync(new ListCategoriesQuery(), cancellationToken);

        return View(new Dashboard(
            counts,
            Top(brands, brand => brand.PartCount),
            Top(categories, category => category.PartCount)));
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
