using AutoPartsErp.Modules.Abstractions.Http;
using AutoPartsErp.Modules.Abstractions.Modules;
using AutoPartsErp.Modules.Purchasing.Application.Contracts;
using AutoPartsErp.Modules.Purchasing.Application.Replenishment;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Paging;
using AutoPartsErp.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AutoPartsErp.Modules.Purchasing.Presentation.Endpoints;

/// <summary>
/// HTTP routes for the buyer's replenishment list.
/// <para>
/// Nothing here creates a suggestion. They arrive on their own, from Inventory's reorder-point
/// signal, and the only thing a person does with one is act on it or explain why not.
/// </para>
/// </summary>
public sealed class ReplenishmentEndpoints : IEndpointGroup
{
    /// <inheritdoc />
    public void Map(IEndpointRouteBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/suggestions", ListAsync)
            .WithName("ListReplenishmentSuggestions")
            .WithSummary("Parts that have run low, worst shortfall first. Open ones by default.")
            .Produces<PagedResult<ReplenishmentSuggestionDto>>();

        group.MapPost("/suggestions/{suggestionId:guid}/dismiss", DismissAsync)
            .WithName("DismissReplenishmentSuggestion")
            .WithSummary("Take a suggestion off the list without buying anything. A reason is required.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/suggestions/{suggestionId:guid}/order-line", AddToOrderAsync)
            .WithName("AddSuggestionToPurchaseOrder")
            .WithSummary("Add the suggested part to an existing draft order and mark it dealt with.")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<IResult> ListAsync(
        IDispatcher dispatcher,
        Guid? warehouseId,
        Guid? partId,
        string? status,
        int page = 1,
        int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        Result<PagedResult<ReplenishmentSuggestionDto>> result = await dispatcher.SendAsync(
            new ListReplenishmentSuggestionsQuery(warehouseId, partId, status, page, pageSize),
            cancellationToken);

        return result.ToOk();
    }

    private static async Task<IResult> DismissAsync(
        IDispatcher dispatcher,
        Guid suggestionId,
        DismissRequest body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        Result result = await dispatcher.SendAsync(
            new DismissReplenishmentSuggestionCommand(suggestionId, body.Reason),
            cancellationToken);

        return result.ToNoContent();
    }

    private static async Task<IResult> AddToOrderAsync(
        IDispatcher dispatcher,
        Guid suggestionId,
        AddSuggestionToOrderRequest body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        Result<Guid> result = await dispatcher.SendAsync(
            new AddSuggestionToPurchaseOrderCommand(
                suggestionId,
                body.PurchaseOrderId,
                body.UnitPrice,
                body.Quantity),
            cancellationToken);

        return result.ToCreated(
            lineId => $"/api/purchasing/orders/{body.PurchaseOrderId}/lines/{lineId}");
    }
}

/// <summary>Body of a dismiss request.</summary>
/// <param name="Reason">Why, so the next person does not raise it again.</param>
public sealed record DismissRequest(string Reason);

/// <summary>Body of a request that turns a suggestion into an order line.</summary>
/// <param name="PurchaseOrderId">The draft order to add it to.</param>
/// <param name="UnitPrice">The agreed price per unit, in the order's currency.</param>
/// <param name="Quantity">How much to order, when the buyer wants something other than the suggestion.</param>
public sealed record AddSuggestionToOrderRequest(
    Guid PurchaseOrderId,
    decimal UnitPrice,
    decimal? Quantity);
