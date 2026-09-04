using AutoPartsErp.Modules.Invoicing.Application.Abstractions;
using AutoPartsErp.Modules.Invoicing.Application.Contracts;
using AutoPartsErp.Modules.Invoicing.Domain;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Paging;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Invoicing.Application.Documents.Queries;

/// <summary>Loads one document in full.</summary>
/// <param name="InvoiceId">The document.</param>
public sealed record GetDocumentQuery(Guid InvoiceId) : IQuery<InvoiceDetail>;

/// <summary>Loads the document.</summary>
public sealed class GetDocumentQueryHandler : IQueryHandler<GetDocumentQuery, InvoiceDetail>
{
    private readonly IInvoicingReadStore _readStore;

    /// <summary>Initializes the handler.</summary>
    public GetDocumentQueryHandler(IInvoicingReadStore readStore)
    {
        _readStore = readStore;
    }

    /// <inheritdoc />
    public async Task<Result<InvoiceDetail>> HandleAsync(
        GetDocumentQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        InvoiceDetail? document = await _readStore
            .GetDocumentAsync(request.InvoiceId, cancellationToken)
            .ConfigureAwait(false);

        return document is null
            ? Result.Failure<InvoiceDetail>(
                InvoicingErrors.Document.NotFound(request.InvoiceId.ToString()))
            : document;
    }
}

/// <summary>Loads one document by the number a customer quotes on the phone.</summary>
/// <param name="DocumentNumber">Its number, e.g. <c>FT SERIE2026/35</c>.</param>
public sealed record GetDocumentByNumberQuery(string DocumentNumber) : IQuery<InvoiceDetail>;

/// <summary>Loads the document.</summary>
public sealed class GetDocumentByNumberQueryHandler
    : IQueryHandler<GetDocumentByNumberQuery, InvoiceDetail>
{
    private readonly IInvoicingReadStore _readStore;

    /// <summary>Initializes the handler.</summary>
    public GetDocumentByNumberQueryHandler(IInvoicingReadStore readStore)
    {
        _readStore = readStore;
    }

    /// <inheritdoc />
    public async Task<Result<InvoiceDetail>> HandleAsync(
        GetDocumentByNumberQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        InvoiceDetail? document = await _readStore
            .GetDocumentByNumberAsync(request.DocumentNumber, cancellationToken)
            .ConfigureAwait(false);

        return document is null
            ? Result.Failure<InvoiceDetail>(InvoicingErrors.Document.NotFound(request.DocumentNumber))
            : document;
    }
}

/// <summary>Searches documents.</summary>
/// <param name="Criteria">What to look for.</param>
/// <param name="Page">Which page to return.</param>
public sealed record SearchDocumentsQuery(InvoiceSearchCriteria Criteria, PageRequest Page)
    : IQuery<PagedResult<InvoiceSummary>>;

/// <summary>Runs the search.</summary>
public sealed class SearchDocumentsQueryHandler
    : IQueryHandler<SearchDocumentsQuery, PagedResult<InvoiceSummary>>
{
    private readonly IInvoicingReadStore _readStore;

    /// <summary>Initializes the handler.</summary>
    public SearchDocumentsQueryHandler(IInvoicingReadStore readStore)
    {
        _readStore = readStore;
    }

    /// <inheritdoc />
    public async Task<Result<PagedResult<InvoiceSummary>>> HandleAsync(
        SearchDocumentsQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await _readStore
            .SearchDocumentsAsync(request.Criteria, request.Page, cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>Loads one series.</summary>
/// <param name="SeriesId">The series.</param>
public sealed record GetDocumentSeriesQuery(Guid SeriesId) : IQuery<DocumentSeriesDto>;

/// <summary>Loads the series.</summary>
public sealed class GetDocumentSeriesQueryHandler
    : IQueryHandler<GetDocumentSeriesQuery, DocumentSeriesDto>
{
    private readonly IInvoicingReadStore _readStore;

    /// <summary>Initializes the handler.</summary>
    public GetDocumentSeriesQueryHandler(IInvoicingReadStore readStore)
    {
        _readStore = readStore;
    }

    /// <inheritdoc />
    public async Task<Result<DocumentSeriesDto>> HandleAsync(
        GetDocumentSeriesQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        DocumentSeriesDto? series = await _readStore
            .GetSeriesAsync(request.SeriesId, cancellationToken)
            .ConfigureAwait(false);

        return series is null
            ? Result.Failure<DocumentSeriesDto>(
                InvoicingErrors.Series.NotFound(request.SeriesId.ToString()))
            : series;
    }
}

/// <summary>Lists document series.</summary>
/// <param name="Type">Restrict to one document type, or null for all.</param>
/// <param name="Year">Restrict to one year, or null for all.</param>
/// <param name="Page">Which page to return.</param>
public sealed record ListDocumentSeriesQuery(string? Type, int? Year, PageRequest Page)
    : IQuery<PagedResult<DocumentSeriesDto>>;

/// <summary>Lists the series.</summary>
public sealed class ListDocumentSeriesQueryHandler
    : IQueryHandler<ListDocumentSeriesQuery, PagedResult<DocumentSeriesDto>>
{
    private readonly IInvoicingReadStore _readStore;

    /// <summary>Initializes the handler.</summary>
    public ListDocumentSeriesQueryHandler(IInvoicingReadStore readStore)
    {
        _readStore = readStore;
    }

    /// <inheritdoc />
    public async Task<Result<PagedResult<DocumentSeriesDto>>> HandleAsync(
        ListDocumentSeriesQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await _readStore
            .ListSeriesAsync(request.Type, request.Year, request.Page, cancellationToken)
            .ConfigureAwait(false);
    }
}
