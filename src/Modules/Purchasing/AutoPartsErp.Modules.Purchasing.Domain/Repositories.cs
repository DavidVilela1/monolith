using AutoPartsErp.Modules.Purchasing.Domain.Agreements;
using AutoPartsErp.Modules.Purchasing.Domain.Invoices;
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
    /// Takes the next order number for the given year, e.g. "PO-2026-00042".
    /// <para>
    /// On the repository rather than in the aggregate because the number has to be unique across
    /// every order in the tenant, which is a fact about the database and not about this document.
    /// A counter in that database hands it out, one statement per number, so two buyers raising an
    /// order at once cannot send the supplier two documents quoting one reference.
    /// </para>
    /// <para>
    /// The number is spent as soon as it is taken, so an order that fails after this point leaves
    /// a gap in the run. That is acceptable here and is not acceptable in Invoicing, which is why
    /// the two are numbered by different mechanisms.
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

/// <summary>Write-side access to what was agreed with a supplier.</summary>
public interface ISupplierAgreementRepository : IRepository<SupplierAgreement, SupplierAgreementId>
{
    /// <summary>
    /// The agreement in force with a supplier on a given day, or null when there is none.
    /// <para>
    /// One lookup, always. The whole point of allowing a single live agreement per supplier is
    /// that "which one applies to this delivery?" never depends on the order two rows came back
    /// in.
    /// </para>
    /// </summary>
    Task<SupplierAgreement?> GetForSupplierAsync(
        SupplierRef supplierId,
        DateOnly on,
        CancellationToken cancellationToken = default);

    /// <summary>True when the supplier already has an agreement that has not ended.</summary>
    Task<bool> HasLiveAgreementAsync(
        SupplierRef supplierId,
        SupplierAgreementId? excluding = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Write-side access to what a supplier charges for a part.</summary>
public interface ISupplierPriceRepository : IRepository<SupplierPrice, SupplierPriceId>
{
    /// <summary>
    /// What a supplier charged for a part on a given day, or null when nothing was agreed by then.
    /// <para>
    /// The most recent row whose day has arrived, which is why a price rise is recorded rather
    /// than edited: both rows are here, and the one that answers is the one that was in force when
    /// the goods came off the van.
    /// </para>
    /// </summary>
    Task<SupplierPrice?> GetPriceOnAsync(
        SupplierRef supplierId,
        PartRef partId,
        DateOnly on,
        CancellationToken cancellationToken = default);

    /// <summary>True when that supplier already has a price for that part from exactly that day.</summary>
    Task<bool> ExistsForAsync(
        SupplierRef supplierId,
        PartRef partId,
        DateOnly effectiveFrom,
        CancellationToken cancellationToken = default);
}

/// <summary>Write-side access to suppliers' own documents.</summary>
public interface ISupplierInvoiceRepository : IRepository<SupplierInvoice, SupplierInvoiceId>
{
    /// <summary>
    /// The draft collecting one supplier's deliveries on one day, or null when none is open.
    /// <para>
    /// What makes a receipt land beside the ones that arrived on the same van instead of opening a
    /// document of its own.
    /// </para>
    /// </summary>
    Task<SupplierInvoice?> GetOpenDraftAsync(
        SupplierRef supplierId,
        DateOnly receivedOn,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// True when goods from that order line have already been put on a document.
    /// <para>
    /// The guard that makes drafting idempotent. Goods-receipt events arrive at least once, and a
    /// redelivered one must not charge the company for the same pallet twice.
    /// </para>
    /// </summary>
    Task<bool> HasLineForReceiptAsync(
        PurchaseOrderLineId purchaseOrderLineId,
        decimal quantity,
        CancellationToken cancellationToken = default);
}

/// <summary>The Purchasing module's unit of work.</summary>
public interface IPurchasingUnitOfWork : IUnitOfWork;
