using AutoPartsErp.Modules.Inventory.Domain;
using AutoPartsErp.Modules.Inventory.Domain.Counting;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Inventory.Application.Counting;

/// <summary>One count sheet, with every line and the differences on it.</summary>
/// <param name="StockCountId">The sheet.</param>
public sealed record GetStockCountQuery(Guid StockCountId) : IQuery<StockCountSheet>;

/// <summary>
/// Serves the sheet from the write model rather than a read store.
/// <para>
/// The exception to how every other query in this system works, and the reason is size: a sheet
/// is bounded by the parts in one warehouse and is read by one person looking at one document.
/// The read store exists for questions asked across thousands of rows by screens that must stay
/// fast; this is one aggregate, loaded whole, being shown to whoever is holding it.
/// </para>
/// </summary>
public sealed class GetStockCountQueryHandler : IQueryHandler<GetStockCountQuery, StockCountSheet>
{
    private readonly IStockCountRepository _counts;

    /// <summary>Initializes the handler.</summary>
    public GetStockCountQueryHandler(IStockCountRepository counts)
    {
        _counts = counts;
    }

    /// <inheritdoc />
    public async Task<Result<StockCountSheet>> HandleAsync(
        GetStockCountQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        StockCount? count = await _counts
            .GetWithLinesAsync(new StockCountId(request.StockCountId), cancellationToken)
            .ConfigureAwait(false);

        if (count is null)
        {
            return Result.Failure<StockCountSheet>(
                InventoryErrors.Count.NotFound(request.StockCountId.ToString()));
        }

        return new StockCountSheet(
            count.Id.Value,
            count.Number,
            count.WarehouseId.Value,
            count.CountedOn,
            count.Status.ToString(),
            count.Notes,
            count.SubmittedBy,
            count.SubmittedAtUtc,
            count.PostedBy,
            count.PostedAtUtc,
            count.Lines.Count,
            count.CountedLines,
            count.UncountedLines,
            [.. count.Lines
                .OrderBy(line => line.LineNumber)
                .Select(line => new StockCountSheetLine(
                    line.Id.Value,
                    line.LineNumber,
                    line.PartId.Value,
                    line.Sku,
                    line.Description,
                    line.SystemQuantity.Value,
                    line.SystemQuantity.Unit.Code,
                    line.CountedQuantity?.Value,
                    line.Variance?.Value,
                    line.CountedBy,
                    line.CountedAtUtc))]);
    }
}

/// <summary>A count sheet as somebody reviewing it needs to see it.</summary>
/// <param name="StockCountId">The sheet.</param>
/// <param name="Number">Its number.</param>
/// <param name="WarehouseId">The warehouse counted.</param>
/// <param name="CountedOn">The day the count is for.</param>
/// <param name="Status">Open, Submitted, Posted or Cancelled.</param>
/// <param name="Notes">Anything recorded about it.</param>
/// <param name="SubmittedBy">Who said counting was finished.</param>
/// <param name="SubmittedAtUtc">When.</param>
/// <param name="PostedBy">Who accepted the differences.</param>
/// <param name="PostedAtUtc">When.</param>
/// <param name="TotalLines">How many parts were in scope.</param>
/// <param name="CountedLines">How many somebody went to.</param>
/// <param name="UncountedLines">
/// How many nobody reached. The figure a reviewer needs and the one nobody thinks to ask for: a
/// sheet with four hundred counted and sixty not is not a finished count of the warehouse,
/// however complete the totals look.
/// </param>
/// <param name="Lines">The sheet itself.</param>
public sealed record StockCountSheet(
    Guid StockCountId,
    string Number,
    Guid WarehouseId,
    DateOnly CountedOn,
    string Status,
    string? Notes,
    string? SubmittedBy,
    DateTimeOffset? SubmittedAtUtc,
    string? PostedBy,
    DateTimeOffset? PostedAtUtc,
    int TotalLines,
    int CountedLines,
    int UncountedLines,
    IReadOnlyList<StockCountSheetLine> Lines);

