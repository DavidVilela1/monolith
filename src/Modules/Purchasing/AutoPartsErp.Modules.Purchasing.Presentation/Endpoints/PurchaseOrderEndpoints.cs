using AutoPartsErp.Modules.Abstractions.Http;
using AutoPartsErp.Modules.Abstractions.Modules;
using AutoPartsErp.Modules.Purchasing.Application.Contracts;
using AutoPartsErp.Modules.Purchasing.Application.Orders.Commands;
using AutoPartsErp.Modules.Purchasing.Application.Orders.Queries;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Paging;
using AutoPartsErp.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AutoPartsErp.Modules.Purchasing.Presentation.Endpoints;

/// <summary>
/// HTTP routes for purchase orders.
/// <para>
/// The lifecycle is exposed as verbs on the document rather than as a status field somebody can
/// PUT to any value they like. <c>POST /submit</c> either works or comes back with the reason it
/// cannot — which is not the same conversation as "set status to Submitted".
/// </para>
/// </summary>
public sealed class PurchaseOrderEndpoints : IEndpointGroup
{
    /// <inheritdoc />
    public void Map(IEndpointRouteBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/orders", SearchAsync)
            .WithName("SearchPurchaseOrders")
            .WithSummary("Search purchase orders. Pass outstandingOnly for the buyer's working list.")
            .Produces<PagedResult<PurchaseOrderSummary>>();

        group.MapGet("/orders/{purchaseOrderId:guid}", GetAsync)
            .WithName("GetPurchaseOrder")
            .WithSummary("Get one purchase order with its lines.")
            .Produces<PurchaseOrderDetail>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/orders/by-number/{orderNumber}", GetByNumberAsync)
            .WithName("GetPurchaseOrderByNumber")
            .WithSummary("Get one purchase order by the number on the document.")
            .Produces<PurchaseOrderDetail>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/orders", CreateAsync)
            .WithName("CreatePurchaseOrder")
            .WithSummary("Start a draft order. The order number is assigned here.")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        group.MapPost("/orders/{purchaseOrderId:guid}/lines", AddLineAsync)
            .WithName("AddPurchaseOrderLine")
            .WithSummary("Add a part to a draft order.")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPut("/orders/{purchaseOrderId:guid}/lines/{lineId:guid}/quantity", ChangeLineQuantityAsync)
            .WithName("ChangePurchaseOrderLineQuantity")
            .WithSummary("Change how much of a part is being ordered. Draft only.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapDelete("/orders/{purchaseOrderId:guid}/lines/{lineId:guid}", RemoveLineAsync)
            .WithName("RemovePurchaseOrderLine")
            .WithSummary("Take a part off a draft order.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/orders/{purchaseOrderId:guid}/submit", SubmitAsync)
            .WithName("SubmitPurchaseOrder")
            .WithSummary("Send the order to the supplier. This is the point of commitment.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/orders/{purchaseOrderId:guid}/confirm", ConfirmAsync)
            .WithName("ConfirmPurchaseOrder")
            .WithSummary("Record the supplier's acknowledgement and the date they promised.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/orders/{purchaseOrderId:guid}/lines/{lineId:guid}/receipts", ReceiveAsync)
            .WithName("ReceivePurchaseOrderLine")
            .WithSummary("Book a delivery in against a line. Inventory picks this up and adds the stock.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/orders/{purchaseOrderId:guid}/cancel", CancelAsync)
            .WithName("CancelPurchaseOrder")
            .WithSummary("Call off an order before anything arrives. A reason is required.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/orders/{purchaseOrderId:guid}/close-short", CloseShortAsync)
            .WithName("ClosePurchaseOrderShort")
            .WithSummary("Accept a short delivery and stop chasing the balance.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<IResult> SearchAsync(
        IDispatcher dispatcher,
        string? term,
        Guid? supplierId,
        Guid? warehouseId,
        string? status,
        bool outstandingOnly = false,
        bool overdueOnly = false,
        int page = 1,
        int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        Result<PagedResult<PurchaseOrderSummary>> result = await dispatcher.SendAsync(
            new SearchPurchaseOrdersQuery(
                term, supplierId, warehouseId, status, outstandingOnly, overdueOnly, page, pageSize),
            cancellationToken);

        return result.ToOk();
    }

    private static async Task<IResult> GetAsync(
        IDispatcher dispatcher,
        Guid purchaseOrderId,
        CancellationToken cancellationToken)
    {
        Result<PurchaseOrderDetail> result =
            await dispatcher.SendAsync(new GetPurchaseOrderQuery(purchaseOrderId), cancellationToken);

        return result.ToOk();
    }

    private static async Task<IResult> GetByNumberAsync(
        IDispatcher dispatcher,
        string orderNumber,
        CancellationToken cancellationToken)
    {
        Result<PurchaseOrderDetail> result = await dispatcher.SendAsync(
            new GetPurchaseOrderByNumberQuery(orderNumber), cancellationToken);

        return result.ToOk();
    }

    private static async Task<IResult> CreateAsync(
        IDispatcher dispatcher,
        CreatePurchaseOrderCommand command,
        CancellationToken cancellationToken)
    {
        Result<Guid> result = await dispatcher.SendAsync(command, cancellationToken);
        return result.ToCreated(id => $"/api/purchasing/orders/{id}");
    }

    private static async Task<IResult> AddLineAsync(
        IDispatcher dispatcher,
        Guid purchaseOrderId,
        AddLineRequest body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        Result<Guid> result = await dispatcher.SendAsync(
            new AddPurchaseOrderLineCommand(
                purchaseOrderId, body.PartId, body.Sku, body.Description,
                body.Quantity, body.UnitCode, body.UnitPrice),
            cancellationToken);

        return result.ToCreated(lineId => $"/api/purchasing/orders/{purchaseOrderId}/lines/{lineId}");
    }

    private static async Task<IResult> ChangeLineQuantityAsync(
        IDispatcher dispatcher,
        Guid purchaseOrderId,
        Guid lineId,
        QuantityRequest body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        Result result = await dispatcher.SendAsync(
            new ChangePurchaseOrderLineQuantityCommand(purchaseOrderId, lineId, body.Quantity),
            cancellationToken);

        return result.ToNoContent();
    }

    private static async Task<IResult> RemoveLineAsync(
        IDispatcher dispatcher,
        Guid purchaseOrderId,
        Guid lineId,
        CancellationToken cancellationToken)
    {
        Result result = await dispatcher.SendAsync(
            new RemovePurchaseOrderLineCommand(purchaseOrderId, lineId),
            cancellationToken);

        return result.ToNoContent();
    }

    private static async Task<IResult> SubmitAsync(
        IDispatcher dispatcher,
        Guid purchaseOrderId,

        // Nullable on purpose: submitting without a promised date is the common case, and a
        // required body would mean every caller posting an empty object to say nothing.
        SubmitRequest? body,
        CancellationToken cancellationToken)
    {
        Result result = await dispatcher.SendAsync(
            new SubmitPurchaseOrderCommand(purchaseOrderId, body?.ExpectedOn),
            cancellationToken);

        return result.ToNoContent();
    }

    private static async Task<IResult> ConfirmAsync(
        IDispatcher dispatcher,
        Guid purchaseOrderId,
        ConfirmRequest body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        Result result = await dispatcher.SendAsync(
            new ConfirmPurchaseOrderCommand(purchaseOrderId, body.ExpectedOn, body.SupplierReference),
            cancellationToken);

        return result.ToNoContent();
    }

    private static async Task<IResult> ReceiveAsync(
        IDispatcher dispatcher,
        Guid purchaseOrderId,
        Guid lineId,
        QuantityRequest body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        Result result = await dispatcher.SendAsync(
            new ReceivePurchaseOrderLineCommand(purchaseOrderId, lineId, body.Quantity),
            cancellationToken);

        return result.ToNoContent();
    }

    private static async Task<IResult> CancelAsync(
        IDispatcher dispatcher,
        Guid purchaseOrderId,
        ReasonRequest body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        Result result = await dispatcher.SendAsync(
            new CancelPurchaseOrderCommand(purchaseOrderId, body.Reason),
            cancellationToken);

        return result.ToNoContent();
    }

    private static async Task<IResult> CloseShortAsync(
        IDispatcher dispatcher,
        Guid purchaseOrderId,
        ReasonRequest body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        Result result = await dispatcher.SendAsync(
            new ClosePurchaseOrderShortCommand(purchaseOrderId, body.Reason),
            cancellationToken);

        return result.ToNoContent();
    }
}

/// <summary>Body of an add-line request.</summary>
/// <param name="PartId">The part to buy.</param>
/// <param name="Sku">Its SKU, snapshotted onto the document.</param>
/// <param name="Description">Its description, snapshotted onto the document.</param>
/// <param name="Quantity">How much to order.</param>
/// <param name="UnitCode">The unit to order it in, e.g. EA, SET, L.</param>
/// <param name="UnitPrice">The agreed price per unit, in the order's currency.</param>
public sealed record AddLineRequest(
    Guid PartId,
    string Sku,
    string Description,
    decimal Quantity,
    string UnitCode,
    decimal UnitPrice);

/// <summary>Body of a request that carries a single quantity.</summary>
/// <param name="Quantity">The quantity, in the unit the line was raised in.</param>
public sealed record QuantityRequest(decimal Quantity);

/// <summary>Body of a submit request.</summary>
/// <param name="ExpectedOn">When delivery is expected, if a date is already known.</param>
public sealed record SubmitRequest(DateOnly? ExpectedOn);

/// <summary>Body of a confirm request.</summary>
/// <param name="ExpectedOn">The date the supplier committed to.</param>
/// <param name="SupplierReference">Their own order number.</param>
public sealed record ConfirmRequest(DateOnly ExpectedOn, string? SupplierReference);

/// <summary>Body of a request that needs an explanation.</summary>
/// <param name="Reason">Why.</param>
public sealed record ReasonRequest(string Reason);
