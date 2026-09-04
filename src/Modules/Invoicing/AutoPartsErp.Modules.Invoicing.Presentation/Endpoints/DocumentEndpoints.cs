using AutoPartsErp.Modules.Abstractions.Http;
using AutoPartsErp.Modules.Abstractions.Modules;
using AutoPartsErp.Modules.Invoicing.Application.Contracts;
using AutoPartsErp.Modules.Invoicing.Application.Documents.Commands;
using AutoPartsErp.Modules.Invoicing.Application.Documents.Queries;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Paging;
using AutoPartsErp.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AutoPartsErp.Modules.Invoicing.Presentation.Endpoints;

/// <summary>
/// HTTP routes for documents.
/// <para>
/// There is no PUT and no DELETE on this resource, and that is the point. A document is built as a
/// draft, issued once, and after that the only thing anybody can do to it is void it — which
/// leaves every figure exactly where it was. An endpoint that could edit an issued invoice would
/// be an endpoint for committing tax fraud.
/// </para>
/// </summary>
public sealed class DocumentEndpoints : IEndpointGroup
{
    /// <inheritdoc />
    public void Map(IEndpointRouteBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/documents", SearchAsync)
            .WithName("SearchDocuments")
            .WithSummary(
                "Search issued documents. Pass draftsOnly for the work in progress instead.")
            .Produces<PagedResult<InvoiceSummary>>();

        group.MapGet("/documents/{invoiceId:guid}", GetAsync)
            .WithName("GetDocument")
            .WithSummary("Get one document with its lines, its tax split and everything printed on it.")
            .Produces<InvoiceDetail>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/documents/by-number/{documentNumber}", GetByNumberAsync)
            .WithName("GetDocumentByNumber")
            .WithSummary("Get one document by the number a customer quotes on the phone.")
            .Produces<InvoiceDetail>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/documents", CreateAsync)
            .WithName("CreateDocument")
            .WithSummary(
                "Start a draft. It takes no number and has no legal standing until it is issued.")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/documents/{invoiceId:guid}/lines", AddLineAsync)
            .WithName("AddDocumentLine")
            .WithSummary(
                "Add a line to a draft. The SKU and description are snapshotted from the catalogue "
                + "as they are today, and never refreshed afterwards.")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/documents/{invoiceId:guid}/issue", IssueAsync)
            .WithName("IssueDocument")
            .WithSummary(
                "Take a number, sign the document and freeze it. This is the point of no return: "
                + "afterwards it can only be voided, never changed.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/documents/{invoiceId:guid}/void", VoidAsync)
            .WithName("VoidDocument")
            .WithSummary(
                "Void an issued document. It keeps its number, its figures and its place in the "
                + "signature chain. A reason is required and goes into the SAF-T export.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<IResult> SearchAsync(
        IDispatcher dispatcher,
        string? term,
        Guid? customerId,
        string? type,
        string? status,
        DateOnly? from,
        DateOnly? to,
        bool draftsOnly = false,
        int page = 1,
        int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var criteria = new InvoiceSearchCriteria
        {
            Term = term,
            CustomerId = customerId,
            Type = type,
            Status = status,
            From = from,
            To = to,
            DraftsOnly = draftsOnly,
        };

        Result<PagedResult<InvoiceSummary>> result = await dispatcher.SendAsync(
            new SearchDocumentsQuery(criteria, PageRequest.Of(page, pageSize)),
            cancellationToken);

        return result.ToOk();
    }

    private static async Task<IResult> GetAsync(
        IDispatcher dispatcher,
        Guid invoiceId,
        CancellationToken cancellationToken)
    {
        Result<InvoiceDetail> result =
            await dispatcher.SendAsync(new GetDocumentQuery(invoiceId), cancellationToken);

        return result.ToOk();
    }

    private static async Task<IResult> GetByNumberAsync(
        IDispatcher dispatcher,
        string documentNumber,
        CancellationToken cancellationToken)
    {
        Result<InvoiceDetail> result =
            await dispatcher.SendAsync(new GetDocumentByNumberQuery(documentNumber), cancellationToken);

        return result.ToOk();
    }

    private static async Task<IResult> CreateAsync(
        IDispatcher dispatcher,
        CreateDocumentCommand command,
        CancellationToken cancellationToken)
    {
        Result<Guid> result = await dispatcher.SendAsync(command, cancellationToken);
        return result.ToCreated(id => $"/api/invoicing/documents/{id}");
    }

    private static async Task<IResult> AddLineAsync(
        IDispatcher dispatcher,
        Guid invoiceId,
        AddDocumentLineRequest body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        Result<Guid> result = await dispatcher.SendAsync(
            new AddDocumentLineCommand(
                invoiceId,
                body.PartId,
                body.Quantity,
                body.UnitPrice,
                body.DiscountPercent,
                body.VatCategory,
                body.VatPercent,
                body.ExemptionCode,
                body.ExemptionReason),
            cancellationToken);

        return result.ToCreated(lineId => $"/api/invoicing/documents/{invoiceId}/lines/{lineId}");
    }

    private static async Task<IResult> IssueAsync(
        IDispatcher dispatcher,
        Guid invoiceId,

        // Nullable on purpose: issuing into the live series for the type and year is the normal
        // case, and naming a series is the exception a year-end changeover needs.
        IssueRequest? body,
        CancellationToken cancellationToken)
    {
        Result result = await dispatcher.SendAsync(
            new IssueDocumentCommand(invoiceId, body?.SeriesId),
            cancellationToken);

        return result.ToNoContent();
    }

    private static async Task<IResult> VoidAsync(
        IDispatcher dispatcher,
        Guid invoiceId,
        VoidRequest body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        Result result =
            await dispatcher.SendAsync(new VoidDocumentCommand(invoiceId, body.Reason), cancellationToken);

        return result.ToNoContent();
    }
}

/// <summary>Body of an add-line request.</summary>
/// <param name="PartId">The part sold.</param>
/// <param name="Quantity">How much, in the part's stocking unit.</param>
/// <param name="UnitPrice">The price per unit, before discount.</param>
/// <param name="DiscountPercent">The discount given, 0 to 100.</param>
/// <param name="VatCategory">ISE, RED, INT or NOR.</param>
/// <param name="VatPercent">The rate. Ignored for an exempt line.</param>
/// <param name="ExemptionCode">The tax authority's code, required when exempt.</param>
/// <param name="ExemptionReason">The legal basis, required when exempt and printed on the page.</param>
public sealed record AddDocumentLineRequest(
    Guid PartId,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountPercent = 0m,
    string VatCategory = "NOR",
    decimal VatPercent = 23m,
    string? ExemptionCode = null,
    string? ExemptionReason = null);

/// <summary>Body of an issue request.</summary>
/// <param name="SeriesId">
/// The series to issue in, or null to use the live one for this document type and year.
/// </param>
public sealed record IssueRequest(Guid? SeriesId);

/// <summary>Body of a void request.</summary>
/// <param name="Reason">Why the document is being voided.</param>
public sealed record VoidRequest(string Reason);
