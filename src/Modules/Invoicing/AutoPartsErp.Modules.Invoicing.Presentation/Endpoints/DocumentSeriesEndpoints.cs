using AutoPartsErp.Modules.Abstractions.Http;
using AutoPartsErp.Modules.Abstractions.Modules;
using AutoPartsErp.Modules.Invoicing.Application.Contracts;
using AutoPartsErp.Modules.Invoicing.Application.Documents.Queries;
using AutoPartsErp.Modules.Invoicing.Application.Series.Commands;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Paging;
using AutoPartsErp.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AutoPartsErp.Modules.Invoicing.Presentation.Endpoints;

/// <summary>
/// HTTP routes for document series.
/// <para>
/// Three separate steps rather than one create call, because they happen at three different times
/// and two of them involve a third party. A series is opened here, declared to the tax authority
/// through their portal, and only then given the code that came back — which can be minutes or
/// days later. Collapsing that into one request would mean pretending the AT answers instantly.
/// </para>
/// </summary>
public sealed class DocumentSeriesEndpoints : IEndpointGroup
{
    /// <inheritdoc />
    public void Map(IEndpointRouteBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/series", ListAsync)
            .WithName("ListDocumentSeries")
            .WithSummary("List document series, newest year first.")
            .Produces<PagedResult<DocumentSeriesDto>>();

        group.MapGet("/series/{seriesId:guid}", GetAsync)
            .WithName("GetDocumentSeries")
            .WithSummary("Get one series, with how many documents it has issued.")
            .Produces<DocumentSeriesDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/series", OpenAsync)
            .WithName("OpenDocumentSeries")
            .WithSummary(
                "Open a series. It cannot issue anything until it has been declared to the tax "
                + "authority and given its validation code.")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/series/{seriesId:guid}/validation-code", ValidateAsync)
            .WithName("ValidateDocumentSeries")
            .WithSummary(
                "Record the validation code the tax authority returned. Accepted once and never "
                + "changed, because it is baked into every ATCUD the series produces.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/series/{seriesId:guid}/activate", ActivateAsync)
            .WithName("ActivateDocumentSeries")
            .WithSummary("Put the series into service. It must have its validation code first.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/series/{seriesId:guid}/close", CloseAsync)
            .WithName("CloseDocumentSeries")
            .WithSummary("Close the series to new documents. One-way, and the tax authority has to be told.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<IResult> ListAsync(
        IDispatcher dispatcher,
        string? type,
        int? year,
        int page = 1,
        int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        Result<PagedResult<DocumentSeriesDto>> result = await dispatcher.SendAsync(
            new ListDocumentSeriesQuery(type, year, PageRequest.Of(page, pageSize)),
            cancellationToken);

        return result.ToOk();
    }

    private static async Task<IResult> GetAsync(
        IDispatcher dispatcher,
        Guid seriesId,
        CancellationToken cancellationToken)
    {
        Result<DocumentSeriesDto> result =
            await dispatcher.SendAsync(new GetDocumentSeriesQuery(seriesId), cancellationToken);

        return result.ToOk();
    }

    private static async Task<IResult> OpenAsync(
        IDispatcher dispatcher,
        OpenDocumentSeriesCommand command,
        CancellationToken cancellationToken)
    {
        Result<Guid> result = await dispatcher.SendAsync(command, cancellationToken);
        return result.ToCreated(id => $"/api/invoicing/series/{id}");
    }

    private static async Task<IResult> ValidateAsync(
        IDispatcher dispatcher,
        Guid seriesId,
        ValidationCodeRequest body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        Result result = await dispatcher.SendAsync(
            new ValidateDocumentSeriesCommand(seriesId, body.ValidationCode),
            cancellationToken);

        return result.ToNoContent();
    }

    private static async Task<IResult> ActivateAsync(
        IDispatcher dispatcher,
        Guid seriesId,
        CancellationToken cancellationToken)
    {
        Result result =
            await dispatcher.SendAsync(new ActivateDocumentSeriesCommand(seriesId), cancellationToken);

        return result.ToNoContent();
    }

    private static async Task<IResult> CloseAsync(
        IDispatcher dispatcher,
        Guid seriesId,
        CancellationToken cancellationToken)
    {
        Result result =
            await dispatcher.SendAsync(new CloseDocumentSeriesCommand(seriesId), cancellationToken);

        return result.ToNoContent();
    }
}

/// <summary>Body of a validation-code request.</summary>
/// <param name="ValidationCode">The code the tax authority returned, e.g. <c>CSDF7T5H</c>.</param>
public sealed record ValidationCodeRequest(string ValidationCode);
