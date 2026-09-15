using AutoPartsErp.Modules.Abstractions.Http;
using AutoPartsErp.Modules.Abstractions.Modules;
using AutoPartsErp.Modules.Purchasing.Application.Agreements.Commands;
using AutoPartsErp.SharedKernel.Authorization;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AutoPartsErp.Modules.Purchasing.Presentation.Endpoints;

/// <summary>
/// HTTP routes for what suppliers owe the company for rebates.
/// <para>
/// A claim is raised on its own, when a rebate period closes owing something. These routes are
/// what a buyer does with it afterwards: ask, record what arrived, or give up and say why.
/// </para>
/// </summary>
public sealed class RappelClaimEndpoints : IEndpointGroup
{
    /// <inheritdoc />
    public void Map(IEndpointRouteBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/rappel-claims", ListOpenAsync)
            .WithName("ListOpenRappelClaims")
            .RequirePermission(Permissions.Purchasing.Read)
            .WithSummary(
                "Every rebate a supplier still owes, oldest period first. The list a buyer takes "
                + "into a supplier meeting.")
            .Produces<IReadOnlyList<RappelClaimView>>();

        group.MapPost("/rappel-claims/{claimId:guid}/sending", SendAsync)
            .WithName("SendRappelClaim")
            .RequirePermission(Permissions.Purchasing.ManageAgreements)
            .WithSummary("Record that the supplier has been asked.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/rappel-claims/{claimId:guid}/credits", CreditAsync)
            .WithName("CreditRappelClaim")
            .RequirePermission(Permissions.Purchasing.ManageAgreements)
            .WithSummary(
                "Record a credit note the supplier sent. Part of a claim can be credited; what is "
                + "left stays outstanding. It goes onto the period as well, so the year stops "
                + "reporting a shortfall that has been paid.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/rappel-claims/{claimId:guid}/write-off", WriteOffAsync)
            .WithName("WriteOffRappelClaim")
            .RequirePermission(Permissions.Purchasing.ManageAgreements)
            .WithSummary(
                "Give up on what is left, with a reason. Writing off money the company was owed "
                + "is a decision, and the sentence is what a buyer's successor needs.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/rappel-periods/closing", CloseDueAsync)
            .WithName("CloseDueRappelPeriods")
            .RequirePermission(Permissions.Purchasing.ManageAgreements)
            .WithSummary(
                "Close every rebate period that has ended, now, rather than waiting for the daily "
                + "sweep. Closing is what raises the claims.")
            .Produces<int>();
    }

    private static async Task<IResult> ListOpenAsync(
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<RappelClaimView>> result =
            await dispatcher.SendAsync(new ListOpenRappelClaimsQuery(), cancellationToken);

        return result.ToOk();
    }

    private static async Task<IResult> SendAsync(
        IDispatcher dispatcher,
        Guid claimId,
        SendClaimRequest? body,
        CancellationToken cancellationToken)
    {
        Result result = await dispatcher.SendAsync(
            new SendRappelClaimCommand(claimId, body?.Note), cancellationToken);

        return result.ToNoContent();
    }

    private static async Task<IResult> CreditAsync(
        IDispatcher dispatcher,
        Guid claimId,
        CreditClaimRequest body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        Result result = await dispatcher.SendAsync(
            new CreditRappelClaimCommand(claimId, body.Amount, body.CreditNoteNumber),
            cancellationToken);

        return result.ToNoContent();
    }

    private static async Task<IResult> WriteOffAsync(
        IDispatcher dispatcher,
        Guid claimId,
        ReasonRequest body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        Result result = await dispatcher.SendAsync(
            new WriteOffRappelClaimCommand(claimId, body.Reason), cancellationToken);

        return result.ToNoContent();
    }

    private static async Task<IResult> CloseDueAsync(
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        Result<int> result = await dispatcher.SendAsync(
            new CloseDueRappelPeriodsCommand(), cancellationToken);

        return result.ToOk();
    }
}

/// <summary>Body of a request that records a supplier having been asked.</summary>
/// <param name="Note">What was said, or how.</param>
public sealed record SendClaimRequest(string? Note);

/// <summary>Body of a request that records a credit note against a claim.</summary>
/// <param name="Amount">What the credit note is for.</param>
/// <param name="CreditNoteNumber">Their number for it.</param>
public sealed record CreditClaimRequest(decimal Amount, string? CreditNoteNumber);
