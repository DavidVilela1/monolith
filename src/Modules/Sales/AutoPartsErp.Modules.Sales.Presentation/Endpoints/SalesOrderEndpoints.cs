using AutoPartsErp.Modules.Abstractions.Http;
using AutoPartsErp.Modules.Abstractions.Modules;
using AutoPartsErp.Modules.Sales.Application.Contracts;
using AutoPartsErp.Modules.Sales.Application.Orders.Commands;
using AutoPartsErp.Modules.Sales.Application.Orders.Queries;
using AutoPartsErp.SharedKernel.Authorization;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Paging;
using AutoPartsErp.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AutoPartsErp.Modules.Sales.Presentation.Endpoints;

/// <summary>
/// HTTP routes for sales orders.
/// <para>
/// The two refusals worth knowing about both come back as 422 with a stable code: a held account
/// is <c>sales.customer.on_hold</c> and an order beyond the limit is
/// <c>sales.customer.credit_limit_exceeded</c>. A counter screen should be showing the reason,
/// not a generic failure — the person at the till has to say something to the customer.
/// </para>
/// </summary>
public sealed class SalesOrderEndpoints : IEndpointGroup
{
    /// <inheritdoc />
    public void Map(IEndpointRouteBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/orders", SearchAsync)
            .WithName("SearchSalesOrders")
            .RequirePermission(Permissions.Sales.Read)
            .WithSummary("Search sales orders. Pass outstandingOnly for the picking list.")
            .Produces<PagedResult<SalesOrderSummary>>();

        group.MapGet("/orders/{salesOrderId:guid}", GetAsync)
            .WithName("GetSalesOrder")
            .RequirePermission(Permissions.Sales.Read)
            .WithSummary("Get one sales order with its lines and totals.")
            .Produces<SalesOrderDetail>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/orders/by-number/{orderNumber}", GetByNumberAsync)
            .WithName("GetSalesOrderByNumber")
            .RequirePermission(Permissions.Sales.Read)
            .WithSummary("Get one sales order by the number the customer quotes on the phone.")
            .Produces<SalesOrderDetail>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/orders", CreateAsync)
            .WithName("CreateSalesOrder")
            .RequirePermission(Permissions.Sales.Manage)
            .WithSummary("Start an order. Code, name and currency come off the customer's account.")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/orders/{salesOrderId:guid}/lines", AddLineAsync)
            .WithName("AddSalesOrderLine")
            .RequirePermission(Permissions.Sales.Manage)
            .WithSummary("Add a part to a draft order.")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // The same second-route shape as confirm-over-limit below, for the same reason: the
        // permission has to be declarable on the route, and a flag in the body is not.
        group.MapPost("/orders/{salesOrderId:guid}/lines/below-floor", AddLineBelowFloorAsync)
            .WithName("AddSalesOrderLineBelowMarginFloor")
            .RequirePermission(Permissions.Sales.Manage)
            .RequirePermission(Permissions.Sales.OverrideMarginFloor)
            .WithSummary(
                "Add a part to a draft order even though it makes less than the customer's price "
                + "list allows. The line records that it was let through.")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPut("/orders/{salesOrderId:guid}/lines/{lineId:guid}/quantity", ChangeQuantityAsync)
            .WithName("ChangeSalesOrderLineQuantity")
            .RequirePermission(Permissions.Sales.Manage)
            .WithSummary("Change how much of a part is being sold. Draft only.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPut("/orders/{salesOrderId:guid}/lines/{lineId:guid}/pricing", ChangePricingAsync)
            .WithName("ChangeSalesOrderLinePricing")
            .RequirePermission(Permissions.Sales.Manage)
            .WithSummary("Change the price or discount on a line. Draft only.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPut("/orders/{salesOrderId:guid}/lines/{lineId:guid}/pricing-below-floor",
                ChangePricingBelowFloorAsync)
            .WithName("ChangeSalesOrderLinePricingBelowMarginFloor")
            .RequirePermission(Permissions.Sales.Manage)
            .RequirePermission(Permissions.Sales.OverrideMarginFloor)
            .WithSummary(
                "Price a line under the least the customer's price list allows. The line records "
                + "that it was let through, and forgets again if it is re-priced back above.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapDelete("/orders/{salesOrderId:guid}/lines/{lineId:guid}", RemoveLineAsync)
            .WithName("RemoveSalesOrderLine")
            .RequirePermission(Permissions.Sales.Manage)
            .WithSummary("Take a part off a draft order.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/orders/{salesOrderId:guid}/confirm", ConfirmAsync)
            .WithName("ConfirmSalesOrder")
            .RequirePermission(Permissions.Sales.Confirm)
            .WithSummary(
                "Agree the order. Checks stock, the account's hold and its credit, then claims the stock. "
                + "Pass allowBackorder to confirm without the stock being there.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // A second route rather than a flag on the first, and the reason is the permission. A
        // check that reads the body cannot be declared on the route, so it would have to live
        // inside the handler, where it is invisible to anybody reading the endpoint. Two routes
        // say what they need in the place somebody looks. This one needs both permissions: the
        // right to confirm, and the right to go past the limit while doing it.
        group.MapPost("/orders/{salesOrderId:guid}/confirm-over-limit", ConfirmOverLimitAsync)
            .WithName("ConfirmSalesOrderOverCreditLimit")
            .RequirePermission(Permissions.Sales.Confirm)
            .RequirePermission(Permissions.Sales.OverrideCredit)
            .WithSummary(
                "Agree the order even though it takes the account past its credit limit. The limit "
                + "only: an account on hold or closed is still refused. The order records that it "
                + "was let through.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/orders/{salesOrderId:guid}/lines/{lineId:guid}/dispatches", DispatchAsync)
            .WithName("DispatchSalesOrderLine")
            .RequirePermission(Permissions.Sales.Dispatch)
            .WithSummary("Record goods leaving. Inventory picks this up and takes them off the shelf.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/orders/{salesOrderId:guid}/cancel", CancelAsync)
            .WithName("CancelSalesOrder")
            .RequirePermission(Permissions.Sales.Manage)
            .WithSummary("Call the order off. Only before anything has gone out.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<IResult> SearchAsync(
        IDispatcher dispatcher,
        string? term,
        Guid? customerId,
        Guid? warehouseId,
        string? status,
        string? kind,
        bool outstandingOnly = false,
        bool lateOnly = false,
        int page = 1,
        int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        Result<PagedResult<SalesOrderSummary>> result = await dispatcher.SendAsync(
            new SearchSalesOrdersQuery(
                term, customerId, warehouseId, status, kind, outstandingOnly, lateOnly, page, pageSize),
            cancellationToken);

        return result.ToOk();
    }

    private static async Task<IResult> GetAsync(
        IDispatcher dispatcher,
        Guid salesOrderId,
        CancellationToken cancellationToken)
    {
        Result<SalesOrderDetail> result =
            await dispatcher.SendAsync(new GetSalesOrderQuery(salesOrderId), cancellationToken);

        return result.ToOk();
    }

    private static async Task<IResult> GetByNumberAsync(
        IDispatcher dispatcher,
        string orderNumber,
        CancellationToken cancellationToken)
    {
        Result<SalesOrderDetail> result =
            await dispatcher.SendAsync(new GetSalesOrderByNumberQuery(orderNumber), cancellationToken);

        return result.ToOk();
    }

    private static async Task<IResult> CreateAsync(
        IDispatcher dispatcher,
        CreateSalesOrderCommand command,
        CancellationToken cancellationToken)
    {
        Result<Guid> result = await dispatcher.SendAsync(command, cancellationToken);
        return result.ToCreated(id => $"/api/sales/orders/{id}");
    }

    private static Task<IResult> AddLineAsync(
        IDispatcher dispatcher,
        Guid salesOrderId,
        AddSalesLineRequest body,
        CancellationToken cancellationToken) =>
        AddLineCoreAsync(dispatcher, salesOrderId, body, overrideMarginFloor: false, cancellationToken);

    private static Task<IResult> AddLineBelowFloorAsync(
        IDispatcher dispatcher,
        Guid salesOrderId,
        AddSalesLineRequest body,
        CancellationToken cancellationToken) =>
        AddLineCoreAsync(dispatcher, salesOrderId, body, overrideMarginFloor: true, cancellationToken);

    // Named apart from the two route handlers above rather than overloading them. A minimal-API
    // route takes a method group, and a group with more than one candidate has nothing to bind to.
    private static async Task<IResult> AddLineCoreAsync(
        IDispatcher dispatcher,
        Guid salesOrderId,
        AddSalesLineRequest body,
        bool overrideMarginFloor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        Result<Guid> result = await dispatcher.SendAsync(
            new AddSalesOrderLineCommand(
                salesOrderId, body.PartId, body.Quantity, body.UnitPrice,
                body.DiscountPercent, body.VatRatePercent, overrideMarginFloor),
            cancellationToken);

        return result.ToCreated(lineId => $"/api/sales/orders/{salesOrderId}/lines/{lineId}");
    }

    private static async Task<IResult> ChangeQuantityAsync(
        IDispatcher dispatcher,
        Guid salesOrderId,
        Guid lineId,
        SalesQuantityRequest body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        Result result = await dispatcher.SendAsync(
            new ChangeSalesOrderLineQuantityCommand(salesOrderId, lineId, body.Quantity),
            cancellationToken);

        return result.ToNoContent();
    }

    private static Task<IResult> ChangePricingAsync(
        IDispatcher dispatcher,
        Guid salesOrderId,
        Guid lineId,
        PricingRequest body,
        CancellationToken cancellationToken) =>
        ChangePricingCoreAsync(
            dispatcher, salesOrderId, lineId, body, overrideMarginFloor: false, cancellationToken);

    private static Task<IResult> ChangePricingBelowFloorAsync(
        IDispatcher dispatcher,
        Guid salesOrderId,
        Guid lineId,
        PricingRequest body,
        CancellationToken cancellationToken) =>
        ChangePricingCoreAsync(
            dispatcher, salesOrderId, lineId, body, overrideMarginFloor: true, cancellationToken);

    private static async Task<IResult> ChangePricingCoreAsync(
        IDispatcher dispatcher,
        Guid salesOrderId,
        Guid lineId,
        PricingRequest body,
        bool overrideMarginFloor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        Result result = await dispatcher.SendAsync(
            new ChangeSalesOrderLinePricingCommand(
                salesOrderId, lineId, body.UnitPrice, body.DiscountPercent, overrideMarginFloor),
            cancellationToken);

        return result.ToNoContent();
    }

    private static async Task<IResult> RemoveLineAsync(
        IDispatcher dispatcher,
        Guid salesOrderId,
        Guid lineId,
        CancellationToken cancellationToken)
    {
        Result result = await dispatcher.SendAsync(
            new RemoveSalesOrderLineCommand(salesOrderId, lineId),
            cancellationToken);

        return result.ToNoContent();
    }

    private static async Task<IResult> ConfirmAsync(
        IDispatcher dispatcher,
        Guid salesOrderId,

        // Nullable: confirming without a promised date is the common case at a counter.
        ConfirmSalesRequest? body,
        CancellationToken cancellationToken)
    {
        Result result = await dispatcher.SendAsync(
            new ConfirmSalesOrderCommand(
                salesOrderId, body?.RequiredBy, body?.AllowBackorder ?? false),
            cancellationToken);

        return result.ToNoContent();
    }

    private static async Task<IResult> ConfirmOverLimitAsync(
        IDispatcher dispatcher,
        Guid salesOrderId,
        ConfirmSalesRequest? body,
        CancellationToken cancellationToken)
    {
        Result result = await dispatcher.SendAsync(
            new ConfirmSalesOrderCommand(
                salesOrderId,
                body?.RequiredBy,
                body?.AllowBackorder ?? false,
                OverrideCreditLimit: true),
            cancellationToken);

        return result.ToNoContent();
    }

    private static async Task<IResult> DispatchAsync(
        IDispatcher dispatcher,
        Guid salesOrderId,
        Guid lineId,
        SalesQuantityRequest body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        Result result = await dispatcher.SendAsync(
            new DispatchSalesOrderLineCommand(salesOrderId, lineId, body.Quantity),
            cancellationToken);

        return result.ToNoContent();
    }

    private static async Task<IResult> CancelAsync(
        IDispatcher dispatcher,
        Guid salesOrderId,
        CancelSalesRequest body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        Result result = await dispatcher.SendAsync(
            new CancelSalesOrderCommand(salesOrderId, body.Reason),
            cancellationToken);

        return result.ToNoContent();
    }
}

/// <summary>Body of an add-line request.</summary>
/// <param name="PartId">The part to sell.</param>
/// <param name="Quantity">How much to sell, in the part's stocking unit.</param>
/// <param name="UnitPrice">
/// Omit it and Pricing is asked. Supply it to set the price by hand, which the line records as
/// having no price list behind it.
/// </param>
/// <param name="DiscountPercent">
/// Omit it and the customer's agreed discount applies. Supply it to replace theirs.
/// </param>
/// <param name="VatRatePercent">The VAT rate, 0 to 100. Portugal's normal rate is 23.</param>
public sealed record AddSalesLineRequest(
    Guid PartId,
    decimal Quantity,
    decimal? UnitPrice = null,
    decimal? DiscountPercent = null,
    decimal VatRatePercent = 23m);

/// <summary>
/// Body of a request that carries a single quantity.
/// <para>
/// Named for its module rather than just "QuantityRequest". Swashbuckle keys schemas on the
/// short type name by default, and Purchasing already has one - two would take the whole
/// Swagger document down on the first request.
/// </para>
/// </summary>
/// <param name="Quantity">The quantity, in the unit the line was raised in.</param>
public sealed record SalesQuantityRequest(decimal Quantity);

/// <summary>Body of a repricing request.</summary>
/// <param name="UnitPrice">The new list price per unit.</param>
/// <param name="DiscountPercent">The new discount, 0 to 100.</param>
public sealed record PricingRequest(decimal UnitPrice, decimal DiscountPercent);

/// <summary>Body of a confirm request.</summary>
/// <param name="RequiredBy">When the customer wants it.</param>
/// <param name="AllowBackorder">
/// True to confirm even where there is not enough on the shelf. Off unless somebody says so.
/// </param>
public sealed record ConfirmSalesRequest(DateOnly? RequiredBy, bool AllowBackorder = false);

/// <summary>Body of a cancel request.</summary>
/// <param name="Reason">Why.</param>
public sealed record CancelSalesRequest(string Reason);
