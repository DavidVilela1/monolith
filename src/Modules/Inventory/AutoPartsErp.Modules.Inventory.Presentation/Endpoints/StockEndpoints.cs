using AutoPartsErp.Modules.Abstractions.Http;
using AutoPartsErp.Modules.Abstractions.Modules;
using AutoPartsErp.Modules.Inventory.Application.Contracts;
using AutoPartsErp.Modules.Inventory.Application.Stock.Commands;
using AutoPartsErp.Modules.Inventory.Application.Stock.Queries;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Paging;
using AutoPartsErp.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AutoPartsErp.Modules.Inventory.Presentation.Endpoints;

/// <summary>HTTP routes for stock.</summary>
public sealed class StockEndpoints : IEndpointGroup
{
    /// <inheritdoc />
    public void Map(IEndpointRouteBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        RouteGroupBuilder stock = group.MapGroup("/stock");

        stock.MapGet("/parts/{partId:guid}", GetPartStockAsync)
            .WithName("GetPartStock")
            .WithSummary("Stock for a part across every warehouse, with totals.")
            .Produces<PartStockPosition>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        stock.MapGet("/parts/{partId:guid}/warehouses/{warehouseId:guid}", GetBalanceAsync)
            .WithName("GetStockBalance")
            .WithSummary("The balance for one part in one warehouse.")
            .Produces<StockBalance>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        stock.MapGet("/parts/{partId:guid}/movements", GetMovementsAsync)
            .WithName("GetStockMovements")
            .WithSummary("The ledger for a part: every movement, newest first.")
            .Produces<PagedResult<StockMovementDto>>();

        stock.MapGet("/parts/{partId:guid}/warehouses/{warehouseId:guid}/reservations", GetReservationsAsync)
            .WithName("GetStockReservations")
            .WithSummary("Claims currently held against a balance.")
            .Produces<IReadOnlyList<ReservationDto>>();

        stock.MapGet("/replenishment", GetReplenishmentAsync)
            .WithName("GetReplenishmentList")
            .WithSummary("Everything at or below its reorder point, deepest shortfall first.")
            .Produces<PagedResult<StockBalance>>();

        stock.MapPost("/receive", ReceiveAsync)
            .WithName("ReceiveStock")
            .WithSummary("Bring stock into a warehouse against a document.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        stock.MapPost("/issue", IssueAsync)
            .WithName("IssueStock")
            .WithSummary("Take stock out against a document.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        stock.MapPost("/adjust", AdjustAsync)
            .WithName("AdjustStock")
            .WithSummary("Correct a balance to a counted figure. A written reason is required.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        stock.MapPost("/reservations", ReserveAsync)
            .WithName("ReserveStock")
            .WithSummary("Hold stock back for a quote or order without moving it.")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        stock.MapDelete(
            "/parts/{partId:guid}/warehouses/{warehouseId:guid}/reservations/{reservationId:guid}",
            ReleaseAsync)
            .WithName("ReleaseReservation")
            .WithSummary("Give a claim back, returning its quantity to available stock.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        stock.MapPost(
            "/parts/{partId:guid}/warehouses/{warehouseId:guid}/reservations/{reservationId:guid}/fulfil",
            FulfilAsync)
            .WithName("FulfilReservation")
            .WithSummary("Issue the stock a claim was holding: the picker took it.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        stock.MapPut("/parts/{partId:guid}/warehouses/{warehouseId:guid}/replenishment", SetPolicyAsync)
            .WithName("SetReplenishmentPolicy")
            .WithSummary("Set the reorder point and quantity, or clear both.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem();
    }

    private static async Task<IResult> GetPartStockAsync(
        IDispatcher dispatcher,
        Guid partId,
        CancellationToken cancellationToken)
    {
        Result<PartStockPosition> result =
            await dispatcher.SendAsync(new GetPartStockQuery(partId), cancellationToken);

        return result.ToOk();
    }

    private static async Task<IResult> GetBalanceAsync(
        IDispatcher dispatcher,
        Guid partId,
        Guid warehouseId,
        CancellationToken cancellationToken)
    {
        Result<StockBalance> result =
            await dispatcher.SendAsync(new GetStockBalanceQuery(partId, warehouseId), cancellationToken);

        return result.ToOk();
    }

    private static async Task<IResult> GetMovementsAsync(
        IDispatcher dispatcher,
        Guid partId,
        Guid? warehouseId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int page = 1,
        int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        Result<PagedResult<StockMovementDto>> result = await dispatcher.SendAsync(
            new GetStockMovementsQuery(partId, warehouseId, from, to, page, pageSize),
            cancellationToken);

        return result.ToOk();
    }

    private static async Task<IResult> GetReservationsAsync(
        IDispatcher dispatcher,
        Guid partId,
        Guid warehouseId,
        bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        Result<IReadOnlyList<ReservationDto>> result = await dispatcher.SendAsync(
            new GetReservationsQuery(partId, warehouseId, activeOnly),
            cancellationToken);

        return result.ToOk();
    }

    private static async Task<IResult> GetReplenishmentAsync(
        IDispatcher dispatcher,
        Guid? warehouseId,
        int page = 1,
        int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        Result<PagedResult<StockBalance>> result = await dispatcher.SendAsync(
            new GetReplenishmentListQuery(warehouseId, page, pageSize),
            cancellationToken);

        return result.ToOk();
    }

    private static async Task<IResult> ReceiveAsync(
        IDispatcher dispatcher,
        ReceiveStockCommand command,
        CancellationToken cancellationToken)
    {
        Result result = await dispatcher.SendAsync(command, cancellationToken);
        return result.ToNoContent();
    }

    private static async Task<IResult> IssueAsync(
        IDispatcher dispatcher,
        IssueStockCommand command,
        CancellationToken cancellationToken)
    {
        Result result = await dispatcher.SendAsync(command, cancellationToken);
        return result.ToNoContent();
    }

    private static async Task<IResult> AdjustAsync(
        IDispatcher dispatcher,
        AdjustStockCommand command,
        CancellationToken cancellationToken)
    {
        Result result = await dispatcher.SendAsync(command, cancellationToken);
        return result.ToNoContent();
    }

    private static async Task<IResult> ReserveAsync(
        IDispatcher dispatcher,
        ReserveStockCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result<Guid> result = await dispatcher.SendAsync(command, cancellationToken);

        return result.ToCreated(id =>
            $"/api/inventory/stock/parts/{command.PartId}/warehouses/{command.WarehouseId}/reservations/{id}");
    }

    private static async Task<IResult> ReleaseAsync(
        IDispatcher dispatcher,
        Guid partId,
        Guid warehouseId,
        Guid reservationId,
        CancellationToken cancellationToken)
    {
        Result result = await dispatcher.SendAsync(
            new ReleaseReservationCommand(partId, warehouseId, reservationId),
            cancellationToken);

        return result.ToNoContent();
    }

    private static async Task<IResult> FulfilAsync(
        IDispatcher dispatcher,
        Guid partId,
        Guid warehouseId,
        Guid reservationId,
        CancellationToken cancellationToken)
    {
        Result result = await dispatcher.SendAsync(
            new FulfilReservationCommand(partId, warehouseId, reservationId),
            cancellationToken);

        return result.ToNoContent();
    }

    private static async Task<IResult> SetPolicyAsync(
        IDispatcher dispatcher,
        Guid partId,
        Guid warehouseId,
        ReplenishmentPolicyRequest body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        Result result = await dispatcher.SendAsync(
            new Application.Warehouses.SetReplenishmentPolicyCommand(
                partId, warehouseId, body.ReorderPoint, body.ReorderQuantity),
            cancellationToken);

        return result.ToNoContent();
    }
}

/// <summary>Body of a replenishment policy request. Send both nulls to clear the policy.</summary>
/// <param name="ReorderPoint">The level that triggers a reorder.</param>
/// <param name="ReorderQuantity">How much to order when it does.</param>
public sealed record ReplenishmentPolicyRequest(decimal? ReorderPoint, decimal? ReorderQuantity);
