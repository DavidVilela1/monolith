using AutoPartsErp.Modules.Sales.Application.Abstractions;
using AutoPartsErp.Modules.Sales.Application.Contracts;
using AutoPartsErp.Modules.Sales.Domain;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Paging;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Sales.Application.Orders.Queries;

/// <summary>Loads one sales order in full.</summary>
/// <param name="SalesOrderId">The order.</param>
public sealed record GetSalesOrderQuery(Guid SalesOrderId) : IQuery<SalesOrderDetail>;

/// <summary>Serves <see cref="GetSalesOrderQuery"/> from the read store.</summary>
public sealed class GetSalesOrderQueryHandler : IQueryHandler<GetSalesOrderQuery, SalesOrderDetail>
{
    private readonly ISalesReadStore _readStore;

    /// <summary>Initializes the handler.</summary>
    public GetSalesOrderQueryHandler(ISalesReadStore readStore)
    {
        _readStore = readStore;
    }

    /// <inheritdoc />
    public async Task<Result<SalesOrderDetail>> HandleAsync(
        GetSalesOrderQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        SalesOrderDetail? order = await _readStore
            .GetOrderAsync(request.SalesOrderId, cancellationToken)
            .ConfigureAwait(false);

        return order is null
            ? Result.Failure<SalesOrderDetail>(
                SalesErrors.Order.NotFound(request.SalesOrderId.ToString()))
            : order;
    }
}

/// <summary>Loads one order by its number, the way a customer quotes it on the phone.</summary>
/// <param name="OrderNumber">The order number, e.g. "SO-2026-01188".</param>
public sealed record GetSalesOrderByNumberQuery(string OrderNumber) : IQuery<SalesOrderDetail>;

/// <summary>Serves <see cref="GetSalesOrderByNumberQuery"/> from the read store.</summary>
public sealed class GetSalesOrderByNumberQueryHandler
    : IQueryHandler<GetSalesOrderByNumberQuery, SalesOrderDetail>
{
    private readonly ISalesReadStore _readStore;

    /// <summary>Initializes the handler.</summary>
    public GetSalesOrderByNumberQueryHandler(ISalesReadStore readStore)
    {
        _readStore = readStore;
    }

    /// <inheritdoc />
    public async Task<Result<SalesOrderDetail>> HandleAsync(
        GetSalesOrderByNumberQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string number = request.OrderNumber?.Trim().ToUpperInvariant() ?? string.Empty;

        SalesOrderDetail? order = await _readStore
            .GetOrderByNumberAsync(number, cancellationToken)
            .ConfigureAwait(false);

        return order is null
            ? Result.Failure<SalesOrderDetail>(SalesErrors.Order.NotFound(number))
            : order;
    }
}

/// <summary>Searches sales orders.</summary>
/// <param name="Term">Free text, matched against order number, customer code and their reference.</param>
/// <param name="CustomerId">Restrict to one customer.</param>
/// <param name="WarehouseId">Restrict to orders coming out of one warehouse.</param>
/// <param name="Status">Restrict to one status.</param>
/// <param name="Kind">Restrict to CounterSale or Order.</param>
/// <param name="OutstandingOnly">True for the picking list: only orders that still owe something.</param>
/// <param name="LateOnly">True for only those past their required-by date with goods still owed.</param>
/// <param name="Page">One-based page number.</param>
/// <param name="PageSize">Rows per page.</param>
public sealed record SearchSalesOrdersQuery(
    string? Term = null,
    Guid? CustomerId = null,
    Guid? WarehouseId = null,
    string? Status = null,
    string? Kind = null,
    bool OutstandingOnly = false,
    bool LateOnly = false,
    int Page = 1,
    int PageSize = PageRequest.DefaultPageSize) : IQuery<PagedResult<SalesOrderSummary>>;

/// <summary>Serves <see cref="SearchSalesOrdersQuery"/> from the read store.</summary>
public sealed class SearchSalesOrdersQueryHandler
    : IQueryHandler<SearchSalesOrdersQuery, PagedResult<SalesOrderSummary>>
{
    private readonly ISalesReadStore _readStore;

    /// <summary>Initializes the handler.</summary>
    public SearchSalesOrdersQueryHandler(ISalesReadStore readStore)
    {
        _readStore = readStore;
    }

    /// <inheritdoc />
    public async Task<Result<PagedResult<SalesOrderSummary>>> HandleAsync(
        SearchSalesOrdersQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var criteria = new SalesOrderSearchCriteria
        {
            Term = request.Term,
            CustomerId = request.CustomerId,
            WarehouseId = request.WarehouseId,
            Status = request.Status,
            Kind = request.Kind,
            OutstandingOnly = request.OutstandingOnly,
            LateOnly = request.LateOnly,
        };

        PagedResult<SalesOrderSummary> page = await _readStore
            .SearchOrdersAsync(criteria, PageRequest.Of(request.Page, request.PageSize), cancellationToken)
            .ConfigureAwait(false);

        return page;
    }
}

