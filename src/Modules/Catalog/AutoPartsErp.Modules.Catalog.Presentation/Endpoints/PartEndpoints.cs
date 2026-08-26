using AutoPartsErp.Modules.Abstractions.Http;
using AutoPartsErp.Modules.Abstractions.Modules;
using AutoPartsErp.Modules.Catalog.Application.Contracts;
using AutoPartsErp.Modules.Catalog.Application.Parts.Commands;
using AutoPartsErp.Modules.Catalog.Application.Parts.Queries;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Paging;
using AutoPartsErp.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AutoPartsErp.Modules.Catalog.Presentation.Endpoints;

/// <summary>
/// HTTP routes for parts.
/// <para>
/// Endpoints stay this thin on purpose: bind the request, dispatch it, map the result. Every
/// decision worth testing lives in a handler or an aggregate, where it can be tested without
/// spinning up a web server.
/// </para>
/// </summary>
public sealed class PartEndpoints : IEndpointGroup
{
    /// <inheritdoc />
    public void Map(IEndpointRouteBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        RouteGroupBuilder parts = group.MapGroup("/parts");

        parts.MapGet("/", SearchAsync)
            .WithName("SearchParts")
            .WithSummary("Search parts by number, cross-reference or description.")
            .Produces<PagedResult<PartSummary>>();

        parts.MapGet("/{partId:guid}", GetByIdAsync)
            .WithName("GetPart")
            .WithSummary("Get one part with its cross-references and fitments.")
            .Produces<PartDetail>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        parts.MapGet("/by-sku/{sku}", GetBySkuAsync)
            .WithName("GetPartBySku")
            .WithSummary("Get one part by its stock keeping unit.")
            .Produces<PartDetail>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        parts.MapGet("/for-vehicle", FindForVehicleAsync)
            .WithName("FindPartsForVehicle")
            .WithSummary("Find every part recorded as fitting a given vehicle.")
            .Produces<PagedResult<PartSummary>>();

        parts.MapPost("/", CreateAsync)
            .WithName("CreatePart")
            .WithSummary("Register a new part. It starts as a draft.")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        parts.MapPost("/{partId:guid}/activate", ActivateAsync)
            .WithName("ActivatePart")
            .WithSummary("Make a draft part orderable and sellable.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        parts.MapPost("/{partId:guid}/discontinue", DiscontinueAsync)
            .WithName("DiscontinuePart")
            .WithSummary("Withdraw a part from purchasing, optionally naming its replacement.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        parts.MapPut("/{partId:guid}/core-charge", SetCoreChargeAsync)
            .WithName("SetPartCoreCharge")
            .WithSummary("Record the refundable deposit on a part sold against a returnable core.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem();

        parts.MapPost("/{partId:guid}/cross-references", AddCrossReferenceAsync)
            .WithName("AddPartCrossReference")
            .WithSummary("Link an OEM or competitor number to this part.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        parts.MapPost("/{partId:guid}/fitments", AddFitmentAsync)
            .WithName("AddPartFitment")
            .WithSummary("Record that this part fits a vehicle.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> SearchAsync(
        IDispatcher dispatcher,
        string? term,
        Guid? brandId,
        Guid? categoryId,
        string? status,
        bool? requiresCoreReturn,
        int page = 1,
        int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        Result<PagedResult<PartSummary>> result = await dispatcher.SendAsync(
            new SearchPartsQuery(term, brandId, categoryId, status, requiresCoreReturn, page, pageSize),
            cancellationToken);

        return result.ToOk();
    }

    private static async Task<IResult> GetByIdAsync(
        IDispatcher dispatcher,
        Guid partId,
        CancellationToken cancellationToken)
    {
        Result<PartDetail> result = await dispatcher.SendAsync(new GetPartByIdQuery(partId), cancellationToken);
        return result.ToOk();
    }

    private static async Task<IResult> GetBySkuAsync(
        IDispatcher dispatcher,
        string sku,
        CancellationToken cancellationToken)
    {
        Result<PartDetail> result = await dispatcher.SendAsync(new GetPartBySkuQuery(sku), cancellationToken);
        return result.ToOk();
    }

    private static async Task<IResult> FindForVehicleAsync(
        IDispatcher dispatcher,
        string make,
        string model,
        int year,
        string? engineCode,
        int page = 1,
        int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        Result<PagedResult<PartSummary>> result = await dispatcher.SendAsync(
            new FindPartsForVehicleQuery(make, model, year, engineCode, page, pageSize),
            cancellationToken);

        return result.ToOk();
    }

    private static async Task<IResult> CreateAsync(
        IDispatcher dispatcher,
        CreatePartCommand command,
        CancellationToken cancellationToken)
    {
        Result<Guid> result = await dispatcher.SendAsync(command, cancellationToken);
        return result.ToCreated(id => $"/api/catalog/parts/{id}");
    }

    private static async Task<IResult> ActivateAsync(
        IDispatcher dispatcher,
        Guid partId,
        CancellationToken cancellationToken)
    {
        Result result = await dispatcher.SendAsync(new ActivatePartCommand(partId), cancellationToken);
        return result.ToNoContent();
    }

    private static async Task<IResult> DiscontinueAsync(
        IDispatcher dispatcher,
        Guid partId,
        DiscontinuePartRequest? body,
        CancellationToken cancellationToken)
    {
        Result result = await dispatcher.SendAsync(
            new DiscontinuePartCommand(partId, body?.SupersededByPartId),
            cancellationToken);

        return result.ToNoContent();
    }

    private static async Task<IResult> SetCoreChargeAsync(
        IDispatcher dispatcher,
        Guid partId,
        SetCoreChargeRequest body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        Result result = await dispatcher.SendAsync(
            new SetCoreChargeCommand(partId, body.Amount, body.CurrencyCode),
            cancellationToken);

        return result.ToNoContent();
    }

    private static async Task<IResult> AddCrossReferenceAsync(
        IDispatcher dispatcher,
        Guid partId,
        AddCrossReferenceRequest body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        Result result = await dispatcher.SendAsync(
            new AddCrossReferenceCommand(partId, body.Kind, body.Number, body.SourceBrand, body.Notes),
            cancellationToken);

        return result.ToNoContent();
    }

    private static async Task<IResult> AddFitmentAsync(
        IDispatcher dispatcher,
        Guid partId,
        AddFitmentRequest body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        Result result = await dispatcher.SendAsync(
            new AddFitmentCommand(
                partId,
                body.Make,
                body.Model,
                body.YearFrom,
                body.YearTo,
                body.EngineCode,
                body.Position,
                body.Notes),
            cancellationToken);

        return result.ToNoContent();
    }
}

/// <summary>Body of a discontinue request.</summary>
/// <param name="SupersededByPartId">The replacement part, when the brand has named one.</param>
public sealed record DiscontinuePartRequest(Guid? SupersededByPartId);

/// <summary>Body of a core charge request.</summary>
/// <param name="Amount">The refundable deposit.</param>
/// <param name="CurrencyCode">ISO currency code.</param>
public sealed record SetCoreChargeRequest(decimal Amount, string CurrencyCode);

/// <summary>Body of an add-cross-reference request.</summary>
/// <param name="Kind">Oem, Competitor, Supersedes, Interchange or TradingPartner.</param>
/// <param name="Number">The foreign number, as printed.</param>
/// <param name="SourceBrand">Whose number it is, when known.</param>
/// <param name="Notes">Optional qualifier.</param>
public sealed record AddCrossReferenceRequest(
    string Kind,
    string Number,
    string? SourceBrand,
    string? Notes);

/// <summary>Body of an add-fitment request.</summary>
/// <param name="Make">Vehicle manufacturer.</param>
/// <param name="Model">Model designation.</param>
/// <param name="YearFrom">First model year covered.</param>
/// <param name="YearTo">Last model year covered.</param>
/// <param name="EngineCode">Optional engine or type code.</param>
/// <param name="Position">Optional fitting position.</param>
/// <param name="Notes">Optional qualifier.</param>
public sealed record AddFitmentRequest(
    string Make,
    string Model,
    int YearFrom,
    int YearTo,
    string? EngineCode,
    string? Position,
    string? Notes);
