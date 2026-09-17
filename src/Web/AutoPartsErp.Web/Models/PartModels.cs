using AutoPartsErp.Modules.Catalog.Application.Contracts;
using AutoPartsErp.SharedKernel.Paging;

namespace AutoPartsErp.Web.Models;

/// <summary>What the search box and its filters hold.</summary>
public sealed class PartSearchForm
{
    /// <summary>How many rows a page of the counter screen shows.</summary>
    public const int PageSize = 25;

    /// <summary>What was typed: a SKU, any number off the old part, or words from the name.</summary>
    public string? Term { get; set; }

    /// <summary>One lifecycle status only, when chosen.</summary>
    public string? Status { get; set; }

    /// <summary>Which page, one-based.</summary>
    public int Page { get; set; } = 1;
}

/// <summary>The search form and what it found, which a view needs together.</summary>
/// <param name="Search">What was asked.</param>
/// <param name="Results">What came back.</param>
public sealed record PartSearchResults(
    PartSearchForm Search,
    PagedResult<PartSummary> Results);
