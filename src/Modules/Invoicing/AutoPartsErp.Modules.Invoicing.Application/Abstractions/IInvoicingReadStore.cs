using AutoPartsErp.Modules.Invoicing.Application.Contracts;
using AutoPartsErp.SharedKernel.Paging;

namespace AutoPartsErp.Modules.Invoicing.Application.Abstractions;

/// <summary>The read side of the Invoicing module.</summary>
public interface IInvoicingReadStore
{
    /// <summary>Loads one series, or null when it does not exist.</summary>
    Task<DocumentSeriesDto?> GetSeriesAsync(Guid seriesId, CancellationToken cancellationToken = default);

    /// <summary>Lists series, newest year first.</summary>
    /// <param name="type">Restrict to one document type, or null for all.</param>
    /// <param name="year">Restrict to one year, or null for all.</param>
    /// <param name="page">Which page to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<PagedResult<DocumentSeriesDto>> ListSeriesAsync(
        string? type,
        int? year,
        PageRequest page,
        CancellationToken cancellationToken = default);

    /// <summary>Loads one document in full, or null when it does not exist.</summary>
    Task<InvoiceDetail?> GetDocumentAsync(Guid invoiceId, CancellationToken cancellationToken = default);

    /// <summary>Loads one document by its number, the way a customer quotes one on the phone.</summary>
    Task<InvoiceDetail?> GetDocumentByNumberAsync(
        string documentNumber,
        CancellationToken cancellationToken = default);

    /// <summary>Searches documents.</summary>
    /// <param name="criteria">What to look for.</param>
    /// <param name="page">Which page to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<PagedResult<InvoiceSummary>> SearchDocumentsAsync(
        InvoiceSearchCriteria criteria,
        PageRequest page,
        CancellationToken cancellationToken = default);
}
