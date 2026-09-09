using AutoPartsErp.Modules.Abstractions.Http;
using AutoPartsErp.Modules.Abstractions.Modules;
using AutoPartsErp.Modules.Finance.Application.Contracts;
using AutoPartsErp.Modules.Finance.Application.Receipts.Commands;
using AutoPartsErp.Modules.Finance.Application.Receivables.Commands;
using AutoPartsErp.Modules.Finance.Application.Receivables.Queries;
using AutoPartsErp.SharedKernel.Authorization;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Paging;
using AutoPartsErp.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AutoPartsErp.Modules.Finance.Presentation.Endpoints;

/// <summary>What customers owe: statements, ageing, and offsetting credit notes.</summary>
public sealed class ReceivableEndpoints : IEndpointGroup
{
    /// <inheritdoc />
    public void Map(IEndpointRouteBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/customers/{customerId:guid}/statement", GetStatementAsync)
            .WithName("GetCustomerStatement")
            .RequirePermission(Permissions.Finance.Read)
            .WithSummary(
                "What a customer owes and the documents behind it. Pass asAt to reproduce a "
                + "statement as it looked on the day it was sent; it defaults to today, which is "
                + "what decides how much of the balance counts as overdue.")
            .Produces<CustomerStatement>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/customers/{customerId:guid}/open-items", ListOutstandingAsync)
            .WithName("ListOutstandingItems")
            .RequirePermission(Permissions.Finance.Read)
            .WithSummary(
                "The documents on a customer's account with something still outstanding, oldest "
                + "first. What an allocation screen shows somebody holding a receipt.")
            .Produces<IReadOnlyList<OpenItemDto>>(StatusCodes.Status200OK);

        group.MapGet("/aging", GetAgingAsync)
            .WithName("GetAging")
            .RequirePermission(Permissions.Finance.Read)
            .WithSummary(
                "Every customer with a balance, in the usual thirty-day buckets. Credit notes "
                + "count against the bucket their own due date falls in, so a customer with more "
                + "credit than debt shows a negative total rather than being left out.")
            .Produces<PagedResult<AgingRow>>(StatusCodes.Status200OK);

        group.MapPost("/credit-notes/{openItemId:guid}/allocate", AllocateCreditAsync)
            .WithName("AllocateCreditNote")
            .RequirePermission(Permissions.Finance.RecordReceipt)
            .WithSummary(
                "Offsets a credit note against documents the customer owes. Separate from issuing "
                + "the note, because what it is set against is a decision somebody makes "
                + "afterwards — often the invoice it was drawn from, often enough the oldest thing "
                + "outstanding instead.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<IResult> GetStatementAsync(
        IDispatcher dispatcher,
        Guid customerId,
        DateOnly? asAt,
        CancellationToken cancellationToken)
    {
        Result<CustomerStatement> result = await dispatcher.SendAsync(
            new GetCustomerStatementQuery(customerId, asAt), cancellationToken);

        return result.ToOk();
    }

    private static async Task<IResult> ListOutstandingAsync(
        IDispatcher dispatcher,
        Guid customerId,
        DateOnly? asAt,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<OpenItemDto>> result = await dispatcher.SendAsync(
            new ListOutstandingItemsQuery(customerId, asAt), cancellationToken);

        return result.ToOk();
    }

    private static async Task<IResult> GetAgingAsync(
        IDispatcher dispatcher,
        DateOnly? asAt,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        Result<PagedResult<AgingRow>> result = await dispatcher.SendAsync(
            new GetAgingQuery(asAt, PageRequest.Of(page, pageSize)), cancellationToken);

        return result.ToOk();
    }

    private static async Task<IResult> AllocateCreditAsync(
        IDispatcher dispatcher,
        Guid openItemId,
        AllocationRequest body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        Result result = await dispatcher.SendAsync(
            new AllocateCreditNoteCommand(openItemId, body.Allocations), cancellationToken);

        return result.ToNoContent();
    }
}

/// <summary>Body of an allocation request.</summary>
/// <param name="Allocations">Which documents are settled, and how much of each.</param>
public sealed record AllocationRequest(IReadOnlyList<AllocationLine> Allocations);
