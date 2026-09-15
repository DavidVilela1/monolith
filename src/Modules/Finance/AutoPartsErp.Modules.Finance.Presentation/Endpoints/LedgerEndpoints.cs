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
/// HTTP routes for the chart of accounts and the general ledger.
/// <para>
/// There is no route that edits a posted entry, and there never will be. A correction is a second
/// entry that reverses the first, so a month somebody has already reported keeps saying what it
/// said.
/// </para>
/// </summary>
public sealed class LedgerEndpoints : IEndpointGroup
{
    /// <inheritdoc />
    public void Map(IEndpointRouteBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapPost("/accounts", OpenAccountAsync)
            .WithName("OpenAccount")
            .RequirePermission(Permissions.Finance.ManageChart)
            .WithSummary(
                "Open an account. A group account exists to be summed and takes no entries of "
                + "its own.")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/accounts/{accountId:guid}", AmendAccountAsync)
            .WithName("AmendAccount")
            .RequirePermission(Permissions.Finance.ManageChart)
            .WithSummary(
                "Rename an account or take it out of use. The code and the type do not move — an "
                + "account that measured something else is a different account.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/journal-entries", PostEntryAsync)
            .WithName("PostJournalEntry")
            .RequirePermission(Permissions.Finance.PostToLedger)
            .WithSummary(
                "Write an entry and post it. Debits have to equal credits to the cent, and once "
                + "it is posted nothing about it changes again.")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/journal-entries/{journalEntryId:guid}/reversal", ReverseEntryAsync)
            .WithName("ReverseJournalEntry")
            .RequirePermission(Permissions.Finance.PostToLedger)
            .WithSummary(
                "Reverse a posted entry with a second one that mirrors it. The only way to undo a "
                + "posting; the original keeps saying what it said.")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<IResult> OpenAccountAsync(
        IDispatcher dispatcher,
        OpenAccountCommand command,
        CancellationToken cancellationToken)
    {
        Result<Guid> result = await dispatcher.SendAsync(command, cancellationToken);

        return result.ToCreated(id => $"/api/finance/accounts/{id}");
    }

    private static async Task<IResult> AmendAccountAsync(
        IDispatcher dispatcher,
        Guid accountId,
        AmendAccountRequest body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        Result result = await dispatcher.SendAsync(
            new AmendAccountCommand(accountId, body.Name, body.IsActive), cancellationToken);

        return result.ToNoContent();
    }

    private static async Task<IResult> PostEntryAsync(
        IDispatcher dispatcher,
        PostJournalEntryCommand command,
        CancellationToken cancellationToken)
    {
        Result<Guid> result = await dispatcher.SendAsync(command, cancellationToken);

        return result.ToCreated(id => $"/api/finance/journal-entries/{id}");
    }

    private static async Task<IResult> ReverseEntryAsync(
        IDispatcher dispatcher,
        Guid journalEntryId,
        ReverseEntryRequest body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        Result<Guid> result = await dispatcher.SendAsync(
            new ReverseJournalEntryCommand(journalEntryId, body.EntryDate, body.Reason),
            cancellationToken);

        return result.ToCreated(id => $"/api/finance/journal-entries/{id}");
    }
}

/// <summary>Body of a request that renames an account or takes it out of use.</summary>
/// <param name="Name">The new name.</param>
/// <param name="IsActive">False to stop it taking new entries.</param>
public sealed record AmendAccountRequest(string Name, bool IsActive);

/// <summary>Body of a request that reverses a posted entry.</summary>
/// <param name="EntryDate">The day the reversal belongs to.</param>
/// <param name="Reason">Why.</param>
public sealed record ReverseEntryRequest(DateOnly EntryDate, string Reason);
