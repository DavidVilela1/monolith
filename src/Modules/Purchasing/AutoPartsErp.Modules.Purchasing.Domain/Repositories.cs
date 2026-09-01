using AutoPartsErp.Modules.Purchasing.Domain.Orders;
using AutoPartsErp.Modules.Purchasing.Domain.Replenishment;
using AutoPartsErp.SharedKernel.Abstractions;

namespace AutoPartsErp.Modules.Purchasing.Domain;

/// <summary>Write-side access to purchase orders.</summary>
public interface IPurchaseOrderRepository : IRepository<PurchaseOrder, PurchaseOrderId>
{
    /// <summary>Loads an order together with its lines, or null when there is no such order.</summary>
    Task<PurchaseOrder?> GetByNumberAsync(string orderNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reserves the next order number for the given year, e.g. "PO-2026-00042".
    /// <para>
    /// On the repository rather than in the aggregate because the number has to be unique across
    /// every order in the tenant, which is a fact about the database and not about this document.
    /// The current implementation is a max-plus-one and will collide under genuine concurrency;
    /// a proper sequence table with its own transaction is on the list, alongside the other
    /// document numbering the Portuguese ATCUD rules will need.
    /// </para>
    /// </summary>
    /// <param name="year">The year to number within.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<string> NextOrderNumberAsync(int year, CancellationToken cancellationToken = default);

    /// <summary>Every order for a supplier that still has something outstanding.</summary>
    Task<IReadOnlyList<PurchaseOrder>> GetOpenForSupplierAsync(
        SupplierRef supplierId,
        CancellationToken cancellationToken = default);
}

/// <summary>Write-side access to replenishment suggestions.</summary>
public interface IReplenishmentSuggestionRepository
    : IRepository<ReplenishmentSuggestion, SuggestionId>
{
    /// <summary>
    /// The open suggestion for a part in a warehouse, or null when there is none.
    /// <para>
    /// This is what makes the reorder-point handler idempotent: an at-least-once bus that
    /// delivers the same signal twice finds the existing suggestion and refreshes it instead of
    /// leaving the buyer with two identical rows.
    /// </para>
    /// </summary>
    Task<ReplenishmentSuggestion?> GetOpenForAsync(
        PartRef partId,
        WarehouseRef warehouseId,
        CancellationToken cancellationToken = default);

    /// <summary>Every open suggestion for a part, across all warehouses.</summary>
    Task<IReadOnlyList<ReplenishmentSuggestion>> GetOpenForPartAsync(
        PartRef partId,
        CancellationToken cancellationToken = default);
}

/// <summary>The Purchasing module's unit of work.</summary>
public interface IPurchasingUnitOfWork : IUnitOfWork;
