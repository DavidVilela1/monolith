using AutoPartsErp.Modules.Sales.Application.Contracts;
using AutoPartsErp.SharedKernel.Paging;

namespace AutoPartsErp.Modules.Sales.Application.Abstractions;

/// <summary>The read side of the Sales module.</summary>
public interface ISalesReadStore
{
    /// <summary>Loads one order in full, or null when it does not exist.</summary>
    Task<SalesOrderDetail?> GetOrderAsync(Guid salesOrderId, CancellationToken cancellationToken = default);

    /// <summary>Loads one order by its number, the way a customer quotes it on the phone.</summary>
    Task<SalesOrderDetail?> GetOrderByNumberAsync(string orderNumber, CancellationToken cancellationToken = default);

    /// <summary>Searches orders by number, customer code or the customer's own reference.</summary>
    /// <param name="criteria">What to look for.</param>
    /// <param name="page">Which page to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<PagedResult<SalesOrderSummary>> SearchOrdersAsync(
        SalesOrderSearchCriteria criteria,
        PageRequest page,
        CancellationToken cancellationToken = default);

    /// <summary>Loads one customer account, or null when Sales has no record of them.</summary>
    Task<CustomerAccountDto?> GetCustomerAsync(Guid customerId, CancellationToken cancellationToken = default);

    /// <summary>Loads one customer account by code, the way the counter looks one up.</summary>
    Task<CustomerAccountDto?> GetCustomerByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>Searches customer accounts by code or name.</summary>
    /// <param name="term">Free text.</param>
    /// <param name="status">Restrict to Active, OnHold or Closed.</param>
    /// <param name="page">Which page to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<PagedResult<CustomerAccountDto>> SearchCustomersAsync(
        string? term,
        string? status,
        PageRequest page,
        CancellationToken cancellationToken = default);
}

/// <summary>What to look for when searching sales orders.</summary>
public sealed record SalesOrderSearchCriteria
{
    /// <summary>Free text, matched against order number, customer code and customer reference.</summary>
    public string? Term { get; init; }

    /// <summary>Restrict to one customer.</summary>
    public Guid? CustomerId { get; init; }

    /// <summary>Restrict to orders coming out of one warehouse.</summary>
    public Guid? WarehouseId { get; init; }

    /// <summary>Restrict to one status.</summary>
    public string? Status { get; init; }

    /// <summary>Restrict to CounterSale or Order.</summary>
    public string? Kind { get; init; }

    /// <summary>True to return only orders that still owe the customer something.</summary>
    public bool OutstandingOnly { get; init; }

    /// <summary>True to return only orders past their required-by date with goods still owed.</summary>
    public bool LateOnly { get; init; }
}
