using AutoPartsErp.Modules.Finance.Application.Contracts;
using AutoPartsErp.SharedKernel.Paging;

namespace AutoPartsErp.Modules.Finance.Application.Abstractions;

/// <summary>The read side of the Finance module.</summary>
public interface IFinanceReadStore
{
    /// <summary>
    /// A customer's account: what they owe, how much of it is late, and the documents behind it.
    /// </summary>
    /// <param name="customerId">The customer.</param>
    /// <param name="asAt">The date to judge overdue against, normally today.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<CustomerStatement?> GetStatementAsync(
        Guid customerId,
        DateOnly asAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The ageing report: every customer with a balance, in the usual thirty-day buckets.
    /// <para>
    /// Paged, because "every customer with a balance" is a number that only goes up, and the
    /// convenient overload that returns all of them is the one somebody eventually calls from a
    /// loop.
    /// </para>
    /// </summary>
    /// <param name="asAt">The date to age against, normally today.</param>
    /// <param name="page">Which page to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<PagedResult<AgingRow>> GetAgingAsync(
        DateOnly asAt,
        PageRequest page,
        CancellationToken cancellationToken = default);

    /// <summary>Loads one receipt with what it paid, or null when it does not exist.</summary>
    /// <param name="receiptId">The receipt.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ReceiptDto?> GetReceiptAsync(
        Guid receiptId,
        CancellationToken cancellationToken = default);

    /// <summary>Lists receipts.</summary>
    /// <param name="criteria">What to look for.</param>
    /// <param name="page">Which page to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<PagedResult<ReceiptDto>> SearchReceiptsAsync(
        ReceiptSearchCriteria criteria,
        PageRequest page,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The documents on a customer's account that still have something outstanding, oldest first.
    /// <para>
    /// What an allocation screen shows: somebody has a receipt in front of them and needs the
    /// list of things it could be paying.
    /// </para>
    /// </summary>
    /// <param name="customerId">The customer.</param>
    /// <param name="asAt">The date to judge overdue against.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<OpenItemDto>> ListOutstandingAsync(
        Guid customerId,
        DateOnly asAt,
        CancellationToken cancellationToken = default);
}
