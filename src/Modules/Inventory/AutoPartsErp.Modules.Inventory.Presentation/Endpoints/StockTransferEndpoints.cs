using AutoPartsErp.Modules.Abstractions.Http;
using AutoPartsErp.Modules.Abstractions.Modules;
using AutoPartsErp.Modules.Inventory.Application.Transfers;
using AutoPartsErp.SharedKernel.Authorization;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AutoPartsErp.Modules.Inventory.Presentation.Endpoints;

/// <summary>
/// HTTP routes for moving stock between the company's own warehouses.
/// <para>
/// Build the note, dispatch it, and book in what turns up — possibly twice, possibly less than
/// left. The middle is the part that matters: between dispatch and receipt the goods belong to
/// neither warehouse, and `GET /transfers/in-transit` is the only place that says where they are.
/// </para>
/// </summary>
public sealed class StockTransferEndpoints : IEndpointGroup
{
    /// <inheritdoc />
    public void Map(IEndpointRouteBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        RouteGroupBuilder transfers = group.MapGroup("/transfers");

        transfers.MapPost("/", DraftAsync)
            .WithName("DraftStockTransfer")
            .RequirePermission(Permissions.Inventory.Transfer)
            .WithSummary("Raise an empty transfer between two warehouses.")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        transfers.MapGet("/in-transit", GetInTransitAsync)
            .WithName("GetTransfersInTransit")
            .RequirePermission(Permissions.Inventory.Read)
            .WithSummary("Everything on a van right now, newest first.")
            .Produces<IReadOnlyList<StockTransferNote>>();

        transfers.MapGet("/{stockTransferId:guid}", GetAsync)
            .WithName("GetStockTransfer")
            .RequirePermission(Permissions.Inventory.Read)
            .WithSummary("The note, with what has left, arrived and gone missing.")
            .Produces<StockTransferNote>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        transfers.MapPost("/{stockTransferId:guid}/lines", AddLineAsync)
            .WithName("AddTransferLine")
            .RequirePermission(Permissions.Inventory.Transfer)
            .WithSummary("Put a part on a draft transfer.")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        transfers.MapDelete("/{stockTransferId:guid}/lines/{lineId:guid}", RemoveLineAsync)
            .WithName("RemoveTransferLine")
            .RequirePermission(Permissions.Inventory.Transfer)
            .WithSummary("Take a line off a draft transfer.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        transfers.MapPost("/{stockTransferId:guid}/dispatch", DispatchAsync)
            .WithName("DispatchStockTransfer")
            .RequirePermission(Permissions.Inventory.Transfer)
            .WithSummary("Send it: takes the stock off the sending shelves and puts it on the van.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        transfers.MapPost("/{stockTransferId:guid}/lines/{lineId:guid}/receive", ReceiveAsync)
            .WithName("ReceiveStockTransferLine")
            .RequirePermission(Permissions.Inventory.Transfer)
            .WithSummary("Book in what turned up, at the value it left with.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        transfers.MapPost("/{stockTransferId:guid}/close-short", CloseShortAsync)
            .WithName("CloseStockTransferShort")
            .RequirePermission(Permissions.Inventory.Adjust)
            .WithSummary("Accept that what is still on the van never arrived. A reason is required.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        transfers.MapPost("/{stockTransferId:guid}/cancel", CancelAsync)
            .WithName("CancelStockTransfer")
            .RequirePermission(Permissions.Inventory.Transfer)
            .WithSummary("Call off a draft transfer. Only possible before anything has left.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<IResult> DraftAsync(
        IDispatcher dispatcher,
        DraftStockTransferCommand command,
        CancellationToken cancellationToken)
    {
        Result<Guid> result = await dispatcher.SendAsync(command, cancellationToken);

        return result.ToCreated(id => $"/api/inventory/transfers/{id}");
    }

    private static async Task<IResult> GetAsync(
        IDispatcher dispatcher,
        Guid stockTransferId,
        CancellationToken cancellationToken)
    {
        Result<StockTransferNote> result =
            await dispatcher.SendAsync(new GetStockTransferQuery(stockTransferId), cancellationToken);

        return result.ToOk();
    }

    private static async Task<IResult> GetInTransitAsync(
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<StockTransferNote>> result =
            await dispatcher.SendAsync(new GetTransfersInTransitQuery(), cancellationToken);

        return result.ToOk();
    }

    private static async Task<IResult> AddLineAsync(
        IDispatcher dispatcher,
        Guid stockTransferId,
        TransferLineRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Guid> result = await dispatcher.SendAsync(
            new AddTransferLineCommand(stockTransferId, request.PartId, request.Quantity),
            cancellationToken);

        return result.ToCreated(id => $"/api/inventory/transfers/{stockTransferId}/lines/{id}");
    }

    private static async Task<IResult> RemoveLineAsync(
        IDispatcher dispatcher,
        Guid stockTransferId,
        Guid lineId,
        CancellationToken cancellationToken)
    {
        Result result = await dispatcher.SendAsync(
            new RemoveTransferLineCommand(stockTransferId, lineId), cancellationToken);

        return result.ToNoContent();
    }

    private static async Task<IResult> DispatchAsync(
        IDispatcher dispatcher,
        Guid stockTransferId,
        CancellationToken cancellationToken)
    {
        Result result = await dispatcher.SendAsync(
            new DispatchStockTransferCommand(stockTransferId), cancellationToken);

        return result.ToNoContent();
    }

    private static async Task<IResult> ReceiveAsync(
        IDispatcher dispatcher,
        Guid stockTransferId,
        Guid lineId,
        ReceiveTransferRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result result = await dispatcher.SendAsync(
            new ReceiveStockTransferLineCommand(stockTransferId, lineId, request.Quantity),
            cancellationToken);

        return result.ToNoContent();
    }

    private static async Task<IResult> CloseShortAsync(
        IDispatcher dispatcher,
        Guid stockTransferId,
        TransferClosureRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result result = await dispatcher.SendAsync(
            new CloseStockTransferShortCommand(stockTransferId, request.Reason), cancellationToken);

        return result.ToNoContent();
    }

    private static async Task<IResult> CancelAsync(
        IDispatcher dispatcher,
        Guid stockTransferId,
        TransferClosureRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result result = await dispatcher.SendAsync(
            new CancelStockTransferCommand(stockTransferId, request.Reason), cancellationToken);

        return result.ToNoContent();
    }
}

/// <summary>A part to move.</summary>
/// <param name="PartId">The part.</param>
/// <param name="Quantity">
/// How much. The unit is not asked for: it comes from the sending warehouse's balance, so a
/// transfer of "4 boxes" cannot be raised against stock kept in pieces.
/// </param>
public sealed record TransferLineRequest(Guid PartId, decimal Quantity);

/// <summary>What turned up.</summary>
/// <param name="Quantity">How much of the line arrived.</param>
public sealed record ReceiveTransferRequest(decimal Quantity);

/// <summary>Why a transfer was cancelled, or why a shortfall was accepted.</summary>
/// <param name="Reason">The explanation.</param>
public sealed record TransferClosureRequest(string? Reason);
