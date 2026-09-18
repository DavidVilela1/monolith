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

/// <summary>
/// The parts counter: the screen this business spends its day on.
/// </summary>
[RequirePermission(Permissions.Catalog.Read)]
public sealed class PartsController : Controller
{
    private readonly IDispatcher _dispatcher;

    /// <summary>Initializes the controller.</summary>
    public PartsController(IDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
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

        if (found.IsFailure)
        {
            ModelState.AddModelError(string.Empty, found.Error.Description);
        }

        return View(new PartSearchResults(
            search,
            found.IsSuccess
                ? found.Value
                : PagedResult<PartSummary>.Empty(1, PartSearchForm.PageSize),
            Names(brands, brand => brand.Name),
            Names(categories, category => category.Name)));
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
    /// A filter list, in the order a person reads one.
    /// <para>
    /// By name, not by whatever order the database handed back. A dropdown of four hundred
    /// brands in insertion order is a dropdown nobody finds anything in.
    /// </para>
    /// </summary>
    private static IReadOnlyList<T> Names<T>(Result<IReadOnlyList<T>> result, Func<T, string> by) =>
        result.IsFailure
            ? []
            : [.. result.Value.OrderBy(by, StringComparer.CurrentCulture)];
}
