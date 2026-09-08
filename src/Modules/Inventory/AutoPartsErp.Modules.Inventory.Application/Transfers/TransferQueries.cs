using AutoPartsErp.Modules.Inventory.Domain;
using AutoPartsErp.Modules.Inventory.Domain.Transfers;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Inventory.Application.Transfers;

/// <summary>One transfer note, with every line and what is still on the van.</summary>
/// <param name="StockTransferId">The transfer.</param>
public sealed record GetStockTransferQuery(Guid StockTransferId) : IQuery<StockTransferNote>;

/// <summary>Serves the note from the write model — one document, read whole.</summary>
public sealed class GetStockTransferQueryHandler
    : IQueryHandler<GetStockTransferQuery, StockTransferNote>
{
    private readonly IStockTransferRepository _transfers;

    /// <summary>Initializes the handler.</summary>
    public GetStockTransferQueryHandler(IStockTransferRepository transfers)
    {
        _transfers = transfers;
    }

    /// <inheritdoc />
    public async Task<Result<StockTransferNote>> HandleAsync(
        GetStockTransferQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        StockTransfer? transfer = await _transfers
            .GetByIdAsync(new StockTransferId(request.StockTransferId), cancellationToken)
            .ConfigureAwait(false);

        return transfer is null
            ? Result.Failure<StockTransferNote>(
                InventoryErrors.Transfer.NotFound(request.StockTransferId.ToString()))
            : Describe(transfer);
    }

    /// <summary>Flattens a transfer into the shape a screen or a printed note needs.</summary>
    internal static StockTransferNote Describe(StockTransfer transfer) =>
        new(
            transfer.Id.Value,
            transfer.Number,
            transfer.FromWarehouseId.Value,
            transfer.ToWarehouseId.Value,
            transfer.Status.ToString(),
            transfer.DispatchedAtUtc,
            transfer.DispatchedBy,
            transfer.CompletedAtUtc,
            transfer.Notes,
            transfer.ClosureReason,
            [.. transfer.Lines
                .OrderBy(line => line.LineNumber)
                .Select(line => new StockTransferNoteLine(
                    line.Id.Value,
                    line.LineNumber,
                    line.PartId.Value,
                    line.Sku,
                    line.Description,
                    line.Quantity.Value,
                    line.Quantity.Unit.Code,
                    line.DispatchedQuantity.Value,
                    line.ReceivedQuantity.Value,
                    line.LostQuantity.Value,
                    line.InTransitQuantity.Value,
                    line.ValueInTransit?.Amount,
                    line.ValueInTransit?.Currency.Code))]);
}

/// <summary>
/// Everything on a van right now, newest first.
/// <para>
/// The question a warehouse manager asks every morning and the one that had no answer before this
/// document existed: what is somewhere between two of our own buildings.
/// </para>
/// </summary>
public sealed record GetTransfersInTransitQuery : IQuery<IReadOnlyList<StockTransferNote>>;

/// <summary>Serves the in-transit list.</summary>
public sealed class GetTransfersInTransitQueryHandler
    : IQueryHandler<GetTransfersInTransitQuery, IReadOnlyList<StockTransferNote>>
{
    private readonly IStockTransferRepository _transfers;

    /// <summary>Initializes the handler.</summary>
    public GetTransfersInTransitQueryHandler(IStockTransferRepository transfers)
    {
        _transfers = transfers;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<StockTransferNote>>> HandleAsync(
        GetTransfersInTransitQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        IReadOnlyList<StockTransfer> transfers = await _transfers
            .GetInTransitAsync(cancellationToken)
            .ConfigureAwait(false);

        return Result.Success<IReadOnlyList<StockTransferNote>>(
            [.. transfers.Select(GetStockTransferQueryHandler.Describe)]);
    }
}

/// <summary>A transfer note.</summary>
/// <param name="StockTransferId">The transfer.</param>
/// <param name="Number">Its number.</param>
/// <param name="FromWarehouseId">Where the stock left.</param>
/// <param name="ToWarehouseId">Where it is going.</param>
/// <param name="Status">Draft, InTransit, PartiallyReceived, Received, ClosedShort or Cancelled.</param>
/// <param name="DispatchedAtUtc">When the van left.</param>
/// <param name="DispatchedBy">Who sent it.</param>
/// <param name="CompletedAtUtc">When the last of it was accounted for.</param>
/// <param name="Notes">Anything recorded about the movement.</param>
/// <param name="ClosureReason">Why it was cancelled, or why a shortfall was accepted.</param>
/// <param name="Lines">What is being moved.</param>
public sealed record StockTransferNote(
    Guid StockTransferId,
    string Number,
    Guid FromWarehouseId,
    Guid ToWarehouseId,
    string Status,
    DateTimeOffset? DispatchedAtUtc,
    string? DispatchedBy,
    DateTimeOffset? CompletedAtUtc,
    string? Notes,
    string? ClosureReason,
    IReadOnlyList<StockTransferNoteLine> Lines);

/// <summary>One line of a transfer note.</summary>
/// <param name="LineId">The line.</param>
/// <param name="LineNumber">Its position on the note.</param>
/// <param name="PartId">The part.</param>
/// <param name="Sku">Its SKU when the transfer was raised.</param>
/// <param name="Description">Its description when the transfer was raised.</param>
/// <param name="Quantity">How much was asked for.</param>
/// <param name="Unit">The unit every figure here is in.</param>
/// <param name="DispatchedQuantity">How much left.</param>
/// <param name="ReceivedQuantity">How much has been booked in.</param>
/// <param name="LostQuantity">How much was written off as never having arrived.</param>
/// <param name="InTransitQuantity">What left and is neither here nor written off.</param>
/// <param name="ValueInTransit">
/// What the stock still on the van is worth, or null when the sending shelf had no value of its
/// own — stock that has never been through a priced receipt.
/// </param>
/// <param name="ValueCurrency">The currency of that value.</param>
public sealed record StockTransferNoteLine(
    Guid LineId,
    int LineNumber,
    Guid PartId,
    string Sku,
    string Description,
    decimal Quantity,
    string Unit,
    decimal DispatchedQuantity,
    decimal ReceivedQuantity,
    decimal LostQuantity,
    decimal InTransitQuantity,
    decimal? ValueInTransit,
    string? ValueCurrency);
