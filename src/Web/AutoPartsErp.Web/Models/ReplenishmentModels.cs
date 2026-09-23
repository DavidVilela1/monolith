using System.ComponentModel.DataAnnotations;
using AutoPartsErp.ModuleContracts.Catalog;
using AutoPartsErp.Modules.Inventory.Application.Contracts;
using AutoPartsErp.Modules.Purchasing.Application.Contracts;
using AutoPartsErp.SharedKernel.Paging;

namespace AutoPartsErp.Web.Models;

/// <summary>
/// One part the buyer still has to decide about, ready to be read.
/// <para>
/// The figures come from Purchasing's suggestion, the words from Catalog, and the warehouse's
/// code from Inventory. Three modules, none of which knows the others exist.
/// </para>
/// </summary>
/// <param name="SuggestionId">The suggestion, which is what an action acts on.</param>
/// <param name="PartId">The part, so the row can be opened.</param>
/// <param name="Sku">Its reference, from the catalogue.</param>
/// <param name="Name">Its description, from the catalogue.</param>
/// <param name="WarehouseCode">Which warehouse is short.</param>
/// <param name="Available">What can still be sold from that shelf.</param>
/// <param name="OnOrder">What is already on its way in.</param>
/// <param name="ReorderPoint">The line it fell below.</param>
/// <param name="Suggested">How much Purchasing thinks should be ordered.</param>
/// <param name="Status">Open, Ordered or Dismissed.</param>
/// <param name="RaisedOn">When it first appeared.</param>
/// <param name="PurchaseOrderId">The order it was rolled into, once it was.</param>
/// <param name="DismissedReason">Why the buyer decided not to act on it.</param>
public sealed record SuggestionRow(
    Guid SuggestionId,
    Guid PartId,
    string Sku,
    string Name,
    string WarehouseCode,
    decimal Available,
    decimal OnOrder,
    decimal ReorderPoint,
    decimal Suggested,
    string Status,
    DateOnly RaisedOn,
    Guid? PurchaseOrderId,
    string? DismissedReason)
{
    /// <summary>
    /// How short the shelf is, never below zero.
    /// <para>
    /// Purchasing already counts what is on order, which is the part that matters — a part with a
    /// delivery on its way is not urgent however empty the shelf looks. What it does not do is
    /// clamp, so a part comfortably above its line reads as a negative shortfall. Zero is what a
    /// person means by "nothing missing".
    /// </para>
    /// </summary>
    public decimal Shortfall => Math.Max(ReorderPoint - (Available + OnOrder), 0m);

    /// <summary>True while this one is still the buyer's to deal with.</summary>
    public bool IsOpen => string.Equals(Status, "Open", StringComparison.Ordinal);

    /// <summary>
    /// Builds a row, naming the part from the catalogue and the warehouse from Inventory.
    /// <para>
    /// A suggestion whose part the catalogue no longer has still renders, with the identifier
    /// where the reference would be. A shelf that ran low is a fact whatever the catalogue
    /// thinks, and a row that vanished would take the shortage with it.
    /// </para>
    /// </summary>
    public static SuggestionRow From(
        ReplenishmentSuggestionDto suggestion,
        PartDescriptor? part,
        string? warehouseCode)
    {
        ArgumentNullException.ThrowIfNull(suggestion);

        return new SuggestionRow(
            suggestion.Id,
            suggestion.PartId,
            part?.Sku ?? suggestion.PartId.ToString(),
            part?.Name ?? "Sem ficha no catálogo",
            warehouseCode ?? "—",
            suggestion.QuantityAvailable,
            suggestion.QuantityOnOrder,
            suggestion.ReorderPoint,
            suggestion.SuggestedQuantity,
            suggestion.Status,
            DateOnly.FromDateTime(suggestion.RaisedAtUtc.UtcDateTime),
            suggestion.PurchaseOrderId,
            suggestion.DismissedReason);
    }

    /// <summary>
    /// Names a page of suggestions, in one go.
    /// <para>
    /// Separate from the controller so it can be tested without a request. Purchasing's order —
    /// furthest under the line first — has to survive being named, because that order is what
    /// tells a buyer what to deal with first.
    /// </para>
    /// </summary>
    /// <param name="suggestions">What Purchasing said, in its order.</param>
    /// <param name="parts">What Catalog said, by part.</param>
    /// <param name="warehouses">What Inventory said, by warehouse.</param>
    /// <returns>One row per suggestion, in the same order.</returns>
    public static IReadOnlyList<SuggestionRow> Combine(
        IReadOnlyList<ReplenishmentSuggestionDto> suggestions,
        IReadOnlyDictionary<Guid, PartDescriptor> parts,
        IReadOnlyDictionary<Guid, string> warehouses)
    {
        ArgumentNullException.ThrowIfNull(suggestions);
        ArgumentNullException.ThrowIfNull(parts);
        ArgumentNullException.ThrowIfNull(warehouses);

        return
        [
            .. suggestions.Select(suggestion => From(
                suggestion,
                parts.TryGetValue(suggestion.PartId, out PartDescriptor? part) ? part : null,
                warehouses.TryGetValue(suggestion.WarehouseId, out string? code) ? code : null)),
        ];
    }
}

