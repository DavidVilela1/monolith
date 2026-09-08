using AutoPartsErp.Modules.Inventory.Domain;
using AutoPartsErp.Modules.Inventory.Domain.Counting;
using AutoPartsErp.Modules.Inventory.Domain.Stock;
using AutoPartsErp.Modules.Inventory.Domain.Warehouses;
using AutoPartsErp.SharedKernel.Abstractions;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Inventory.Application.Counting;

/// <summary>
/// Opens a count sheet and fills it with everything currently held in the warehouse.
/// <para>
/// The whole warehouse, deliberately, rather than a filter this module cannot honour. Counting
/// "all brake parts" means asking Catalog which parts those are, and Inventory does not get to
/// know what a category is — that would be a query contract, and it is the obvious next step
/// once somebody actually wants cycle counting by category. Until then the sheet covers the site.
/// </para>
/// </summary>
/// <param name="WarehouseId">The warehouse to count.</param>
/// <param name="CountedOn">The day the count is for.</param>
/// <param name="Notes">Anything worth recording about it.</param>
/// <param name="IncludeZeroBalances">
/// Whether to put parts the system believes are absent on the sheet. Off by default and the
/// default is the interesting one: a shelf the system says is empty is exactly where unrecorded
/// stock hides, but including every part the warehouse has ever held turns a two-hour count into
/// a two-day one. Off means "check what we think we have"; on means "check the whole aisle".
/// </param>
public sealed record OpenStockCountCommand(
    Guid WarehouseId,
    DateOnly CountedOn,
    string? Notes = null,
    bool IncludeZeroBalances = false) : ICommand<Guid>;

/// <summary>Opens the sheet and snapshots the balances onto it.</summary>
public sealed class OpenStockCountCommandHandler : ICommandHandler<OpenStockCountCommand, Guid>
{
    private readonly IStockCountRepository _counts;
    private readonly IStockCountScope _scope;
    private readonly IWarehouseRepository _warehouses;
    private readonly IInventoryUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public OpenStockCountCommandHandler(
        IStockCountRepository counts,
        IStockCountScope scope,
        IWarehouseRepository warehouses,
        IInventoryUnitOfWork unitOfWork)
    {
        _counts = counts;
        _scope = scope;
        _warehouses = warehouses;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result<Guid>> HandleAsync(
        OpenStockCountCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var warehouseId = new WarehouseId(request.WarehouseId);

        Warehouse? warehouse = await _warehouses
            .GetByIdAsync(warehouseId, cancellationToken)
            .ConfigureAwait(false);

        if (warehouse is null)
        {
            return Result.Failure<Guid>(
                InventoryErrors.Warehouse.NotFound(request.WarehouseId.ToString()));
        }

        string number = await _counts
            .NextCountNumberAsync(request.CountedOn.Year, cancellationToken)
            .ConfigureAwait(false);

        Result<StockCount> count = StockCount.Open(
            number, warehouseId, request.CountedOn, request.Notes);

        if (count.IsFailure)
        {
            return Result.Failure<Guid>(count.Error);
        }

        IReadOnlyList<CountableStock> stock = await _scope
            .ListAsync(warehouseId, request.IncludeZeroBalances, cancellationToken)
            .ConfigureAwait(false);

        foreach (CountableStock item in stock)
        {
            Result<StockCountLineId> line = count.Value.AddLine(
                item.PartId, item.Sku, item.Description, item.OnHand);

            if (line.IsFailure)
            {
                return Result.Failure<Guid>(line.Error);
            }
        }

        _counts.Add(count.Value);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return count.Value.Id.Value;
    }
}

/// <summary>
/// What is in a warehouse, flat, for putting on a sheet.
/// <para>
/// Its own abstraction rather than the stock repository, because opening a sheet for a warehouse
/// holding forty thousand parts must not load forty thousand aggregates with their reservations
/// and their expected deliveries. This reads four columns.
/// </para>
/// </summary>
public interface IStockCountScope
{
    /// <summary>Lists what should go on a sheet for the warehouse.</summary>
    /// <param name="warehouseId">The warehouse.</param>
    /// <param name="includeZeroBalances">Whether to include parts the system says are absent.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<CountableStock>> ListAsync(
        WarehouseId warehouseId,
        bool includeZeroBalances,
        CancellationToken cancellationToken = default);
}

/// <summary>One part in a warehouse, as a count sheet needs it.</summary>
/// <param name="PartId">The part.</param>
/// <param name="Sku">Its SKU, snapshotted onto the line so a printed sheet stays readable.</param>
/// <param name="Description">Its description, likewise.</param>
/// <param name="OnHand">What the system believes is on the shelf right now.</param>
public sealed record CountableStock(PartRef PartId, string Sku, string Description, Quantity OnHand);

