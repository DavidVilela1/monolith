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
/// HTTP routes for the mapping from facts to account codes.
/// <para>
/// Configuration, not bookkeeping: nothing here posts anything. It is where an accountant says
/// that a sale's VAT belongs on 2433 and the cost of it on 61, so that the entries can be written
/// without anybody editing code.
/// </para>
/// </summary>
public sealed class PostingRuleEndpoints : IEndpointGroup
{
    /// <inheritdoc />
    public void Map(IEndpointRouteBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/postable-facts", ListFactsAsync)
            .WithName("ListPostableFacts")
            .RequirePermission(Permissions.Finance.ReadLedger)
            .WithSummary(
                "Every fact that can be mapped, the amounts each one carries, and whether it is "
                + "already mapped. What the configuration screen is built from.")
            .Produces<IReadOnlyList<PostableFactView>>();

        group.MapGet("/posting-rules", ListRulesAsync)
            .WithName("ListPostingRules")
            .RequirePermission(Permissions.Finance.ReadLedger)
            .WithSummary("Every mapping and what each one lands on.")
            .Produces<IReadOnlyList<PostingRuleView>>();

        group.MapPost("/posting-rules", DefineRuleAsync)
            .WithName("DefinePostingRule")
            .RequirePermission(Permissions.Finance.ManageChart)
            .WithSummary(
                "Map a fact to the accounts it lands on. One rule per fact — two would each post "
                + "their own version of it and the ledger would carry it twice.")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/posting-rules/{postingRuleId:guid}", AmendRuleAsync)
            .WithName("AmendPostingRule")
            .RequirePermission(Permissions.Finance.ManageChart)
            .WithSummary(
                "Replace a mapping's lines, or take it out of use. The lines go together: a rule "
                + "patched one line at a time posts one side of every fact that arrives meanwhile.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> ListFactsAsync(
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<PostableFactView>> result =
            await dispatcher.SendAsync(new ListPostableFactsQuery(), cancellationToken);

        return result.ToOk();
    }

    private static async Task<IResult> ListRulesAsync(
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<PostingRuleView>> result =
            await dispatcher.SendAsync(new ListPostingRulesQuery(), cancellationToken);

        return result.ToOk();
    }

    private static async Task<IResult> DefineRuleAsync(
        IDispatcher dispatcher,
        DefinePostingRuleCommand command,
        CancellationToken cancellationToken)
    {
        Result<Guid> result = await dispatcher.SendAsync(command, cancellationToken);

        return result.ToCreated(id => $"/api/finance/posting-rules/{id}");
    }

    private static async Task<IResult> AmendRuleAsync(
        IDispatcher dispatcher,
        Guid postingRuleId,
        AmendPostingRuleRequest body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        Result result = await dispatcher.SendAsync(
            new AmendPostingRuleCommand(
                postingRuleId, body.Description, body.Lines, body.IsActive),
            cancellationToken);

        return result.ToNoContent();
    }
}

/// <summary>Body of a request that replaces a mapping's lines.</summary>
/// <param name="Description">What the entries are for.</param>
/// <param name="Lines">Where each amount lands, from now on.</param>
/// <param name="IsActive">False to take the rule out of use.</param>
public sealed record AmendPostingRuleRequest(
    string Description,
    IReadOnlyList<PostingRuleLineInput> Lines,
    bool IsActive = true);
