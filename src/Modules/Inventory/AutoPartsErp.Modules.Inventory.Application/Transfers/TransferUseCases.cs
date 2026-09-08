using AutoPartsErp.Modules.Inventory.Domain;
using AutoPartsErp.Modules.Inventory.Domain.Stock;
using AutoPartsErp.Modules.Inventory.Domain.Transfers;
using AutoPartsErp.Modules.Inventory.Domain.Warehouses;
using AutoPartsErp.SharedKernel.Abstractions;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Inventory.Application.Transfers;

/// <summary>Raises an empty transfer between two warehouses.</summary>
/// <param name="FromWarehouseId">Where the stock is leaving.</param>
/// <param name="ToWarehouseId">Where it is going.</param>
/// <param name="Notes">Anything worth recording about the movement.</param>
public sealed record DraftStockTransferCommand(
    Guid FromWarehouseId,
    Guid ToWarehouseId,
    string? Notes = null) : ICommand<Guid>;

/// <summary>Opens the transfer, after checking both ends exist and are open.</summary>
public sealed class DraftStockTransferCommandHandler
    : ICommandHandler<DraftStockTransferCommand, Guid>
{
    private readonly IStockTransferRepository _transfers;
    private readonly IWarehouseRepository _warehouses;
    private readonly IInventoryUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    /// <summary>Initializes the handler.</summary>
    public DraftStockTransferCommandHandler(
        IStockTransferRepository transfers,
        IWarehouseRepository warehouses,
        IInventoryUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _transfers = transfers;
        _warehouses = warehouses;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result<Guid>> HandleAsync(
        DraftStockTransferCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result from = await CheckWarehouseAsync(request.FromWarehouseId, cancellationToken)
            .ConfigureAwait(false);

        if (from.IsFailure)
        {
            return Result.Failure<Guid>(from.Error);
        }

        Result to = await CheckWarehouseAsync(request.ToWarehouseId, cancellationToken)
            .ConfigureAwait(false);

        if (to.IsFailure)
        {
            return Result.Failure<Guid>(to.Error);
        }

        string number = await _transfers
            .NextTransferNumberAsync(_clock.UtcNow.Year, cancellationToken)
            .ConfigureAwait(false);

        Result<StockTransfer> transfer = StockTransfer.Draft(
            number,
            new WarehouseId(request.FromWarehouseId),
            new WarehouseId(request.ToWarehouseId),
            request.Notes);

        if (transfer.IsFailure)
        {
            return Result.Failure<Guid>(transfer.Error);
        }

        _transfers.Add(transfer.Value);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return transfer.Value.Id.Value;
    }

    private async Task<Result> CheckWarehouseAsync(Guid warehouseId, CancellationToken cancellationToken)
    {
        Warehouse? warehouse = await _warehouses
            .GetByIdAsync(new WarehouseId(warehouseId), cancellationToken)
            .ConfigureAwait(false);

        if (warehouse is null)
        {
            return InventoryErrors.Warehouse.NotFound(warehouseId.ToString());
        }

        return warehouse.IsActive ? Result.Success() : InventoryErrors.Warehouse.Inactive;
    }
}

/// <summary>Puts a part on a draft transfer.</summary>
/// <param name="StockTransferId">The transfer.</param>
/// <param name="PartId">The part to move.</param>
/// <param name="Quantity">How much. Must be positive.</param>
public sealed record AddTransferLineCommand(
    Guid StockTransferId,
    Guid PartId,
    decimal Quantity) : ICommand<Guid>;

/// <summary>
/// Adds the line, taking the unit and the description from where they actually live.
/// <para>
/// The unit comes from the sending warehouse's balance rather than from the request, for the same
/// reason a count does: a person moving stock says how many, not in what unit, and letting the
/// request name one invites a transfer of "4 boxes" against a balance kept in pieces.
/// </para>
/// </summary>
public sealed class AddTransferLineCommandHandler : ICommandHandler<AddTransferLineCommand, Guid>
{
    private readonly IStockTransferRepository _transfers;
    private readonly IStockItemRepository _stockItems;
    private readonly ITransferPartNaming _naming;
    private readonly IInventoryUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public AddTransferLineCommandHandler(
        IStockTransferRepository transfers,
        IStockItemRepository stockItems,
        ITransferPartNaming naming,
        IInventoryUnitOfWork unitOfWork)
    {
        _transfers = transfers;
        _stockItems = stockItems;
        _naming = naming;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result<Guid>> HandleAsync(
        AddTransferLineCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        StockTransfer? transfer = await _transfers
            .GetByIdAsync(new StockTransferId(request.StockTransferId), cancellationToken)
            .ConfigureAwait(false);

        if (transfer is null)
        {
            return Result.Failure<Guid>(
                InventoryErrors.Transfer.NotFound(request.StockTransferId.ToString()));
        }

        var part = new PartRef(request.PartId);

        StockItem? stockItem = await _stockItems
            .GetAsync(part, transfer.FromWarehouseId, cancellationToken)
            .ConfigureAwait(false);

        if (stockItem is null)
        {
            return Result.Failure<Guid>(
                InventoryErrors.Stock.NotFound(
                    request.PartId.ToString(), transfer.FromWarehouseId.ToString()));
        }

        Result<Quantity> quantity = Quantity.Create(request.Quantity, stockItem.Unit);

        if (quantity.IsFailure)
        {
            return Result.Failure<Guid>(quantity.Error);
        }

        (string sku, string description) = await _naming
            .DescribeAsync(request.PartId, cancellationToken)
            .ConfigureAwait(false);

        Result<StockTransferLineId> line = transfer.AddLine(part, sku, description, quantity.Value);

        if (line.IsFailure)
        {
            return Result.Failure<Guid>(line.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return line.Value.Value;
    }
}

/// <summary>Names a part for a transfer note.</summary>
/// <remarks>
/// Its own abstraction so the Application project never learns that Catalog exists. The
/// implementation asks the catalogue; a part it does not recognize still goes on the note, because
/// something on a shelf has to be movable whatever the catalogue currently thinks of it.
/// </remarks>
public interface ITransferPartNaming
{
    /// <summary>The SKU and short description to snapshot onto a transfer line.</summary>
    /// <param name="partId">The part.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<(string Sku, string Description)> DescribeAsync(
        Guid partId,
        CancellationToken cancellationToken = default);
}

/// <summary>Takes a line off a draft transfer.</summary>
/// <param name="StockTransferId">The transfer.</param>
/// <param name="LineId">The line to remove.</param>
public sealed record RemoveTransferLineCommand(Guid StockTransferId, Guid LineId) : ICommand;

/// <summary>
/// Sends the goods: takes them off the sending balances and puts them on the van.
/// <para>
/// One transaction covering every line, and it has to be. A dispatch that took three lines off
/// the shelf and then failed on the fourth would leave stock that is neither in the warehouse nor
/// on a transfer — gone, with no document saying where.
/// </para>
/// </summary>
/// <param name="StockTransferId">The transfer to send.</param>
public sealed record DispatchStockTransferCommand(Guid StockTransferId) : ICommand;

/// <summary>Applies the departure to stock and records it on the transfer.</summary>
public sealed class DispatchStockTransferCommandHandler
    : ICommandHandler<DispatchStockTransferCommand>
{
    private readonly IStockTransferRepository _transfers;
    private readonly IStockItemRepository _stockItems;
    private readonly IWarehouseRepository _warehouses;
    private readonly IStockMovementRepository _movements;
    private readonly IInventoryUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    /// <summary>Initializes the handler.</summary>
    public DispatchStockTransferCommandHandler(
        IStockTransferRepository transfers,
        IStockItemRepository stockItems,
        IWarehouseRepository warehouses,
        IStockMovementRepository movements,
        IInventoryUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        IDateTimeProvider clock)
    {
        _transfers = transfers;
        _stockItems = stockItems;
        _warehouses = warehouses;
        _movements = movements;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        DispatchStockTransferCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        StockTransfer? transfer = await _transfers
            .GetByIdAsync(new StockTransferId(request.StockTransferId), cancellationToken)
            .ConfigureAwait(false);

        if (transfer is null)
        {
            return InventoryErrors.Transfer.NotFound(request.StockTransferId.ToString());
        }

        Warehouse? from = await _warehouses
            .GetByIdAsync(transfer.FromWarehouseId, cancellationToken)
            .ConfigureAwait(false);

        if (from is null)
        {
            return InventoryErrors.Warehouse.NotFound(transfer.FromWarehouseId.ToString());
        }

        if (!from.IsActive)
        {
            return InventoryErrors.Warehouse.Inactive;
        }

        Result<MovementReference> reference = MovementReference.Create(
            ReferenceType.StockTransfer,
            transfer.Number,
            $"To warehouse {transfer.ToWarehouseId}");

        if (reference.IsFailure)
        {
            return Result.FromError(reference.Error);
        }

        var values = new Dictionary<StockTransferLineId, Money?>();
        var departures = new List<StockMovement>();

        foreach (StockTransferLine line in transfer.Lines)
        {
            StockItem? stockItem = await _stockItems
                .GetAsync(line.PartId, transfer.FromWarehouseId, cancellationToken)
                .ConfigureAwait(false);

            if (stockItem is null)
            {
                return InventoryErrors.Stock.NotFound(line.Sku, from.Code);
            }

            Result<TransferredStock> dispatched = stockItem.Dispatch(
                line.Quantity.Value, reference.Value, _clock.UtcNow, from.AllowsNegativeStock);

            if (dispatched.IsFailure)
            {
                // The whole transfer fails and nothing is saved. Anything else leaves stock off
                // one shelf, not on another, and not on a document either.
                return Result.FromError(dispatched.Error);
            }

            values[line.Id] = dispatched.Value.Value;
            departures.Add(dispatched.Value.Movement);
        }

        Result sent = transfer.Dispatch(values, _currentUser.UserName, _clock.UtcNow);

        if (sent.IsFailure)
        {
            return sent;
        }

        _movements.AddRange(departures);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>Books in what turned up at the receiving warehouse.</summary>
/// <param name="StockTransferId">The transfer.</param>
/// <param name="LineId">The line arriving.</param>
/// <param name="Quantity">How much of it arrived. Must be positive.</param>
public sealed record ReceiveStockTransferLineCommand(
    Guid StockTransferId,
    Guid LineId,
    decimal Quantity) : ICommand;

/// <summary>
/// Puts the arrival on the receiving shelf at the value it left with.
/// <para>
/// The value is the whole point of routing this through the transfer rather than through a plain
/// receipt. Stock moving between two of the company's own shelves must not change what the company
/// owns; the transfer carries the figure so the receiving warehouse does not have to invent one.
/// </para>
/// </summary>
public sealed class ReceiveStockTransferLineCommandHandler
    : ICommandHandler<ReceiveStockTransferLineCommand>
{
    private readonly IStockTransferRepository _transfers;
    private readonly IStockItemRepository _stockItems;
    private readonly IWarehouseRepository _warehouses;
    private readonly IStockMovementRepository _movements;
    private readonly IInventoryUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    /// <summary>Initializes the handler.</summary>
    public ReceiveStockTransferLineCommandHandler(
        IStockTransferRepository transfers,
        IStockItemRepository stockItems,
        IWarehouseRepository warehouses,
        IStockMovementRepository movements,
        IInventoryUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _transfers = transfers;
        _stockItems = stockItems;
        _warehouses = warehouses;
        _movements = movements;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        ReceiveStockTransferLineCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        StockTransfer? transfer = await _transfers
            .GetByIdAsync(new StockTransferId(request.StockTransferId), cancellationToken)
            .ConfigureAwait(false);

        if (transfer is null)
        {
            return InventoryErrors.Transfer.NotFound(request.StockTransferId.ToString());
        }

        StockTransferLine? line = transfer.Lines
            .FirstOrDefault(item => item.Id.Value == request.LineId);

        if (line is null)
        {
            return InventoryErrors.Transfer.LineNotFound(request.LineId.ToString());
        }

        Warehouse? destination = await _warehouses
            .GetByIdAsync(transfer.ToWarehouseId, cancellationToken)
            .ConfigureAwait(false);

        if (destination is null)
        {
            return InventoryErrors.Warehouse.NotFound(transfer.ToWarehouseId.ToString());
        }

        if (!destination.IsActive)
        {
            return InventoryErrors.Warehouse.Inactive;
        }

        Result<Quantity> quantity = Quantity.Create(request.Quantity, line.Quantity.Unit);

        if (quantity.IsFailure)
        {
            return Result.FromError(quantity.Error);
        }

        StockItem? stockItem = await _stockItems
            .GetAsync(line.PartId, transfer.ToWarehouseId, cancellationToken)
            .ConfigureAwait(false);

        // A part with no balance at the destination is the ordinary case rather than an error:
        // the first time a branch stocks something, nothing there has ever held it. The record is
        // opened at zero and the arrival lands on it.
        if (stockItem is null)
        {
            Result<StockItem> opened = StockItem.Open(
                line.PartId, transfer.ToWarehouseId, line.Quantity.Unit);

            if (opened.IsFailure)
            {
                return Result.FromError(opened.Error);
            }

            stockItem = opened.Value;
            _stockItems.Add(stockItem);
        }

        if (stockItem.Unit != line.Quantity.Unit)
        {
            return InventoryErrors.Transfer.UnitMismatch(line.Sku, stockItem.Unit.Code);
        }

        // Taken off the transfer before it is put on the shelf, so a refusal - more arriving than
        // ever left - stops before any balance moves.
        Result<Money?> arrived = transfer.Receive(line.Id, quantity.Value, _clock.UtcNow);

        if (arrived.IsFailure)
        {
            return Result.FromError(arrived.Error);
        }

        Result<MovementReference> reference = MovementReference.Create(
            ReferenceType.StockTransfer,
            transfer.Number,
            $"From warehouse {transfer.FromWarehouseId}");

        if (reference.IsFailure)
        {
            return Result.FromError(reference.Error);
        }

        Result<StockMovement> movement = stockItem.ReceiveValued(
            quantity.Value.Value, reference.Value, _clock.UtcNow, arrived.Value);

        if (movement.IsFailure)
        {
            return Result.FromError(movement.Error);
        }

        _movements.Add(movement.Value);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>Accepts that what is still on the van is never arriving.</summary>
/// <param name="StockTransferId">The transfer.</param>
/// <param name="Reason">Why the shortfall was accepted.</param>
public sealed record CloseStockTransferShortCommand(
    Guid StockTransferId,
    string? Reason) : ICommand;

/// <summary>Calls off a draft transfer before anything has left.</summary>
/// <param name="StockTransferId">The transfer.</param>
/// <param name="Reason">Why.</param>
public sealed record CancelStockTransferCommand(Guid StockTransferId, string? Reason) : ICommand;

/// <summary>
/// Handles the transitions that touch no balance.
/// <para>
/// Closing short moves no stock, which surprises people and is right: the goods left the sending
/// shelf when the van did, and the receiving shelf never had them. What the write-off adds is a
/// document saying the difference was a loss rather than a delivery still in progress.
/// </para>
/// </summary>
public sealed class StockTransferStateCommandHandler
    : ICommandHandler<RemoveTransferLineCommand>,
      ICommandHandler<CloseStockTransferShortCommand>,
      ICommandHandler<CancelStockTransferCommand>
{
    private readonly IStockTransferRepository _transfers;
    private readonly IInventoryUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    /// <summary>Initializes the handler.</summary>
    public StockTransferStateCommandHandler(
        IStockTransferRepository transfers,
        IInventoryUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _transfers = transfers;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <inheritdoc />
    public Task<Result> HandleAsync(
        RemoveTransferLineCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ApplyAsync(
            request.StockTransferId,
            transfer => transfer.RemoveLine(new StockTransferLineId(request.LineId)),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<Result> HandleAsync(
        CloseStockTransferShortCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ApplyAsync(
            request.StockTransferId,
            transfer => transfer.CloseShort(request.Reason, _clock.UtcNow),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<Result> HandleAsync(
        CancelStockTransferCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ApplyAsync(
            request.StockTransferId,
            transfer => transfer.Cancel(request.Reason),
            cancellationToken);
    }

    private async Task<Result> ApplyAsync(
        Guid stockTransferId,
        Func<StockTransfer, Result> change,
        CancellationToken cancellationToken)
    {
        StockTransfer? transfer = await _transfers
            .GetByIdAsync(new StockTransferId(stockTransferId), cancellationToken)
            .ConfigureAwait(false);

        if (transfer is null)
        {
            return InventoryErrors.Transfer.NotFound(stockTransferId.ToString());
        }

        Result changed = change(transfer);

        if (changed.IsFailure)
        {
            return changed;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
