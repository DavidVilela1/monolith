using AutoPartsErp.Modules.Abstractions.Http;
using AutoPartsErp.Modules.Abstractions.Modules;
using AutoPartsErp.Modules.Sales.Application.Contracts;
using AutoPartsErp.Modules.Sales.Application.Returns;
using AutoPartsErp.SharedKernel.Authorization;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Paging;
using AutoPartsErp.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AutoPartsErp.Modules.Sales.Presentation.Endpoints;

/// <summary>
/// HTTP routes for goods coming back from customers.
/// <para>
/// Two steps, and they are two routes for a reason. Raising a return records what the customer
/// says is coming; <c>/receive</c> records that it is here, and that is the one that moves stock.
/// A counter return does both in the same minute; a workshop ringing about a pump arriving next
/// Tuesday must not put a pump on the shelf today.
/// </para>
/// </summary>
public sealed class CustomerReturnEndpoints : IEndpointGroup
{
    /// <inheritdoc />
    public void Map(IEndpointRouteBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        RouteGroupBuilder returns = group.MapGroup("/returns");

        returns.MapGet("/", SearchAsync)
            .WithName("SearchCustomerReturns")
            .RequirePermission(Permissions.Sales.Read)
            .WithSummary("Search returns by number, order number or customer code.")
            .Produces<PagedResult<CustomerReturnSummary>>();

        returns.MapGet("/{customerReturnId:guid}", GetAsync)
            .WithName("GetCustomerReturn")
            .RequirePermission(Permissions.Sales.Read)
            .WithSummary("One return, with its lines and what the credit will come to.")
            .Produces<CustomerReturnDetail>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        returns.MapPost("/", RaiseAsync)
            .WithName("RaiseCustomerReturn")
            .RequirePermission(Permissions.Sales.Return)
            .WithSummary(
                "Raise a return against an order. The customer, the currency and the warehouse "
                + "the goods come back to are all taken from the order.")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        returns.MapPost("/{customerReturnId:guid}/lines", AddLineAsync)
            .WithName("AddCustomerReturnLine")
            .RequirePermission(Permissions.Sales.Return)
            .WithSummary(
                "Put a line of the original order on the return. The price comes off that line — "
                + "the customer gets back what they paid.")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        returns.MapDelete("/{customerReturnId:guid}/lines/{lineId:guid}", RemoveLineAsync)
            .WithName("RemoveCustomerReturnLine")
            .RequirePermission(Permissions.Sales.Return)
            .WithSummary("Take a line off a draft return.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        returns.MapPost("/{customerReturnId:guid}/receive", ReceiveAsync)
            .WithName("ReceiveCustomerReturn")
            .RequirePermission(Permissions.Sales.Return)
            .WithSummary(
                "Record that the goods are physically back. Saleable lines go to Inventory and "
                + "rejoin the balance; scrapped ones are credited and written off.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        returns.MapPost("/{customerReturnId:guid}/cancel", CancelAsync)
            .WithName("CancelCustomerReturn")
            .RequirePermission(Permissions.Sales.Return)
            .WithSummary("Call a draft return off. A reason is required.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<IResult> SearchAsync(
        IDispatcher dispatcher,
        string? term,
        Guid? customerId,
        string? status,
        int page = 1,
        int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        Result<PagedResult<CustomerReturnSummary>> result = await dispatcher.SendAsync(
            new SearchCustomerReturnsQuery(term, customerId, status, page, pageSize),
            cancellationToken);

        return result.ToOk();
    }

    private static async Task<IResult> GetAsync(
        IDispatcher dispatcher,
        Guid customerReturnId,
        CancellationToken cancellationToken)
    {
        Result<CustomerReturnDetail> result = await dispatcher.SendAsync(
            new GetCustomerReturnQuery(customerReturnId), cancellationToken);

        return result.ToOk();
    }

    private static async Task<IResult> RaiseAsync(
        IDispatcher dispatcher,
        RaiseCustomerReturnCommand command,
        CancellationToken cancellationToken)
    {
        Result<Guid> result = await dispatcher.SendAsync(command, cancellationToken);

        return result.ToCreated(id => $"/api/sales/returns/{id}");
    }

    private static async Task<IResult> AddLineAsync(
        IDispatcher dispatcher,
        Guid customerReturnId,
        AddCustomerReturnLineRequest body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        Result<Guid> result = await dispatcher.SendAsync(
            new AddCustomerReturnLineCommand(
                customerReturnId,
                body.SalesOrderLineId,
                body.Quantity,
                body.Disposition,
                body.ConditionNote),
            cancellationToken);

        return result.ToCreated(id => $"/api/sales/returns/{customerReturnId}/lines/{id}");
    }

    private static async Task<IResult> RemoveLineAsync(
        IDispatcher dispatcher,
        Guid customerReturnId,
        Guid lineId,
        CancellationToken cancellationToken)
    {
        Result result = await dispatcher.SendAsync(
            new RemoveCustomerReturnLineCommand(customerReturnId, lineId), cancellationToken);

        return result.ToNoContent();
    }

    private static async Task<IResult> ReceiveAsync(
        IDispatcher dispatcher,
        Guid customerReturnId,
        CancellationToken cancellationToken)
    {
        Result result = await dispatcher.SendAsync(
            new ReceiveCustomerReturnCommand(customerReturnId), cancellationToken);

        return result.ToNoContent();
    }

    private static async Task<IResult> CancelAsync(
        IDispatcher dispatcher,
        Guid customerReturnId,
        CancelReturnRequest body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        Result result = await dispatcher.SendAsync(
            new CancelCustomerReturnCommand(customerReturnId, body.Reason), cancellationToken);

        return result.ToNoContent();
    }
}

/// <summary>Body of an add-return-line request.</summary>
/// <param name="SalesOrderLineId">The line of the original order the goods came from.</param>
/// <param name="Quantity">How much is coming back.</param>
/// <param name="Disposition">BackToStock or Scrap for goods, Core for a returned old unit.</param>
/// <param name="ConditionNote">What state it arrived in.</param>
public sealed record AddCustomerReturnLineRequest(
    Guid SalesOrderLineId,
    decimal Quantity,
    string Disposition,
    string? ConditionNote);

/// <summary>Body of a cancel-return request.</summary>
/// <param name="Reason">Why the return is being called off.</param>
public sealed record CancelReturnRequest(string Reason);
