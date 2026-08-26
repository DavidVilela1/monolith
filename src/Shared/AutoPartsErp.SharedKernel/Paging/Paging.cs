namespace AutoPartsErp.SharedKernel.Paging;

/// <summary>
/// A request for one page of results. A distributor's catalogue runs to hundreds of
/// thousands of lines, so every list endpoint pages by construction; there is no
/// "return everything" overload to reach for by accident.
/// </summary>
public sealed record PageRequest
{
    /// <summary>The largest page any caller may ask for.</summary>
    public const int MaxPageSize = 200;

    /// <summary>The page size used when the caller does not specify one.</summary>
    public const int DefaultPageSize = 50;

    private readonly int _page = 1;
    private readonly int _pageSize = DefaultPageSize;

    /// <summary>One-based page number. Values below 1 are clamped to 1.</summary>
    public int Page
    {
        get => _page;
        init => _page = value < 1 ? 1 : value;
    }

    /// <summary>Rows per page, clamped to [1, <see cref="MaxPageSize"/>].</summary>
    public int PageSize
    {
        get => _pageSize;
        init => _pageSize = Math.Clamp(value < 1 ? DefaultPageSize : value, 1, MaxPageSize);
    }

    /// <summary>Optional free-text filter applied by the query handler.</summary>
    public string? Search { get; init; }

    /// <summary>Property name to sort by. The handler validates it against an allow-list.</summary>
    public string? SortBy { get; init; }

    /// <summary>True to sort descending.</summary>
    public bool SortDescending { get; init; }

    /// <summary>Rows to skip, derived from <see cref="Page"/> and <see cref="PageSize"/>.</summary>
    public int Skip => (Page - 1) * PageSize;

    /// <summary>Creates a page request, applying the clamping rules above.</summary>
    public static PageRequest Of(int? page, int? pageSize) =>
        new() { Page = page ?? 1, PageSize = pageSize ?? DefaultPageSize };
}

/// <summary>One page of results together with the totals a UI needs to render a pager.</summary>
/// <typeparam name="T">The row type.</typeparam>
public sealed record PagedResult<T>
{
    /// <summary>The rows on this page.</summary>
    public required IReadOnlyList<T> Items { get; init; }

    /// <summary>The one-based page number these rows came from.</summary>
    public required int Page { get; init; }

    /// <summary>The requested page size.</summary>
    public required int PageSize { get; init; }

    /// <summary>Total rows matching the filter across all pages.</summary>
    public required int TotalCount { get; init; }

    /// <summary>Total number of pages.</summary>
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    /// <summary>True when a previous page exists.</summary>
    public bool HasPrevious => Page > 1;

    /// <summary>True when a further page exists.</summary>
    public bool HasNext => Page < TotalPages;

    /// <summary>Builds a page of results.</summary>
    public static PagedResult<T> Create(IReadOnlyList<T> items, int page, int pageSize, int totalCount) =>
        new()
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
        };

    /// <summary>An empty page, used when a filter matches nothing.</summary>
    public static PagedResult<T> Empty(int page, int pageSize) =>
        Create([], page, pageSize, 0);
}
