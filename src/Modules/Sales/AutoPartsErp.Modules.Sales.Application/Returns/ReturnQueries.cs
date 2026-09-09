using AutoPartsErp.Modules.Sales.Application.Abstractions;
using AutoPartsErp.Modules.Sales.Application.Contracts;
using AutoPartsErp.Modules.Sales.Domain;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Paging;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Sales.Application.Returns;

/// <summary>Loads one customer return in full.</summary>
/// <param name="CustomerReturnId">The return.</param>
public sealed record GetCustomerReturnQuery(Guid CustomerReturnId) : IQuery<CustomerReturnDetail>;

/// <summary>Serves <see cref="GetCustomerReturnQuery"/> from the read store.</summary>
public sealed class GetCustomerReturnQueryHandler
    : IQueryHandler<GetCustomerReturnQuery, CustomerReturnDetail>
{
    private readonly ISalesReadStore _readStore;

    /// <summary>Initializes the handler.</summary>
    public GetCustomerReturnQueryHandler(ISalesReadStore readStore)
    {
        _readStore = readStore;
    }

    /// <inheritdoc />
    public async Task<Result<CustomerReturnDetail>> HandleAsync(
        GetCustomerReturnQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        CustomerReturnDetail? found = await _readStore
            .GetReturnAsync(request.CustomerReturnId, cancellationToken)
            .ConfigureAwait(false);

        return found is null
            ? Result.Failure<CustomerReturnDetail>(
                SalesErrors.Return.NotFound(request.CustomerReturnId.ToString()))
            : found;
    }
}

/// <summary>Searches customer returns.</summary>
/// <param name="Term">Free text, matched against return number, order number and customer code.</param>
/// <param name="CustomerId">Restrict to one customer.</param>
/// <param name="Status">Restrict to Draft, Received or Cancelled.</param>
/// <param name="Page">Which page to return.</param>
/// <param name="PageSize">How many per page.</param>
public sealed record SearchCustomerReturnsQuery(
    string? Term = null,
    Guid? CustomerId = null,
    string? Status = null,
    int Page = 1,
    int PageSize = PageRequest.DefaultPageSize) : IQuery<PagedResult<CustomerReturnSummary>>;

/// <summary>Serves <see cref="SearchCustomerReturnsQuery"/> from the read store.</summary>
public sealed class SearchCustomerReturnsQueryHandler
    : IQueryHandler<SearchCustomerReturnsQuery, PagedResult<CustomerReturnSummary>>
{
    private readonly ISalesReadStore _readStore;

    /// <summary>Initializes the handler.</summary>
    public SearchCustomerReturnsQueryHandler(ISalesReadStore readStore)
    {
        _readStore = readStore;
    }

    /// <inheritdoc />
    public async Task<Result<PagedResult<CustomerReturnSummary>>> HandleAsync(
        SearchCustomerReturnsQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        PagedResult<CustomerReturnSummary> page = await _readStore
            .SearchReturnsAsync(
                request.Term,
                request.CustomerId,
                request.Status,
                PageRequest.Of(request.Page, request.PageSize),
                cancellationToken)
            .ConfigureAwait(false);

        return page;
    }
}
