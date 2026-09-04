using AutoPartsErp.Modules.Abstractions.Http;
using AutoPartsErp.Modules.Abstractions.Modules;
using AutoPartsErp.Modules.Pricing.Application.Contracts;
using AutoPartsErp.Modules.Pricing.Application.Customers.Commands;
using AutoPartsErp.Modules.Pricing.Application.PriceLists.Queries;
using AutoPartsErp.Modules.Pricing.Application.Quotes;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Paging;
using AutoPartsErp.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AutoPartsErp.Modules.Pricing.Presentation.Endpoints;

/// <summary>
/// HTTP routes for customer agreements, and for the one question the rest of the system asks:
/// what does this cost?
/// </summary>
public sealed class QuoteEndpoints : IEndpointGroup
{
    /// <inheritdoc />
    public void Map(IEndpointRouteBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/quote", QuoteAsync)
            .WithName("QuotePrice")
            .WithSummary(
                "What a customer pays for a part at a quantity today, with the list and break it came from.")
            .Produces<PriceQuoteDto>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/agreements/{customerId:guid}", GetAgreementAsync)
            .WithName("GetCustomerPricing")
            .WithSummary("What was agreed with one customer.")
            .Produces<CustomerPricingDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/lists/{priceListId:guid}/agreements", ListAgreementsAsync)
            .WithName("ListAgreementsForPriceList")
            .WithSummary("Who a change to this list would reach.")
            .Produces<PagedResult<CustomerPricingDto>>();

        group.MapPost("/agreements", AgreeAsync)
            .WithName("AgreeCustomerPricing")
            .WithSummary("Record what was agreed with a customer. One agreement each.")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/agreements/{customerId:guid}", RenegotiateAsync)
            .WithName("RenegotiateCustomerPricing")
            .WithSummary("Change a customer's list, their discount, or both.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/agreements/{customerId:guid}/end", EndAsync)
            .WithName("EndCustomerPricing")
            .WithSummary("End a customer's terms, sending them back to the default list.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<IResult> QuoteAsync(
        IDispatcher dispatcher,
        Guid partId,
        decimal quantity,
        Guid? customerId,
        string? currencyCode,
        DateOnly? on,
        CancellationToken cancellationToken)
    {
        Result<PriceQuoteDto> result = await dispatcher.SendAsync(
            new QuotePriceQuery(partId, quantity, customerId, currencyCode, on),
            cancellationToken);

        return result.ToOk();
    }

    private static async Task<IResult> GetAgreementAsync(
        IDispatcher dispatcher,
        Guid customerId,
        CancellationToken cancellationToken)
    {
        Result<CustomerPricingDto> result = await dispatcher.SendAsync(
            new GetCustomerPricingQuery(customerId), cancellationToken);

        return result.ToOk();
    }

    private static async Task<IResult> ListAgreementsAsync(
        IDispatcher dispatcher,
        Guid priceListId,
        int page = 1,
        int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        Result<PagedResult<CustomerPricingDto>> result = await dispatcher.SendAsync(
            new ListAgreementsForListQuery(priceListId, PageRequest.Of(page, pageSize)),
            cancellationToken);

        return result.ToOk();
    }

    private static async Task<IResult> AgreeAsync(
        IDispatcher dispatcher,
        AgreeCustomerPricingCommand command,
        CancellationToken cancellationToken)
    {
        Result<Guid> result = await dispatcher.SendAsync(command, cancellationToken);

        return result.ToCreated(_ => $"/api/pricing/agreements/{command.CustomerId}");
    }

    private static async Task<IResult> RenegotiateAsync(
        IDispatcher dispatcher,
        Guid customerId,
        RenegotiateRequest body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        Result result = await dispatcher.SendAsync(
            new RenegotiateCustomerPricingCommand(
                customerId,
                body.PriceListId,
                body.DiscountPercent,
                body.EffectiveFrom,
                body.EffectiveTo,
                body.Note),
            cancellationToken);

        return result.ToNoContent();
    }

    private static async Task<IResult> EndAsync(
        IDispatcher dispatcher,
        Guid customerId,
        EndAgreementRequest? body,
        CancellationToken cancellationToken)
    {
        Result result = await dispatcher.SendAsync(
            new EndCustomerPricingCommand(customerId, body?.On), cancellationToken);

        return result.ToNoContent();
    }
}

/// <summary>Body of a renegotiation.</summary>
/// <param name="PriceListId">The list they buy from now.</param>
/// <param name="DiscountPercent">What comes off it now, 0 to 100.</param>
/// <param name="EffectiveFrom">The new first day, or null for always.</param>
/// <param name="EffectiveTo">The new last day, or null for never expiring.</param>
/// <param name="Note">Why it changed.</param>
public sealed record RenegotiateRequest(
    Guid PriceListId,
    decimal DiscountPercent,
    DateOnly? EffectiveFrom,
    DateOnly? EffectiveTo,
    string? Note);

/// <summary>Body of a request that ends an agreement.</summary>
/// <param name="On">The last day it applies. Defaults to today when omitted.</param>
public sealed record EndAgreementRequest(DateOnly? On);