/// <summary>Records what somebody found on a shelf.</summary>
/// <param name="StockCountId">The sheet.</param>
/// <param name="LineId">The line counted.</param>
/// <param name="CountedQuantity">What was found. Zero is a real answer; it means empty.</param>
public sealed record RecordCountCommand(
    Guid StockCountId,
    Guid LineId,
    decimal CountedQuantity) : ICommand;

/// <summary>Writes the counted figure onto the line.</summary>
public sealed class RecordCountCommandHandler : ICommandHandler<RecordCountCommand>
{
    private readonly IStockCountRepository _counts;
    private readonly IInventoryUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    /// <summary>Initializes the handler.</summary>
    public RecordCountCommandHandler(
        IStockCountRepository counts,
        IInventoryUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        IDateTimeProvider clock)
    {
        _counts = counts;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        RecordCountCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        StockCount? count = await _counts
            .GetWithLinesAsync(new StockCountId(request.StockCountId), cancellationToken)
            .ConfigureAwait(false);

        if (count is null)
        {
            return InventoryErrors.Count.NotFound(request.StockCountId.ToString());
        }

        StockCountLine? line = count.Lines
            .FirstOrDefault(item => item.Id.Value == request.LineId);

        if (line is null)
        {
            return InventoryErrors.Count.LineNotFound(request.LineId.ToString());
        }

        // The unit comes off the line's own snapshot rather than from the request. A counter
        // reports a number, not a unit of measure, and letting the request name one would invite
        // a count of "4 boxes" against a balance kept in pieces.
        Result<Quantity> counted = Quantity.Create(
            request.CountedQuantity, line.SystemQuantity.Unit);

        if (counted.IsFailure)
        {
            return Result.FromError(counted.Error);
        }

        Result recorded = count.RecordCount(
            line.Id, counted.Value, _currentUser.UserName, _clock.UtcNow);

        if (recorded.IsFailure)
        {
            return recorded;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>Says the counting is finished and the sheet needs reviewing.</summary>
/// <param name="StockCountId">The sheet.</param>
public sealed record SubmitStockCountCommand(Guid StockCountId) : ICommand;

/// <summary>Moves the sheet to awaiting review.</summary>
public sealed class SubmitStockCountCommandHandler : ICommandHandler<SubmitStockCountCommand>
{
    private readonly IStockCountRepository _counts;
    private readonly IInventoryUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    /// <summary>Initializes the handler.</summary>
    public SubmitStockCountCommandHandler(
        IStockCountRepository counts,
        IInventoryUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        IDateTimeProvider clock)
    {
        _counts = counts;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        SubmitStockCountCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        StockCount? count = await _counts
            .GetWithLinesAsync(new StockCountId(request.StockCountId), cancellationToken)
            .ConfigureAwait(false);

        if (count is null)
        {
            return InventoryErrors.Count.NotFound(request.StockCountId.ToString());
        }

        Result submitted = count.Submit(_currentUser.UserName, _clock.UtcNow);

        if (submitted.IsFailure)
        {
            return submitted;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>
/// Accepts the differences on a sheet and applies them to stock.
/// <para>
/// The one command here that changes a balance, and the only place a count ever touches one.
/// Everything before it is paperwork.
/// </para>
/// </summary>
/// <param name="StockCountId">The sheet to post.</param>
public sealed record PostStockCountCommand(Guid StockCountId) : ICommand<int>;

/// <summary>
/// Applies every counted line to its balance, in one transaction with the sheet's own closure.
/// <para>
/// Uncounted lines are skipped, and that is the single most important line of code in this file.
/// A line nobody reached has a null counted quantity, not a zero; posting it as zero would write
/// off every part the counter did not get to before going home, and the write-off would look
/// exactly like a real count.
/// </para>
/// <para>
/// A line whose counted figure already matches the live balance is skipped too — <c>AdjustTo</c>
/// refuses a change of nothing, and it is right to: a movement of zero explains nothing and makes
/// the ledger longer. Most lines on most sheets are this, which is what makes the ones that are
/// not worth looking at.
/// </para>
/// </summary>
public sealed class PostStockCountCommandHandler : ICommandHandler<PostStockCountCommand, int>
{
    private readonly IStockCountRepository _counts;
    private readonly IStockItemRepository _stockItems;
    private readonly IStockMovementRepository _movements;
    private readonly IInventoryUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    /// <summary>Initializes the handler.</summary>
    public PostStockCountCommandHandler(
        IStockCountRepository counts,
        IStockItemRepository stockItems,
        IStockMovementRepository movements,
        IInventoryUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        IDateTimeProvider clock)
    {
        _counts = counts;
        _stockItems = stockItems;
        _movements = movements;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _clock = clock;
    }

    /// <inheritdoc />
    /// <returns>How many balances were actually corrected.</returns>
    public async Task<Result<int>> HandleAsync(
        PostStockCountCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        StockCount? count = await _counts
            .GetWithLinesAsync(new StockCountId(request.StockCountId), cancellationToken)
            .ConfigureAwait(false);

        if (count is null)
        {
            return Result.Failure<int>(
                InventoryErrors.Count.NotFound(request.StockCountId.ToString()));
        }

        // Closes the sheet first, so a sheet that is not in a postable state fails before
        // anything has been applied to a balance.
        Result posted = count.Post(_currentUser.UserName, _clock.UtcNow);

        if (posted.IsFailure)
        {
            return Result.Failure<int>(posted.Error);
        }

        var corrections = new List<StockMovement>();

        foreach (StockCountLine line in count.Lines)
        {
            if (line.CountedQuantity is not { } counted)
            {
                continue;
            }

            StockItem? stockItem = await _stockItems
                .GetAsync(line.PartId, count.WarehouseId, cancellationToken)
                .ConfigureAwait(false);

            if (stockItem is null)
            {
                return Result.Failure<int>(InventoryErrors.Count.NoStockRecord(line.Sku));
            }

            // Against the live balance, not against the snapshot on the line. A count is an
            // assertion that the shelf holds this many; if something moved while the aisle was
            // being walked, the correction is still "make it what was counted". The snapshot
            // stays on the sheet so the difference between the two is visible afterwards, which
            // is how a stock loss is told apart from a sale nobody had entered yet.
            if (counted.Value == stockItem.OnHand.Value)
            {
                continue;
            }

            Result<MovementReference> reference = MovementReference.Create(
                ReferenceType.StockCount,
                count.Number,
                $"Counted by {line.CountedBy ?? "unknown"}, posted by {_currentUser.UserName}");

            if (reference.IsFailure)
            {
                return Result.Failure<int>(reference.Error);
            }

            Result<StockMovement> movement = stockItem.AdjustTo(
                counted.Value, reference.Value, _clock.UtcNow);

            if (movement.IsFailure)
            {
                // The whole sheet fails and nothing is saved. A partly posted count is the worst
                // possible outcome: some balances corrected, some not, and a sheet that says it
                // was applied. The usual cause is a count below what is already reserved, which
                // needs a person to decide which orders go short.
                return Result.Failure<int>(movement.Error);
            }

            corrections.Add(movement.Value);
        }

        _movements.AddRange(corrections);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return corrections.Count;
    }
}

/// <summary>Sends a submitted sheet back for more counting.</summary>
/// <param name="StockCountId">The sheet.</param>
public sealed record ReopenStockCountCommand(Guid StockCountId) : ICommand;

/// <summary>Abandons a sheet without touching stock.</summary>
/// <param name="StockCountId">The sheet.</param>
/// <param name="Reason">Why.</param>
public sealed record CancelStockCountCommand(Guid StockCountId, string? Reason) : ICommand;

/// <summary>Handles the two transitions that change nothing but the sheet's own state.</summary>
public sealed class StockCountStateCommandHandler
    : ICommandHandler<ReopenStockCountCommand>,
      ICommandHandler<CancelStockCountCommand>
{
    private readonly IStockCountRepository _counts;
    private readonly IInventoryUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public StockCountStateCommandHandler(
        IStockCountRepository counts,
        IInventoryUnitOfWork unitOfWork)
    {
        _counts = counts;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public Task<Result> HandleAsync(
        ReopenStockCountCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ApplyAsync(request.StockCountId, count => count.Reopen(), cancellationToken);
    }

    /// <inheritdoc />
    public Task<Result> HandleAsync(
        CancelStockCountCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ApplyAsync(
            request.StockCountId, count => count.Cancel(request.Reason), cancellationToken);
    }

    private async Task<Result> ApplyAsync(
        Guid stockCountId,
        Func<StockCount, Result> change,
        CancellationToken cancellationToken)
    {
        StockCount? count = await _counts
            .GetByIdAsync(new StockCountId(stockCountId), cancellationToken)
            .ConfigureAwait(false);

        if (count is null)
        {
            return InventoryErrors.Count.NotFound(stockCountId.ToString());
        }

        Result changed = change(count);

        if (changed.IsFailure)
        {
            return changed;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