/// <summary>One line of a count sheet.</summary>
/// <param name="LineId">The line.</param>
/// <param name="LineNumber">Its position on the sheet.</param>
/// <param name="PartId">The part.</param>
/// <param name="Sku">Its SKU when the sheet was opened.</param>
/// <param name="Description">Its description when the sheet was opened.</param>
/// <param name="SystemQuantity">What the system believed was there when the sheet was opened.</param>
/// <param name="Unit">The unit both figures are in.</param>
/// <param name="CountedQuantity">
/// What was found, or null while nobody has been to this shelf. Null and zero are different
/// answers: zero means empty, null means nobody looked, and posting skips the second.
/// </param>
/// <param name="Variance">Counted less system, or null while uncounted.</param>
/// <param name="CountedBy">Who counted it.</param>
/// <param name="CountedAtUtc">When.</param>
public sealed record StockCountSheetLine(
    Guid LineId,
    int LineNumber,
    Guid PartId,
    string Sku,
    string Description,
    decimal SystemQuantity,
    string Unit,
    decimal? CountedQuantity,
    decimal? Variance,
    string? CountedBy,
    DateTimeOffset? CountedAtUtc);

/// <summary>
/// The count sheets, so one can be found again after it was created.
/// </summary>
/// <param name="WarehouseId">One warehouse only, when given.</param>
/// <param name="Status">Open, Submitted, Posted or Cancelled; all of them when omitted.</param>
/// <param name="Take">At most this many, newest first.</param>
public sealed record ListStockCountsQuery(
    Guid? WarehouseId = null,
    string? Status = null,
    int Take = 50) : IQuery<IReadOnlyList<StockCountSummary>>;

/// <summary>A count sheet as the list shows it.</summary>
/// <param name="StockCountId">The sheet.</param>
/// <param name="Number">Our number for it.</param>
/// <param name="WarehouseId">The warehouse counted.</param>
/// <param name="CountedOn">The day it is for.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="TotalLines">How many lines it has.</param>
/// <param name="CountedLines">How many have a figure on them.</param>
/// <param name="UncountedLines">How many are still blank — what is left to do.</param>
/// <param name="SubmittedBy">Who submitted it.</param>
/// <param name="SubmittedAtUtc">When.</param>
/// <param name="PostedBy">Who posted it.</param>
/// <param name="PostedAtUtc">When.</param>
public sealed record StockCountSummary(
    Guid StockCountId,
    string Number,
    Guid WarehouseId,
    DateOnly CountedOn,
    string Status,
    int TotalLines,
    int CountedLines,
    int UncountedLines,
    string? SubmittedBy,
    DateTimeOffset? SubmittedAtUtc,
    string? PostedBy,
    DateTimeOffset? PostedAtUtc);

/// <summary>Lists the count sheets.</summary>
public sealed class ListStockCountsQueryHandler
    : IQueryHandler<ListStockCountsQuery, IReadOnlyList<StockCountSummary>>
{
    private readonly IStockCountRepository _counts;

    /// <summary>Initializes the handler.</summary>
    public ListStockCountsQueryHandler(IStockCountRepository counts)
    {
        _counts = counts;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<StockCountSummary>>> HandleAsync(
        ListStockCountsQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        StockCountStatus? status = null;

        // An unrecognized status returns nothing rather than everything, the way every other
        // filter in this system does. Quietly ignoring one is how a person reads the wrong list.
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!Enum.TryParse(request.Status, ignoreCase: true, out StockCountStatus parsed)
                || parsed == StockCountStatus.Unknown)
            {
                return Result.Success<IReadOnlyList<StockCountSummary>>([]);
            }

            status = parsed;
        }

        IReadOnlyList<StockCount> counts = await _counts
            .ListAsync(
                request.WarehouseId is { } warehouse ? new WarehouseId(warehouse) : null,
                status,
                Math.Clamp(request.Take, 1, 200),
                cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<StockCountSummary> items =
        [
            .. counts.Select(count => new StockCountSummary(
                count.Id.Value,
                count.Number,
                count.WarehouseId.Value,
                count.CountedOn,
                count.Status.ToString(),
                count.Lines.Count,
                count.Lines.Count(line => line.CountedQuantity is not null),
                count.Lines.Count(line => line.CountedQuantity is null),
                count.SubmittedBy,
                count.SubmittedAtUtc,
                count.PostedBy,
                count.PostedAtUtc)),
        ];

        return Result.Success(items);
    }
}
