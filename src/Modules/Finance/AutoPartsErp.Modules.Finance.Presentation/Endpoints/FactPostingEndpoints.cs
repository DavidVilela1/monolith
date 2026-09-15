using AutoPartsErp.Modules.Abstractions.Http;
using AutoPartsErp.Modules.Abstractions.Modules;
using AutoPartsErp.Modules.Finance.Application.Ledger.Commands;
using AutoPartsErp.SharedKernel.Authorization;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AutoPartsErp.Modules.Finance.Presentation.Endpoints;

/// <summary>
/// HTTP routes for the facts that were handed to the ledger and have not reached it.
/// <para>
/// The screen that stops the general ledger being quietly incomplete. Every fact this system
/// produced is kept whether or not anything could post it, so an empty waiting list is a real
/// statement: everything that happened is in the books.
/// </para>
/// </summary>
public sealed class FactPostingEndpoints : IEndpointGroup
{
    /// <inheritdoc />
    public void Map(IEndpointRouteBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/waiting-facts", ListWaitingAsync)
            .WithName("ListWaitingFacts")
            .RequirePermission(Permissions.Finance.ReadLedger)
            .WithSummary(
                "Everything that happened and has not reached the ledger, oldest first, with why "
                + "each one is stuck. An empty list means the books are complete.")
            .Produces<IReadOnlyList<WaitingFactView>>();

        group.MapPost("/waiting-facts/posting", PostWaitingAsync)
            .WithName("PostWaitingFacts")
            .RequirePermission(Permissions.Finance.PostToLedger)
            .WithSummary(
                "Try the whole waiting list again. What somebody presses after filling in the "
                + "rules; a fact that still cannot post stops none of the others.")
            .Produces<PostWaitingFactsResult>();

        group.MapPost("/waiting-facts/{factPostingId:guid}/dismissal", DismissAsync)
            .WithName("DismissFactPosting")
            .RequirePermission(Permissions.Finance.PostToLedger)
            .WithSummary(
                "Take a fact off the waiting list without posting it, with a reason. Not a "
                + "delete — the row stays, because \"why is there no entry for 4471?\" needs an "
                + "answer.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/waiting-facts/{factPostingId:guid}/dismissal", ReinstateAsync)
            .WithName("ReinstateFactPosting")
            .RequirePermission(Permissions.Finance.PostToLedger)
            .WithSummary("Put a dismissed fact back on the waiting list.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> ListWaitingAsync(
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<WaitingFactView>> result =
            await dispatcher.SendAsync(new ListWaitingFactsQuery(), cancellationToken);

        return result.ToOk();
    }

    private static async Task<IResult> PostWaitingAsync(
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        Result<PostWaitingFactsResult> result =
            await dispatcher.SendAsync(new PostWaitingFactsCommand(), cancellationToken);

        return result.ToOk();
    }

    private static async Task<IResult> DismissAsync(
        IDispatcher dispatcher,
        Guid factPostingId,
        DismissFactRequest body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        Result result = await dispatcher.SendAsync(
            new DismissFactPostingCommand(factPostingId, body.Reason), cancellationToken);

        return result.ToNoContent();
    }

    private static async Task<IResult> ReinstateAsync(
        IDispatcher dispatcher,
        Guid factPostingId,
        CancellationToken cancellationToken)
    {
        Result result = await dispatcher.SendAsync(
            new ReinstateFactPostingCommand(factPostingId), cancellationToken);

        return result.ToNoContent();
    }
}

/// <summary>Body of a request that takes a fact off the waiting list.</summary>
/// <param name="Reason">Why it does not belong in the ledger.</param>
public sealed record DismissFactRequest(string Reason);
