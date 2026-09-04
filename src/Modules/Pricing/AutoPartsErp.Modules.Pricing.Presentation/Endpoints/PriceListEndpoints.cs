using AutoPartsErp.Modules.Abstractions.Http;
using AutoPartsErp.Modules.Abstractions.Modules;
using AutoPartsErp.Modules.Pricing.Application.Contracts;
using AutoPartsErp.Modules.Pricing.Application.PriceLists.Commands;
using AutoPartsErp.Modules.Pricing.Application.PriceLists.Queries;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Paging;
using AutoPartsErp.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AutoPartsErp.Modules.Pricing.Presentation.Endpoints;

/// <summary>HTTP routes for price lists and the prices inside them.</summary>
public sealed class PriceListEndpoints : IEndpointGroup
{
    /// <inheritdoc />
    public void Map(IEndpointRouteBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/lists", SearchAsync)
            .WithName("SearchPriceLists")
            .WithSummary("Price lists, the default one first.")
            .Produces<PagedResult<PriceListSummary>>();

        group.MapGet("/lists/{priceListId:guid}", GetAsync)
            .WithName("GetPriceList")
            .WithSummary("One price list.")
            .Produces<PriceListSummary>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/lists/by-code/{code}", GetByCodeAsync)
            .WithName("GetPriceListByCode")
            .WithSummary("One price list, by the code people refer to it by.")
            .Produces<PriceListSummary>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/lists", OpenAsync)
            .WithName("OpenPriceList")
            .WithSummary("Open a price list, in draft. A promotion needs a last day.")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/lists/{priceListId:guid}", AmendAsync)
            .WithName("AmendPriceList")
            .WithSummary("Rename a list, or move the period it applies over.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/lists/{priceListId:guid}/activate", ActivateAsync)
            .WithName("ActivatePriceList")
            .WithSummary("Put a list into service. It has to price something first.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/lists/{priceListId:guid}/archive", ArchiveAsync)
            .WithName("ArchivePriceList")
            .WithSummary("Withdraw a list. Documents that quoted it still explain themselves.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/lists/{priceListId:guid}/make-default", MakeDefaultAsync)
            .WithName("MakeDefaultPriceList")
            .WithSummary("Make this the list customers with no agreement fall back to.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/lists/{priceListId:guid}/prices", ListPricesAsync)
            .WithName("ListPrices")
            .WithSummary("The prices in one list. Paged, because a standard list is enormous.")
            .Produces<PagedResult<PriceListEntryDto>>();

        group.MapGet("/lists/{priceListId:guid}/prices/{partId:guid}", GetPriceAsync)
            .WithName("GetPartPrice")
            .WithSummary("What one list says one part costs.")
            .Produces<PriceListEntryDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/lists/{priceListId:guid}/prices/{partId:guid}", SetPriceAsync)
            .WithName("SetPartPrice")
            .WithSummary(
                "Set the price from a quantity upwards. Adds the break, or corrects the one already there.")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapDelete("/lists/{priceListId:guid}/prices/{partId:guid}/breaks/{minimumQuantity:decimal}",
                RemoveBreakAsync)
            .WithName("RemovePriceBreak")
            .WithSummary("Remove one quantity break. The last one cannot go.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/lists/{priceListId:guid}/prices/{partId:guid}", RemovePriceAsync)
            .WithName("RemovePartPrice")
            .WithSummary("Take a part out of a list entirely.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> SearchAsync(
        IDispatcher dispatcher,
        string? term,
        string? kind,
        string? status,
        bool effectiveOnly = false,
        int page = 1,
        int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var criteria = new PriceListSearchCriteria
        {
            Term = term,
            Kind = kind,
            Status = status,
            EffectiveOnly = effectiveOnly,
        };

        Result<PagedResult<PriceListSummary>> result = await dispatcher.SendAsync(
            new SearchPriceListsQuery(criteria, PageRequest.Of(page, pageSize)),
            cancellationToken);

        return result.ToOk();
    }

    private static async Task<IResult> GetAsync(
        IDispatcher dispatcher,
        Guid priceListId,
        CancellationToken cancellationToken)
    {
        Result<PriceListSummary> result = await dispatcher.SendAsync(
            new GetPriceListQuery(priceListId), cancellationToken);

        return result.ToOk();
    }

    private static async Task<IResult> GetByCodeAsync(
        IDispatcher dispatcher,
        string code,
        CancellationToken cancellationToken)
    {
        Result<PriceListSummary> result = await dispatcher.SendAsync(
            new GetPriceListByCodeQuery(code), cancellationToken);

        return result.ToOk();
    }

    private static async Task<IResult> OpenAsync(
        IDispatcher dispatcher,
        OpenPriceListCommand command,
        CancellationToken cancellationToken)
    {
        Result<Guid> result = await dispatcher.SendAsync(command, cancellationToken);

        return result.ToCreated(id => $"/api/pricing/lists/{id}");
    }

    private static async Task<IResult> AmendAsync(
        IDispatcher dispatcher,
        Guid priceListId,
        AmendPriceListRequest body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        Result result = await dispatcher.SendAsync(
            new AmendPriceListCommand(priceListId, body.Name, body.EffectiveFrom, body.EffectiveTo),
            cancellationToken);

        return result.ToNoContent();
    }

    private static async Task<IResult> ActivateAsync(
        IDispatcher dispatcher,
        Guid priceListId,
        CancellationToken cancellationToken)
    {
        Result result = await dispatcher.SendAsync(
            new ActivatePriceListCommand(priceListId), cancellationToken);

        return result.ToNoContent();
    }

    private static async Task<IResult> ArchiveAsync(
        IDispatcher dispatcher,
        Guid priceListId,
        CancellationToken cancellationToken)
    {
        Result result = await dispatcher.SendAsync(
            new ArchivePriceListCommand(priceListId), cancellationToken);

        return result.ToNoContent();
    }

    private static async Task<IResult> MakeDefaultAsync(
        IDispatcher dispatcher,
        Guid priceListId,
        CancellationToken cancellationToken)
    {
        Result result = await dispatcher.SendAsync(
            new MakeDefaultPriceListCommand(priceListId), cancellationToken);

        return result.ToNoContent();
    }

    private static async Task<IResult> ListPricesAsync(
        IDispatcher dispatcher,
        Guid priceListId,
        int page = 1,
        int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        Result<PagedResult<PriceListEntryDto>> result = await dispatcher.SendAsync(
            new ListPricesQuery(priceListId, PageRequest.Of(page, pageSize)),
            cancellationToken);

        return result.ToOk();
    }

    private static async Task<IResult> GetPriceAsync(
        IDispatcher dispatcher,
        Guid priceListId,
        Guid partId,
        CancellationToken cancellationToken)
    {
        Result<PriceListEntryDto> result = await dispatcher.SendAsync(
            new GetPartPriceQuery(priceListId, partId), cancellationToken);

        return result.ToOk();
    }

    private static async Task<IResult> SetPriceAsync(
        IDispatcher dispatcher,
        Guid priceListId,
        Guid partId,
        SetPriceRequest body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        Result<Guid> result = await dispatcher.SendAsync(
            new SetPartPriceCommand(priceListId, partId, body.MinimumQuantity, body.UnitPrice),
            cancellationToken);

        return result.ToCreated(_ => $"/api/pricing/lists/{priceListId}/prices/{partId}");
    }

    private static async Task<IResult> RemoveBreakAsync(
        IDispatcher dispatcher,
        Guid priceListId,
        Guid partId,
        decimal minimumQuantity,
        CancellationToken cancellationToken)
    {
        Result result = await dispatcher.SendAsync(
            new RemovePriceBreakCommand(priceListId, partId, minimumQuantity),
            cancellationToken);

        return result.ToNoContent();
    }

    private static async Task<IResult> RemovePriceAsync(
        IDispatcher dispatcher,
        Guid priceListId,
        Guid partId,
        CancellationToken cancellationToken)
    {
        Result result = await dispatcher.SendAsync(
            new RemovePartPriceCommand(priceListId, partId), cancellationToken);

        return result.ToNoContent();
    }
}

/// <summary>Body of a request that renames a list or moves its period.</summary>
/// <param name="Name">The new name.</param>
/// <param name="EffectiveFrom">The new first day, or null for always.</param>
/// <param name="EffectiveTo">The new last day, or null for never expiring.</param>
public sealed record AmendPriceListRequest(
    string Name,
    DateOnly? EffectiveFrom,
    DateOnly? EffectiveTo);

/// <summary>Body of a request that sets a price from a quantity upwards.</summary>
/// <param name="MinimumQuantity">The quantity the price applies from. Usually 1.</param>
/// <param name="UnitPrice">What one unit costs from there upwards, in the list's currency.</param>
public sealed record SetPriceRequest(decimal MinimumQuantity, decimal UnitPrice);
