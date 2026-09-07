using AutoPartsErp.Modules.Finance.Domain.Customers;
using AutoPartsErp.Modules.Finance.Domain.Receipts;
using AutoPartsErp.Modules.Finance.Domain.Receivables;
using AutoPartsErp.SharedKernel.Abstractions;

namespace AutoPartsErp.Modules.Finance.Domain;

/// <summary>Write-side access to the items on customers' accounts.</summary>
public interface IOpenItemRepository : IRepository<OpenItem, OpenItemId>
{
    /// <summary>
    /// Loads the item raised from a document, or null when there is none.
    /// <para>
    /// Used by the event handlers, which know a document and nothing else. It is also what makes
    /// them idempotent: the outbox delivers at least once, and an item that already exists for
    /// this document means the message has been seen before.
    /// </para>
    /// </summary>
    /// <param name="documentId">The document in Invoicing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<OpenItem?> GetByDocumentAsync(
        DocumentRef documentId,
        CancellationToken cancellationToken = default);

    /// <summary>Loads several items by identity, for settling them together.</summary>
    /// <param name="ids">The items wanted.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<OpenItem>> GetManyAsync(
        IReadOnlyCollection<OpenItemId> ids,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Everything still outstanding on one customer's account, oldest document first.
    /// </summary>
    /// <param name="customerId">The customer.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<OpenItem>> GetOutstandingForCustomerAsync(
        CustomerRef customerId,
        CancellationToken cancellationToken = default);
}

/// <summary>Write-side access to receipts. Allocations are owned, so they load with their receipt.</summary>
public interface IReceiptRepository : IRepository<Receipt, ReceiptId>
{
    /// <summary>Loads a receipt by its number, or null when there is none.</summary>
    /// <param name="number">The receipt number.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Receipt?> GetByNumberAsync(
        string number,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes the next receipt number for the given year, e.g. <c>RC-2026-00042</c>.
    /// <para>
    /// From the module's counter table, not from the highest number already used - the same
    /// mechanism Sales and Purchasing number orders with, and for the same reason.
    /// </para>
    /// </summary>
    /// <param name="year">The year to number within.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<string> NextReceiptNumberAsync(int year, CancellationToken cancellationToken = default);
}

/// <summary>Write-side access to customers' payment terms.</summary>
public interface ICustomerTermsRepository : IRepository<CustomerTerms, CustomerRef>
{
}

/// <summary>The Finance module's unit of work.</summary>
public interface IFinanceUnitOfWork : IUnitOfWork;
