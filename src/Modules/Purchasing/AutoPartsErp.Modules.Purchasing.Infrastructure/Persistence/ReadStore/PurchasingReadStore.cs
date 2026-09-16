using AutoPartsErp.Modules.Purchasing.Application.Abstractions;
using AutoPartsErp.Modules.Purchasing.Application.Contracts;
using AutoPartsErp.Modules.Purchasing.Domain;
using AutoPartsErp.Modules.Purchasing.Domain.Agreements;
using AutoPartsErp.Modules.Purchasing.Domain.Invoices;
using AutoPartsErp.Modules.Purchasing.Domain.Orders;
using AutoPartsErp.Modules.Purchasing.Domain.Replenishment;
using AutoPartsErp.SharedKernel.Abstractions;
using AutoPartsErp.SharedKernel.Paging;
using Microsoft.EntityFrameworkCore;

namespace AutoPartsErp.Modules.Purchasing.Infrastructure.Persistence.ReadStore;

/// <summary>Serves the Purchasing module's queries.</summary>
public sealed class PurchasingReadStore : IPurchasingReadStore
{
    /// <summary>Statuses in which an order still has something to come.</summary>
    private static readonly PurchaseOrderStatus[] OpenStatuses =
    [
        PurchaseOrderStatus.Submitted,
        PurchaseOrderStatus.Confirmed,
        PurchaseOrderStatus.PartiallyReceived,
    ];

    private readonly PurchasingDbContext _context;
    private readonly IDateTimeProvider _clock;

    /// <summary>Initializes the read store.</summary>
    public PurchasingReadStore(PurchasingDbContext context, IDateTimeProvider clock)
    {
        _context = context;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<PurchaseOrderDetail?> GetOrderAsync(
        Guid purchaseOrderId,
        CancellationToken cancellationToken = default)
    {
        var id = new PurchaseOrderId(purchaseOrderId);

        PurchaseOrder? order = await _context.PurchaseOrders
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken)
            .ConfigureAwait(false);

        return order is null ? null : MapDetail(order);
    }

    /// <inheritdoc />
    public async Task<PurchaseOrderDetail?> GetOrderByNumberAsync(
        string orderNumber,
        CancellationToken cancellationToken = default)
    {
        string normalized = orderNumber?.Trim().ToUpperInvariant() ?? string.Empty;

        PurchaseOrder? order = await _context.PurchaseOrders
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.OrderNumber == normalized, cancellationToken)
            .ConfigureAwait(false);

