using AutoPartsErp.ModuleContracts.Catalog;
using AutoPartsErp.Modules.Inventory.Application.Contracts;
using AutoPartsErp.Modules.Inventory.Application.Warehouses;
using AutoPartsErp.Modules.Purchasing.Application.Contracts;
using AutoPartsErp.Modules.Purchasing.Application.Orders.Queries;
using AutoPartsErp.Modules.Purchasing.Application.Replenishment;
using AutoPartsErp.SharedKernel.Authorization;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Paging;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.Web.Models;
using AutoPartsErp.Web.Security;
using Microsoft.AspNetCore.Mvc;

namespace AutoPartsErp.Web.Controllers;

/// <summary>
/// The buyer's screen: what has run low, and what to do about it.
/// <para>
/// It reads Purchasing's suggestions rather than Inventory's balances. The two answer different
/// questions — Inventory says what is below its line, Purchasing says what a buyer has not yet
/// decided about — and only the second is a work queue. A list that kept showing a part the buyer
/// dismissed last week is a list that argues with the person reading it.
/// </para>
/// </summary>
[RequirePermission(Permissions.Purchasing.Read)]
public sealed class PurchasingController : Controller
{
    private readonly IDispatcher _dispatcher;
    private readonly ICatalogDirectory _catalogue;

    /// <summary>Initializes the controller.</summary>
    public PurchasingController(IDispatcher dispatcher, ICatalogDirectory catalogue)
    {
        _dispatcher = dispatcher;
        _catalogue = catalogue;
    }

    /// <summary>The list of suggestions.</summary>
    /// <param name="search">How the list is narrowed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet]
    public async Task<IActionResult> Index(
        SuggestionSearchForm search,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(search);

        Result<PagedResult<ReplenishmentSuggestionDto>> found = await _dispatcher.SendAsync(
            new ListReplenishmentSuggestionsQuery(
                search.WarehouseId,
                PartId: null,
                search.EffectiveStatus,
                search.Page,
                SuggestionSearchForm.PageSize),
            cancellationToken);

        Result<IReadOnlyList<WarehouseDto>> warehouses =
            await _dispatcher.SendAsync(new ListWarehousesQuery(), cancellationToken);

        if (found.IsFailure)
        {
            ModelState.AddModelError(string.Empty, found.Error.Description);
        }

        PagedResult<ReplenishmentSuggestionDto> page = found.IsSuccess
            ? found.Value
            : PagedResult<ReplenishmentSuggestionDto>.Empty(1, SuggestionSearchForm.PageSize);

        IReadOnlyList<WarehouseDto> places = warehouses.IsFailure
            ? []
            : [.. warehouses.Value.OrderBy(place => place.Code, StringComparer.CurrentCulture)];

        return View(new SuggestionResults(
            search,
            SuggestionRow.Combine(page.Items, await NamesAsync(page.Items, cancellationToken), Codes(places)),
            page,
            places));
    }

    /// <summary>The purchase orders themselves.</summary>
    /// <param name="search">How the list is narrowed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet]
    public async Task<IActionResult> Orders(
        OrderSearchForm search,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(search);

        Result<PagedResult<PurchaseOrderSummary>> found = await _dispatcher.SendAsync(
            new SearchPurchaseOrdersQuery(
                search.Term,
                SupplierId: null,
                search.WarehouseId,
                search.Status,
                search.OutstandingOnly,
                OverdueOnly: false,
                search.Page,
                OrderSearchForm.PageSize),
            cancellationToken);

        Result<IReadOnlyList<WarehouseDto>> warehouses =
            await _dispatcher.SendAsync(new ListWarehousesQuery(), cancellationToken);

        if (found.IsFailure)
        {
            ModelState.AddModelError(string.Empty, found.Error.Description);
        }

        return View(new OrderResults(
            search,
            found.IsSuccess
                ? found.Value
                : PagedResult<PurchaseOrderSummary>.Empty(1, OrderSearchForm.PageSize),
            warehouses.IsFailure
                ? []
                : [.. warehouses.Value.OrderBy(place => place.Code, StringComparer.CurrentCulture)]));
    }

    /// <summary>One purchase order, with its lines.</summary>
    /// <param name="id">The order.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet]
    public async Task<IActionResult> Order(Guid id, CancellationToken cancellationToken)
    {
        Result<PurchaseOrderDetail> found = await _dispatcher.SendAsync(
            new GetPurchaseOrderQuery(id), cancellationToken);

        if (found.IsFailure)
        {
            return NotFound();
        }

        Result<IReadOnlyList<WarehouseDto>> warehouses =
            await _dispatcher.SendAsync(new ListWarehousesQuery(ActiveOnly: false), cancellationToken);

        // Inactive warehouses included on purpose. An order delivered into a site that has since
        // been closed still has to say where it went.
        string? code = warehouses.IsSuccess
            ? warehouses.Value
                .FirstOrDefault(place => place.Id == found.Value.DeliverToWarehouseId)?.Code
            : null;

        return View(new OrderDetail(found.Value, code));
    }

    /// <summary>Takes a suggestion off the list, with a reason.</summary>
    /// <param name="form">Which one, and why.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpPost]
    [RequirePermission(Permissions.Purchasing.Manage)]
    public async Task<IActionResult> Dismiss(
        DismissSuggestionForm form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);

        if (!ModelState.IsValid)
        {
            TempData["Error"] = ModelState.Values
                .SelectMany(entry => entry.Errors)
                .Select(error => error.ErrorMessage)
                .FirstOrDefault();

            return Back(form.ReturnUrl);
        }

        Result dismissed = await _dispatcher.SendAsync(
            new DismissReplenishmentSuggestionCommand(form.SuggestionId, form.Reason),
            cancellationToken);

        // The module's own sentence when it refuses. It knows things this screen does not — that
        // the suggestion was already rolled into an order, for one — and inventing a friendlier
        // message here would be inventing a reason.
        TempData[dismissed.IsSuccess ? "Done" : "Error"] = dismissed.IsSuccess
            ? "Sugestão retirada da lista."
            : dismissed.Error.Description;

        return Back(form.ReturnUrl);
    }

    /// <summary>
    /// Back where they were, filters and page intact.
    /// <para>
    /// Through <c>IsLocalUrl</c>, like the sign-in redirect: a return address that came in on a
    /// form post is a return address somebody else could have written.
    /// </para>
    /// </summary>
    private IActionResult Back(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? Redirect(returnUrl)
            : RedirectToAction(nameof(Index));

    private async Task<IReadOnlyDictionary<Guid, PartDescriptor>> NamesAsync(
        IReadOnlyList<ReplenishmentSuggestionDto> suggestions,
        CancellationToken cancellationToken)
    {
        if (suggestions.Count == 0)
        {
            return new Dictionary<Guid, PartDescriptor>();
        }

        return await _catalogue.GetManyAsync(
            [.. suggestions.Select(suggestion => suggestion.PartId).Distinct()], cancellationToken);
    }

    private static Dictionary<Guid, string> Codes(IReadOnlyList<WarehouseDto> warehouses) =>
        warehouses.ToDictionary(place => place.Id, place => place.Code);
}