/// <summary>How the buyer's list is narrowed.</summary>
public sealed class SuggestionSearchForm
{
    /// <summary>How many rows a page shows.</summary>
    public const int PageSize = 40;

    /// <summary>
    /// Which suggestions to show. Empty means the ones still to be dealt with.
    /// <para>
    /// Open by default and not "all", because this screen is a work queue. A list that opened
    /// showing everything ever suggested, dismissals included, is a list nobody can work from.
    /// </para>
    /// </summary>
    public string? Status { get; set; }

    /// <summary>One warehouse only, when chosen.</summary>
    public Guid? WarehouseId { get; set; }

    /// <summary>Which page, one-based.</summary>
    public int Page { get; set; } = 1;

    /// <summary>The status actually asked of Purchasing.</summary>
    public string EffectiveStatus =>
        string.IsNullOrWhiteSpace(Status) ? "Open" : Status;
}

/// <summary>The buyer's list, and what the filters are chosen from.</summary>
/// <param name="Search">What was asked.</param>
/// <param name="Rows">What came back, named.</param>
/// <param name="Page">The paging figures the footer needs.</param>
/// <param name="Warehouses">Every active warehouse, for the filter.</param>
public sealed record SuggestionResults(
    SuggestionSearchForm Search,
    IReadOnlyList<SuggestionRow> Rows,
    PagedResult<ReplenishmentSuggestionDto> Page,
    IReadOnlyList<WarehouseDto> Warehouses);

/// <summary>What the dismiss form asks for.</summary>
public sealed class DismissSuggestionForm
{
    /// <summary>The suggestion being taken off the list.</summary>
    [Required]
    public Guid SuggestionId { get; set; }

    /// <summary>
    /// Why.
    /// <para>
    /// Required, and required by the domain too. A suggestion dismissed without a reason is one
    /// the next person raises again next week, and the week after — the reason is the only thing
    /// that stops the list arguing with itself.
    /// </para>
    /// </summary>
    [Required(ErrorMessage = "Diga porquê — sem motivo, a sugestão volta a aparecer.")]
    [StringLength(200, MinimumLength = 3, ErrorMessage = "O motivo tem de ter pelo menos 3 caracteres.")]
    [Display(Name = "Motivo")]
    public string Reason { get; set; } = string.Empty;

    /// <summary>Where to go back to, so the filters survive the action.</summary>
    public string? ReturnUrl { get; set; }
}

/// <summary>How the purchase order list is narrowed.</summary>
public sealed class OrderSearchForm
{
    /// <summary>How many rows a page shows.</summary>
    public const int PageSize = 40;

    /// <summary>An order number, or part of one.</summary>
    public string? Term { get; set; }

    /// <summary>One status only, when chosen.</summary>
    public string? Status { get; set; }

    /// <summary>One warehouse only, when chosen.</summary>
    public Guid? WarehouseId { get; set; }

    /// <summary>
    /// Whether to show only orders with something still to come.
    /// <para>
    /// On unless somebody turns it off. A buyer opening this screen is asking what is still owed
    /// to them, not what arrived last March — and once a company has traded for a year, the
    /// second list buries the first.
    /// </para>
    /// </summary>
    public bool? Outstanding { get; set; }

    /// <summary>Which page, one-based.</summary>
    public int Page { get; set; } = 1;

    /// <summary>Whether the outstanding-only filter is actually applied.</summary>
    public bool OutstandingOnly => Outstanding ?? true;
}

/// <summary>The purchase order list, and what the filters are chosen from.</summary>
/// <param name="Search">What was asked.</param>
/// <param name="Page">What came back.</param>
/// <param name="Warehouses">Every active warehouse, for the filter.</param>
public sealed record OrderResults(
    OrderSearchForm Search,
    PagedResult<PurchaseOrderSummary> Page,
    IReadOnlyList<WarehouseDto> Warehouses);

/// <summary>One purchase order, ready to be read.</summary>
/// <param name="Order">What Purchasing holds.</param>
/// <param name="WarehouseCode">
/// The delivery warehouse's code, from Inventory. The order carries the identifier and nothing
/// else, because a warehouse renamed in 2027 should not rewrite an order raised in 2026 — but a
/// person reading the screen wants the code, not a GUID.
/// </param>
public sealed record OrderDetail(PurchaseOrderDetail Order, string? WarehouseCode)
{
    /// <summary>What has arrived, as a share of what was ordered.</summary>
    /// <remarks>
    /// By value rather than by line count. Eleven of twelve lines received sounds like an order
    /// almost done; if the twelfth is the engine block, it is not.
    /// </remarks>
    public decimal ReceivedShare =>
        Order.Total <= 0m
            ? 0m
            : Math.Clamp((Order.Total - Order.OutstandingValue) / Order.Total, 0m, 1m);
}
