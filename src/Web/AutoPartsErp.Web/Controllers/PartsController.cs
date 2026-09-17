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
                BrandId: null,
                CategoryId: null,
                Status: search.Status,
                RequiresCoreReturn: null,
                Page: search.Page,
                PageSize: PartSearchForm.PageSize),
            cancellationToken);

        if (found.IsFailure)
        {
            ModelState.AddModelError(string.Empty, found.Error.Description);

            return View(new PartSearchResults(search, PagedResult<PartSummary>.Empty(1, PartSearchForm.PageSize)));
        }

        return View(new PartSearchResults(search, found.Value));
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
}
