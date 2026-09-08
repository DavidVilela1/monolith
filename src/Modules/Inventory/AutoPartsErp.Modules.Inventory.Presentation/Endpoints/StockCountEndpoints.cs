using AutoPartsErp.Modules.Abstractions.Http;
using AutoPartsErp.Modules.Abstractions.Modules;
using AutoPartsErp.Modules.Inventory.Application.Counting;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AutoPartsErp.Modules.Inventory.Presentation.Endpoints;

/// <summary>
/// HTTP routes for physical stock counts.
/// <para>
/// Five steps, and the shape of them is the feature. Opening a sheet snapshots what the system
/// believes; counting records what somebody found; submitting says the walking is done; posting
/// applies the differences; and posting is a different call from submitting so that it can be a
/// different person. Collapsing any two of those back into one gives back the single adjustment
/// command this replaced.
/// </para>
/// </summary>
public sealed class StockCountEndpoints : IEndpointGroup
{
    /// <inheritdoc />
    public void Map(IEndpointRouteBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        RouteGroupBuilder counts = group.MapGroup("/counts");

        counts.MapPost("/", OpenAsync)
            .WithName("OpenStockCount")
            .WithSummary("Open a count sheet for a warehouse, snapshotting what the system holds.")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        counts.MapGet("/{stockCountId:guid}", GetAsync)
            .WithName("GetStockCount")
            .WithSummary("The sheet, with every line and the differences on it.")
            .Produces<StockCountSheet>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        counts.MapPut("/{stockCountId:guid}/lines/{lineId:guid}", RecordAsync)
            .WithName("RecordStockCount")
            .WithSummary("Record what was found on a shelf. Zero means empty.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        counts.MapPost("/{stockCountId:guid}/submit", SubmitAsync)
            .WithName("SubmitStockCount")
            .WithSummary("Say the counting is finished and the sheet needs reviewing.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        counts.MapPost("/{stockCountId:guid}/reopen", ReopenAsync)
            .WithName("ReopenStockCount")
            .WithSummary("Send a submitted sheet back for more counting.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        counts.MapPost("/{stockCountId:guid}/post", PostAsync)
            .WithName("PostStockCount")
            .WithSummary("Accept the differences and apply them to stock. Returns how many balances changed.")
            .Produces<int>()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        counts.MapPost("/{stockCountId:guid}/cancel", CancelAsync)
            .WithName("CancelStockCount")
            .WithSummary("Abandon the sheet without touching stock. A reason is required.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<IResult> OpenAsync(
        IDispatcher dispatcher,
        OpenStockCountCommand command,
        CancellationToken cancellationToken)
    {
        Result<Guid> result = await dispatcher.SendAsync(command, cancellationToken);

        return result.ToCreated(id => $"/api/inventory/counts/{id}");
    }

    private static async Task<IResult> GetAsync(
        IDispatcher dispatcher,
        Guid stockCountId,
        CancellationToken cancellationToken)
    {
        Result<StockCountSheet> result =
            await dispatcher.SendAsync(new GetStockCountQuery(stockCountId), cancellationToken);

        return result.ToOk();
    }

    private static async Task<IResult> RecordAsync(
        IDispatcher dispatcher,
        Guid stockCountId,
        Guid lineId,
        RecordCountRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result result = await dispatcher.SendAsync(
            new RecordCountCommand(stockCountId, lineId, request.CountedQuantity),
            cancellationToken);

        return result.ToNoContent();
    }

    private static async Task<IResult> SubmitAsync(
        IDispatcher dispatcher,
        Guid stockCountId,
        CancellationToken cancellationToken)
    {
        Result result = await dispatcher.SendAsync(
            new SubmitStockCountCommand(stockCountId), cancellationToken);

        return result.ToNoContent();
    }

    private static async Task<IResult> ReopenAsync(
        IDispatcher dispatcher,
        Guid stockCountId,
        CancellationToken cancellationToken)
    {
        Result result = await dispatcher.SendAsync(
            new ReopenStockCountCommand(stockCountId), cancellationToken);

        return result.ToNoContent();
    }

    private static async Task<IResult> PostAsync(
        IDispatcher dispatcher,
        Guid stockCountId,
        CancellationToken cancellationToken)
    {
        Result<int> result = await dispatcher.SendAsync(
            new PostStockCountCommand(stockCountId), cancellationToken);

        return result.ToOk();
    }

    private static async Task<IResult> CancelAsync(
        IDispatcher dispatcher,
        Guid stockCountId,
        CancelStockCountRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result result = await dispatcher.SendAsync(
            new CancelStockCountCommand(stockCountId, request.Reason), cancellationToken);

        return result.ToNoContent();
    }
}

/// <summary>What was found on a shelf.</summary>
/// <param name="CountedQuantity">
/// The figure. Zero is a real answer and means the shelf was empty; a line nobody has been to is
/// simply never sent.
/// </param>
public sealed record RecordCountRequest(decimal CountedQuantity);

/// <summary>Why a count sheet was abandoned.</summary>
/// <param name="Reason">The explanation. Somebody will ask what happened to sheet 14.</param>
public sealed record CancelStockCountRequest(string? Reason);
