using AutoPartsErp.Modules.Purchasing.Application.Abstractions;
using AutoPartsErp.Modules.Purchasing.Application.Contracts;
using AutoPartsErp.Modules.Purchasing.Domain;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Paging;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Purchasing.Application.Orders.Queries;

/// <summary>Loads one purchase order in full.</summary>
/// <param name="PurchaseOrderId">The order.</param>
public sealed record GetPurchaseOrderQuery(Guid PurchaseOrderId) : IQuery<PurchaseOrderDetail>;

/// <summary>Serves <see cref="GetPurchaseOrderQuery"/> from the read store.</summary>
public sealed class GetPurchaseOrderQueryHandler : IQueryHandler<GetPurchaseOrderQuery, PurchaseOrderDetail>
{
    private readonly IPurchasingReadStore _readStore;

    /// <summary>Initializes the handler.</summary>
    public GetPurchaseOrderQueryHandler(IPurchasingReadStore readStore)
    {
        _readStore = readStore;
    }

    /// <inheritdoc />
    public async Task<Result<PurchaseOrderDetail>> HandleAsync(
        GetPurchaseOrderQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        PurchaseOrderDetail? order = await _readStore
            .GetOrderAsync(request.PurchaseOrderId, cancellationToken)
            .ConfigureAwait(false);

        return order is null
            ? Result.Failure<PurchaseOrderDetail>(
                PurchasingErrors.Order.NotFound(request.PurchaseOrderId.ToString()))
            : order;
    }
}

/// <summary>Loads one order by its number, the way somebody reads it off a delivery note.</summary>
/// <param name="OrderNumber">The order number, e.g. "PO-2026-00042".</param>
public sealed record GetPurchaseOrderByNumberQuery(string OrderNumber) : IQuery<PurchaseOrderDetail>;

/// <summary>Serves <see cref="GetPurchaseOrderByNumberQuery"/> from the read store.</summary>
public sealed class GetPurchaseOrderByNumberQueryHandler
    : IQueryHandler<GetPurchaseOrderByNumberQuery, PurchaseOrderDetail>
{
    private readonly IPurchasingReadStore _readStore;

    /// <summary>Initializes the handler.</summary>
    public GetPurchaseOrderByNumberQueryHandler(IPurchasingReadStore readStore)
    {
        _readStore = readStore;
    }

    /// <inheritdoc />
    public async Task<Result<PurchaseOrderDetail>> HandleAsync(
        GetPurchaseOrderByNumberQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string number = request.OrderNumber?.Trim().ToUpperInvariant() ?? string.Empty;

        PurchaseOrderDetail? order = await _readStore
            .GetOrderByNumberAsync(number, cancellationToken)
            .ConfigureAwait(false);

        return order is null
            ? Result.Failure<PurchaseOrderDetail>(PurchasingErrors.Order.NotFound(number))
            : order;
    }
}

/// <summary>Searches purchase orders.</summary>
/// <param name="Term">Free text, matched against order number, supplier code and supplier reference.</param>
/// <param name="SupplierId">Restrict to one supplier.</param>
/// <param name="WarehouseId">Restrict to orders delivering into one warehouse.</param>
/// <param name="Status">Restrict to one status.</param>
/// <param name="OutstandingOnly">True for the buyer's working list: only orders with something still to come.</param>
/// <param name="OverdueOnly">True for only those whose expected date has passed with goods outstanding.</param>
/// <param name="Page">One-based page number.</param>
/// <param name="PageSize">Rows per page.</param>
public sealed record SearchPurchaseOrdersQuery(
    string? Term = null,
    Guid? SupplierId = null,
    Guid? WarehouseId = null,
    string? Status = null,
    bool OutstandingOnly = false,
    bool OverdueOnly = false,
    int Page = 1,
    int PageSize = PageRequest.DefaultPageSize) : IQuery<PagedResult<PurchaseOrderSummary>>;

/// <summary>Serves <see cref="SearchPurchaseOrdersQuery"/> from the read store.</summary>
public sealed class SearchPurchaseOrdersQueryHandler
    : IQueryHandler<SearchPurchaseOrdersQuery, PagedResult<PurchaseOrderSummary>>
{
    private readonly IPurchasingReadStore _readStore;

    /// <summary>Initializes the handler.</summary>
    public SearchPurchaseOrdersQueryHandler(IPurchasingReadStore readStore)
    {
        _readStore = readStore;
    }

    /// <inheritdoc />
    public async Task<Result<PagedResult<PurchaseOrderSummary>>> HandleAsync(
        SearchPurchaseOrdersQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var criteria = new PurchaseOrderSearchCriteria
        {
            Term = request.Term,
            SupplierId = request.SupplierId,
            WarehouseId = request.WarehouseId,
            Status = request.Status,
            OutstandingOnly = request.OutstandingOnly,
            OverdueOnly = request.OverdueOnly,
        };

        PagedResult<PurchaseOrderSummary> page = await _readStore
            .SearchOrdersAsync(criteria, PageRequest.Of(request.Page, request.PageSize), cancellationToken)
            .ConfigureAwait(false);

        return page;
    }
}
