using AutoPartsErp.Modules.Invoicing.Application.Contracts;
using AutoPartsErp.Modules.Invoicing.Application.Saft;
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

    /// <summary>
    /// Every issued document in a period, in the shape a SAF-T file wants, oldest first.
    /// <para>
    /// Not paged, deliberately, unlike everything else on this interface. A SAF-T file is one
    /// file: a caller that received half of it would have produced a document claiming to be a
    /// complete record of a month while being nothing of the sort, and the number of entries in
    /// its own header would say so. A busy month is tens of thousands of lines and a few tens of
    /// megabytes, which is large for a response and small for a machine.
    /// </para>
    /// <para>
    /// Drafts are excluded and voided documents are included. A draft has no number and does not
    /// exist as far as the tax authority is concerned; a voided one has a number that was
    /// reported, and leaving it out would put a gap in the sequence.
    /// </para>
    /// </summary>
    /// <param name="from">The first document date to include.</param>
    /// <param name="to">The last document date to include.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<SaftDocument>> GetSaftDocumentsAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default);
}
