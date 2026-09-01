using AutoPartsErp.Modules.Purchasing.Application.Abstractions;
using AutoPartsErp.Modules.Purchasing.Application.Contracts;
using AutoPartsErp.Modules.Purchasing.Domain;
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
            .OrderByDescending(suggestion => suggestion.ReorderPoint - suggestion.QuantityAvailable)
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
}
