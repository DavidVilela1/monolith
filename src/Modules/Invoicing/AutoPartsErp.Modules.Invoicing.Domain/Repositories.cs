using AutoPartsErp.Modules.Invoicing.Domain.Invoices;
using AutoPartsErp.Modules.Invoicing.Domain.Series;
using AutoPartsErp.SharedKernel.Abstractions;

namespace AutoPartsErp.Modules.Invoicing.Domain;

/// <summary>Write-side access to document series.</summary>
public interface IDocumentSeriesRepository : IRepository<DocumentSeries, DocumentSeriesId>
{
    /// <summary>
    /// Loads a series for exclusive use while a document takes its next number.
    /// <para>
    /// Not the same call as <see cref="IRepository{TAggregate,TId}.GetByIdAsync"/>, and the
    /// difference is the whole of gapless numbering. Two documents issuing at once would both
    /// read the same next number, both write, and one would lose on the concurrency token — which
    /// is safe but produces a failed request in the middle of a customer's transaction. This
    /// takes a row lock instead, so the second one waits a few milliseconds and then gets the
    /// next number rather than an error.
    /// </para>
    /// </summary>
    /// <param name="id">The series.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<DocumentSeries?> GetForIssuingAsync(
        DocumentSeriesId id,
        CancellationToken cancellationToken = default);

    /// <summary>The live series for a document type and year, or null when none has been opened.</summary>
    Task<DocumentSeries?> GetActiveAsync(
        DocumentType type,
        int year,
        CancellationToken cancellationToken = default);

    /// <summary>True when a series already uses that code for that type and year.</summary>
    Task<bool> CodeExistsAsync(
        DocumentType type,
        string code,
        int year,
        CancellationToken cancellationToken = default);
}

/// <summary>Write-side access to documents.</summary>
public interface IInvoiceRepository : IRepository<Invoice, InvoiceId>
{
    /// <summary>Loads a document by its number, the way a customer quotes one on the phone.</summary>
    Task<Invoice?> GetByNumberAsync(string documentNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// The base64 signature of the last document issued in a series, or null when it has issued
    /// none.
    /// <para>
    /// This is the link in the chain the next document signs onto. It is a repository call and
    /// not something the aggregate can work out, which is why <see cref="Invoice.Issue"/> takes
    /// it as an argument.
    /// </para>
    /// </summary>
    /// <param name="seriesId">The series.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<string?> GetLastSignatureAsync(
        DocumentSeriesId seriesId,
        CancellationToken cancellationToken = default);
}

/// <summary>The Invoicing module's unit of work.</summary>
public interface IInvoicingUnitOfWork : IUnitOfWork;