        return order is null ? null : MapDetail(order);
    }

    /// <inheritdoc />
    public async Task<PagedResult<PurchaseOrderSummary>> SearchOrdersAsync(
        PurchaseOrderSearchCriteria criteria,
        PageRequest page,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        ArgumentNullException.ThrowIfNull(page);

        DateOnly today = _clock.TodayUtc;

        IQueryable<PurchaseOrder> query = _context.PurchaseOrders.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(criteria.Term))
        {
            string term = criteria.Term.Trim();
            string upper = term.ToUpperInvariant();

            query = query.Where(order =>
                EF.Functions.Like(order.OrderNumber, $"%{upper}%")
                || EF.Functions.Like(order.SupplierCode, $"{upper}%")
                || (order.SupplierReference != null
                    && EF.Functions.ILike(order.SupplierReference, $"%{term}%")));
        }

        if (criteria.SupplierId is { } supplierId)
        {
            var supplier = new SupplierRef(supplierId);
            query = query.Where(order => order.SupplierId == supplier);
        }

        if (criteria.WarehouseId is { } warehouseId)
        {
            var warehouse = new WarehouseRef(warehouseId);
            query = query.Where(order => order.DeliverToWarehouseId == warehouse);
        }

        if (!string.IsNullOrWhiteSpace(criteria.Status)
            && Enum.TryParse(criteria.Status, ignoreCase: true, out PurchaseOrderStatus status))
        {
            query = query.Where(order => order.Status == status);
        }

        // "Still to come" is a question about the order's status, not about arithmetic across
        // its lines. Asking it that way keeps the filter an index seek instead of a subquery
        // over every line in the table.
        if (criteria.OutstandingOnly || criteria.OverdueOnly)
        {
            query = query.Where(order => OpenStatuses.Contains(order.Status));
        }

        if (criteria.OverdueOnly)
        {
            query = query.Where(order => order.ExpectedOn != null && order.ExpectedOn < today);
        }

        int total = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        if (total == 0)
        {
            return PagedResult<PurchaseOrderSummary>.Empty(page.Page, page.PageSize);
        }

        // Newest first. Order numbers are zero-padded and sequential within a year, so ordering
        // them as text is also ordering them by when they were raised.
        List<PurchaseOrder> rows = await query
            .OrderByDescending(order => order.OrderNumber)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<PurchaseOrderSummary> items = [.. rows.Select(order => MapSummary(order, today))];

        return PagedResult<PurchaseOrderSummary>.Create(items, page.Page, page.PageSize, total);
    }

    /// <inheritdoc />
    public async Task<PagedResult<ReplenishmentSuggestionDto>> ListSuggestionsAsync(
        SuggestionSearchCriteria criteria,
        PageRequest page,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        ArgumentNullException.ThrowIfNull(page);

        IQueryable<ReplenishmentSuggestion> query = _context.ReplenishmentSuggestions.AsNoTracking();

        // Open by default: a buyer opening this screen wants the work, not the archive.
        SuggestionStatus status = SuggestionStatus.Open;
        if (!string.IsNullOrWhiteSpace(criteria.Status)
            && Enum.TryParse(criteria.Status, ignoreCase: true, out SuggestionStatus requested)
            && requested != SuggestionStatus.Unknown)
        {
            status = requested;
        }

        query = query.Where(suggestion => suggestion.Status == status);

        if (criteria.WarehouseId is { } warehouseId)
        {
            var warehouse = new WarehouseRef(warehouseId);
            query = query.Where(suggestion => suggestion.WarehouseId == warehouse);
        }

        if (criteria.PartId is { } partId)
        {
            var part = new PartRef(partId);
            query = query.Where(suggestion => suggestion.PartId == part);
        }

        int total = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        if (total == 0)
        {
            return PagedResult<ReplenishmentSuggestionDto>.Empty(page.Page, page.PageSize);
        }

        // Worst shortfall first: the part that is furthest below its trigger is the one about to
        // cost a sale. Written out rather than using the Shortfall property, which is C# and
        // would drag the whole table into memory to sort it.
        List<ReplenishmentSuggestion> rows = await query
            .OrderByDescending(suggestion =>
                suggestion.ReorderPoint - (suggestion.QuantityAvailable + suggestion.QuantityOnOrder))
            .ThenBy(suggestion => suggestion.RaisedAtUtc)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<ReplenishmentSuggestionDto> items = [.. rows.Select(suggestion => new ReplenishmentSuggestionDto(
            suggestion.Id.Value,
            suggestion.PartId.Value,
            suggestion.WarehouseId.Value,
            suggestion.QuantityAvailable,
            suggestion.QuantityOnOrder,
            suggestion.ReorderPoint,
            suggestion.SuggestedQuantity,
            suggestion.Shortfall,
            suggestion.Status.ToString(),
            suggestion.RaisedAtUtc,
            suggestion.LastSeenAtUtc,
            suggestion.PurchaseOrderId?.Value,
            suggestion.DismissedReason))];

        return PagedResult<ReplenishmentSuggestionDto>.Create(items, page.Page, page.PageSize, total);
    }

    private static PurchaseOrderSummary MapSummary(PurchaseOrder order, DateOnly today) => new(
        order.Id.Value,
        order.OrderNumber,
        order.SupplierId.Value,
        order.SupplierCode,
        order.Status.ToString(),
        order.OrderedOn,
        order.ExpectedOn,
        order.Total.Amount,
        order.OutstandingValue.Amount,
        order.CurrencyCode,
        order.Lines.Count,
        order.ExpectedOn is { } expected && expected < today && order.HasOutstandingLines && !order.IsClosed);

    private static PurchaseOrderDetail MapDetail(PurchaseOrder order) => new()
    {
        Id = order.Id.Value,
        OrderNumber = order.OrderNumber,
        SupplierId = order.SupplierId.Value,
        SupplierCode = order.SupplierCode,
        DeliverToWarehouseId = order.DeliverToWarehouseId.Value,
        Status = order.Status.ToString(),
        CurrencyCode = order.CurrencyCode,
        OrderedOn = order.OrderedOn,
        ExpectedOn = order.ExpectedOn,
        SupplierReference = order.SupplierReference,
        Notes = order.Notes,
        ClosureReason = order.ClosureReason,
        Total = order.Total.Amount,
        OutstandingValue = order.OutstandingValue.Amount,
        IsEditable = order.IsEditable,
        CanReceive = order.CanReceive,
        Lines = [.. order.Lines.Select(line => new PurchaseOrderLineDto(
            line.Id.Value,
            line.PartId.Value,
            line.Sku,
            line.Description,
            line.Quantity.Value,
            line.ReceivedQuantity.Value,
            line.OutstandingQuantity.Value,
            line.Quantity.Unit.Code,
            line.UnitPrice.Amount,
            line.LineTotal.Amount,
            line.IsFullyReceived))],
    };

    /// <inheritdoc />
    public async Task<PagedResult<SupplierInvoiceSummary>> SearchSupplierInvoicesAsync(
        Guid? supplierId,
        string? status,
        bool openOnly,
        PageRequest page,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);

        IQueryable<SupplierInvoice> query = _context.SupplierInvoices.AsNoTracking();

        if (supplierId is { } supplier)
        {
            query = query.Where(invoice => invoice.SupplierId == new SupplierRef(supplier));
        }

        // An unrecognized status returns nothing rather than everything. Silently dropping a
        // filter somebody typed is how a person reads the wrong list and believes it.
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse(status, ignoreCase: true, out SupplierInvoiceStatus parsed)
                || parsed == SupplierInvoiceStatus.Unknown)
            {
                return PagedResult<SupplierInvoiceSummary>.Empty(page.Page, page.PageSize);
            }

            query = query.Where(invoice => invoice.Status == parsed);
        }
        else if (openOnly)
        {
            query = query.Where(invoice =>
                invoice.Status == SupplierInvoiceStatus.Drafted
                || invoice.Status == SupplierInvoiceStatus.Disputed);
        }

        int total = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        // The totals are computed properties over the lines — the VAT one spreads the rebate
        // across the rate bands — so the rows are materialized and projected in memory. The page
        // is fifty invoices, and the alternative is duplicating that arithmetic in SQL where it
        // would quietly disagree with the aggregate one day.
        List<SupplierInvoice> rows = await query
            .Include(invoice => invoice.Lines)
            .OrderByDescending(invoice => invoice.ReceivedOn)
            .ThenBy(invoice => invoice.SupplierCode)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<SupplierInvoiceSummary> items =
        [
            .. rows.Select(invoice => new SupplierInvoiceSummary(
                invoice.Id.Value,
                invoice.SupplierId.Value,
                invoice.SupplierCode,
                invoice.SupplierDocumentNumber,
                invoice.DocumentDate,
                invoice.ReceivedOn,
                invoice.Status.ToString(),
                invoice.NetTotal.Amount,
                invoice.GrossTotal.Amount,
                invoice.StatedGrossTotal?.Amount,
                invoice.StatedGrossTotal is null
                    ? null
                    : invoice.StatedGrossTotal.Amount - invoice.GrossTotal.Amount,
                invoice.CurrencyCode,
                invoice.Lines.Count,
                invoice.Reason)),
        ];

        return PagedResult<SupplierInvoiceSummary>.Create(
            items, page.Page, page.PageSize, total);
    }

    /// <inheritdoc />
    public async Task<SupplierInvoiceDetail?> GetSupplierInvoiceAsync(
        Guid supplierInvoiceId,
        CancellationToken cancellationToken = default)
    {
        var id = new SupplierInvoiceId(supplierInvoiceId);

        SupplierInvoice? invoice = await _context.SupplierInvoices
            .AsNoTracking()
            .Include(row => row.Lines)
            .FirstOrDefaultAsync(row => row.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (invoice is null)
        {
            return null;
        }

        IReadOnlyList<SupplierInvoiceLineDto> lines =
        [
            .. invoice.Lines.Select(line => new SupplierInvoiceLineDto(
                line.Id.Value,
                line.PurchaseOrderId.Value,
                line.PurchaseOrderLineId.Value,
                line.PartId.Value,
                line.Sku,
                line.Description,
                line.Quantity.Value,
                line.Quantity.Unit.Code,
                line.UnitPrice.Amount,
                line.LineTotal.Amount,
                line.VatRatePercent,

                // Null means the price fell back to the purchase order's, because nobody had
                // agreed one for that day. That is the line worth a person's eye.
                line.PriceSource is not null)),
        ];

        return new SupplierInvoiceDetail(
            invoice.Id.Value,
            invoice.SupplierId.Value,
            invoice.SupplierCode,
            invoice.SupplierDocumentNumber,
            invoice.DocumentDate,
            invoice.ReceivedOn,
            invoice.Status.ToString(),
            invoice.LinesTotal.Amount,
            invoice.RappelRatePercent,
            invoice.RappelAmount.Amount,
            invoice.NetTotal.Amount,
            invoice.VatTotal.Amount,
            invoice.GrossTotal.Amount,
            invoice.StatedGrossTotal?.Amount,
            invoice.StatedGrossTotal is null
                ? null
                : invoice.StatedGrossTotal.Amount - invoice.GrossTotal.Amount,
            invoice.CurrencyCode,
            invoice.Reason,
            invoice.IsOpen,
            lines);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SupplierAgreementDto>> ListAgreementsAsync(
        Guid? supplierId,
        bool liveOnly,
        CancellationToken cancellationToken = default)
    {
        DateOnly today = _clock.TodayUtc;

        IQueryable<SupplierAgreement> query = _context.SupplierAgreements
            .AsNoTracking()
            .Include(agreement => agreement.RappelSteps);

        if (supplierId is { } supplier)
        {
            query = query.Where(agreement => agreement.SupplierId == new SupplierRef(supplier));
        }

        if (liveOnly)
        {
            query = query.Where(agreement =>
                agreement.EffectiveFrom <= today
                && (agreement.EffectiveTo == null || agreement.EffectiveTo >= today));
        }

        List<SupplierAgreement> rows = await query
            .OrderBy(agreement => agreement.SupplierCode)
            .ThenByDescending(agreement => agreement.EffectiveFrom)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. rows.Select(agreement => new SupplierAgreementDto(
                agreement.Id.Value,
                agreement.SupplierId.Value,
                agreement.SupplierCode,
                agreement.EffectiveFrom,
                agreement.EffectiveTo,
                agreement.IsEffectiveOn(today),
                agreement.RappelBasis.ToString(),
                agreement.RappelPeriod.ToString(),
                [
                    .. agreement.RappelSteps
                        .OrderBy(step => step.From.Amount)
                        .Select(step => new RappelStepDto(step.From.Amount, step.Percent)),
                ],
                agreement.CurrencyCode,
                agreement.Note)),
        ];
    }

    /// <inheritdoc />
    public async Task<PagedResult<SupplierPriceDto>> SearchSupplierPricesAsync(
        Guid? supplierId,
        Guid? partId,
        bool currentOnly,
        PageRequest page,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);

        DateOnly today = _clock.TodayUtc;

        IQueryable<SupplierPrice> query = _context.SupplierPrices.AsNoTracking();

        if (supplierId is { } supplier)
        {
            query = query.Where(price => price.SupplierId == new SupplierRef(supplier));
        }

        if (partId is { } part)
        {
            query = query.Where(price => price.PartId == new PartRef(part));
        }

        if (currentOnly)
        {
            query = query.Where(price => price.EffectiveFrom <= today);
        }

        int total = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        var rows = await query
            .OrderBy(price => price.SupplierId)
            .ThenBy(price => price.PartId)
            .ThenByDescending(price => price.EffectiveFrom)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(price => new
            {
                price.Id,
                price.SupplierId,
                price.PartId,
                price.SupplierPartNumber,
                Amount = price.UnitPrice.Amount,
                CurrencyCode = price.UnitPrice.Currency.Code,
                price.EffectiveFrom,
                price.Note,

                // In force today means: nothing later has started yet for this supplier and part.
                // A rise is a new row, so a list without this reads as several prices for one
                // thing and a buyer cannot tell which one the system will actually use.
                IsCurrent = price.EffectiveFrom <= today
                    && !_context.SupplierPrices.Any(later =>
                        later.SupplierId == price.SupplierId
                        && later.PartId == price.PartId
                        && later.EffectiveFrom <= today
                        && later.EffectiveFrom > price.EffectiveFrom),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<SupplierPriceDto> items =
        [
            .. rows.Select(row => new SupplierPriceDto(
                row.Id.Value,
                row.SupplierId.Value,
                row.PartId.Value,
                row.SupplierPartNumber,
                row.Amount,
                row.CurrencyCode,
                row.EffectiveFrom,
                row.IsCurrent,
                row.Note)),
        ];

        return PagedResult<SupplierPriceDto>.Create(items, page.Page, page.PageSize, total);
    }
}
