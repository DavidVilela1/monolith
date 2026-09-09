using AutoPartsErp.Modules.Abstractions.Http;
using AutoPartsErp.Modules.Abstractions.Modules;
using AutoPartsErp.Modules.Finance.Application.Contracts;
using AutoPartsErp.Modules.Finance.Application.Receipts.Commands;
using AutoPartsErp.Modules.Finance.Application.Receivables.Queries;
using AutoPartsErp.SharedKernel.Authorization;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Paging;
using AutoPartsErp.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AutoPartsErp.Modules.Finance.Presentation.Endpoints;

/// <summary>Money received, and what it paid.</summary>
public sealed class ReceiptEndpoints : IEndpointGroup
{
    /// <inheritdoc />
    public void Map(IEndpointRouteBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapPost("/receipts", RecordAsync)
            .WithName("RecordReceipt")
            .RequirePermission(Permissions.Finance.RecordReceipt)
            .WithSummary(
                "Records money received. The allocations are optional: a transfer arrives with a "
                + "reference nobody can read, and the customer's balance should show it whether or "
                + "not anybody has yet worked out which invoices it was for.")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/receipts/{receiptId:guid}/allocate", AllocateAsync)
            .WithName("AllocateReceipt")
            .RequirePermission(Permissions.Finance.RecordReceipt)
            .WithSummary(
                "Matches money already received against documents the customer owes. Refused as a "
                + "whole if any line is wrong, so a part-applied allocation is not a state that "
                + "can be reached.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/receipts/{receiptId:guid}", GetAsync)
            .WithName("GetReceipt")
            .RequirePermission(Permissions.Finance.Read)
            .WithSummary("One receipt, with the documents it paid.")
            .Produces<ReceiptDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/receipts", SearchAsync)
            .WithName("SearchReceipts")
            .RequirePermission(Permissions.Finance.Read)
            .WithSummary(
                "Lists receipts, most recent first. onlyUnallocated narrows it to money that has "
                + "arrived and not yet been matched, which is somebody's working list.")
            .Produces<PagedResult<ReceiptDto>>(StatusCodes.Status200OK);
    }

    private static async Task<IResult> RecordAsync(
        IDispatcher dispatcher,
        RecordReceiptRequest body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        Result<Guid> result = await dispatcher.SendAsync(
            new RecordReceiptCommand(
                body.CustomerId,
                body.Amount,
                body.CurrencyCode,
                body.ReceivedOn,
                body.Method,
                body.Reference,
                body.Notes,
                body.Allocations),
            cancellationToken);

        return result.ToCreated(id => $"/api/finance/receipts/{id}");
    }

    private static async Task<IResult> AllocateAsync(
        IDispatcher dispatcher,
        Guid receiptId,
        AllocationRequest body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        Result result = await dispatcher.SendAsync(
            new AllocateReceiptCommand(receiptId, body.Allocations), cancellationToken);

        return result.ToNoContent();
    }

    private static async Task<IResult> GetAsync(
        IDispatcher dispatcher,
        Guid receiptId,
        CancellationToken cancellationToken)
    {
        Result<ReceiptDto> result =
            await dispatcher.SendAsync(new GetReceiptQuery(receiptId), cancellationToken);

        return result.ToOk();
    }

    private static async Task<IResult> SearchAsync(
        IDispatcher dispatcher,
        Guid? customerId,
        DateOnly? from,
        DateOnly? to,
        bool? onlyUnallocated,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        Result<PagedResult<ReceiptDto>> result = await dispatcher.SendAsync(
            new SearchReceiptsQuery(
                new ReceiptSearchCriteria(customerId, from, to, onlyUnallocated ?? false),
                PageRequest.Of(page, pageSize)),
            cancellationToken);

        return result.ToOk();
    }
}

/// <summary>Body of a request to record money received.</summary>
/// <param name="CustomerId">Who paid.</param>
/// <param name="Amount">How much arrived.</param>
/// <param name="CurrencyCode">The currency it arrived in.</param>
/// <param name="ReceivedOn">The date it arrived. Defaults to today.</param>
/// <param name="Method">Cash, BankTransfer, Cheque, Card, DirectDebit or Other.</param>
/// <param name="Reference">The bank reference or cheque number.</param>
/// <param name="Notes">Anything else worth recording.</param>
/// <param name="Allocations">What it pays, when that is already known.</param>
public sealed record RecordReceiptRequest(
    Guid CustomerId,
    decimal Amount,
    string CurrencyCode = "EUR",
    DateOnly? ReceivedOn = null,
    string Method = "BankTransfer",
    string? Reference = null,
    string? Notes = null,
    IReadOnlyList<AllocationLine>? Allocations = null);
