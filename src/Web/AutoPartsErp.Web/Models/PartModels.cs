using AutoPartsErp.Modules.Catalog.Application.Contracts;
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
/// The search form, what it found, and the lists the filters are chosen from.
/// <para>
/// One model rather than three pieces of <c>ViewData</c>: a filter that renders the brand it was
/// given but cannot name it is the sort of thing that survives review because it only shows up
/// once somebody actually picks a brand.
/// </para>
/// </summary>
/// <param name="Search">What was asked.</param>
/// <param name="Results">What came back.</param>
/// <param name="Brands">Every active brand, for the filter.</param>
/// <param name="Categories">Every active family, for the filter.</param>
public sealed record PartSearchResults(
    PartSearchForm Search,
    PagedResult<PartSummary> Results,
    IReadOnlyList<BrandDto> Brands,
    IReadOnlyList<CategoryDto> Categories)
{
    /// <summary>The chosen brand's name, when one is chosen.</summary>
    public string? BrandName =>
        Brands.FirstOrDefault(brand => brand.Id == Search.BrandId)?.Name;

    /// <summary>The chosen family's name, when one is chosen.</summary>
    public string? CategoryName =>
        Categories.FirstOrDefault(category => category.Id == Search.CategoryId)?.Name;
}