/// <summary>Loads one customer account.</summary>
/// <param name="CustomerId">The customer.</param>
public sealed record GetCustomerAccountQuery(Guid CustomerId) : IQuery<CustomerAccountDto>;

/// <summary>Serves <see cref="GetCustomerAccountQuery"/> from the read store.</summary>
public sealed class GetCustomerAccountQueryHandler
    : IQueryHandler<GetCustomerAccountQuery, CustomerAccountDto>
{
    private readonly ISalesReadStore _readStore;

    /// <summary>Initializes the handler.</summary>
    public GetCustomerAccountQueryHandler(ISalesReadStore readStore)
    {
        _readStore = readStore;
    }

    /// <inheritdoc />
    public async Task<Result<CustomerAccountDto>> HandleAsync(
        GetCustomerAccountQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        CustomerAccountDto? account = await _readStore
            .GetCustomerAsync(request.CustomerId, cancellationToken)
            .ConfigureAwait(false);

        return account is null
            ? Result.Failure<CustomerAccountDto>(
                SalesErrors.Customer.NotFound(request.CustomerId.ToString()))
            : account;
    }
}

/// <summary>Loads one customer account by code, the way the counter looks one up.</summary>
/// <param name="Code">Their short code.</param>
public sealed record GetCustomerAccountByCodeQuery(string Code) : IQuery<CustomerAccountDto>;

/// <summary>Serves <see cref="GetCustomerAccountByCodeQuery"/> from the read store.</summary>
public sealed class GetCustomerAccountByCodeQueryHandler
    : IQueryHandler<GetCustomerAccountByCodeQuery, CustomerAccountDto>
{
    private readonly ISalesReadStore _readStore;

    /// <summary>Initializes the handler.</summary>
    public GetCustomerAccountByCodeQueryHandler(ISalesReadStore readStore)
    {
        _readStore = readStore;
    }

    /// <inheritdoc />
    public async Task<Result<CustomerAccountDto>> HandleAsync(
        GetCustomerAccountByCodeQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string code = request.Code?.Trim().ToUpperInvariant() ?? string.Empty;

        CustomerAccountDto? account = await _readStore
            .GetCustomerByCodeAsync(code, cancellationToken)
            .ConfigureAwait(false);

        return account is null
            ? Result.Failure<CustomerAccountDto>(SalesErrors.Customer.NotFound(code))
            : account;
    }
}

/// <summary>Searches customer accounts by code or name.</summary>
/// <param name="Term">Free text.</param>
/// <param name="Status">Restrict to Active, OnHold or Closed.</param>
/// <param name="Page">One-based page number.</param>
/// <param name="PageSize">Rows per page.</param>
public sealed record SearchCustomerAccountsQuery(
    string? Term = null,
    string? Status = null,
    int Page = 1,
    int PageSize = PageRequest.DefaultPageSize) : IQuery<PagedResult<CustomerAccountDto>>;

/// <summary>Serves <see cref="SearchCustomerAccountsQuery"/> from the read store.</summary>
public sealed class SearchCustomerAccountsQueryHandler
    : IQueryHandler<SearchCustomerAccountsQuery, PagedResult<CustomerAccountDto>>
{
    private readonly ISalesReadStore _readStore;

    /// <summary>Initializes the handler.</summary>
    public SearchCustomerAccountsQueryHandler(ISalesReadStore readStore)
    {
        _readStore = readStore;
    }

    /// <inheritdoc />
    public async Task<Result<PagedResult<CustomerAccountDto>>> HandleAsync(
        SearchCustomerAccountsQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        PagedResult<CustomerAccountDto> page = await _readStore
            .SearchCustomersAsync(
                request.Term,
                request.Status,
                PageRequest.Of(request.Page, request.PageSize),
                cancellationToken)
            .ConfigureAwait(false);

        return page;
    }
}
