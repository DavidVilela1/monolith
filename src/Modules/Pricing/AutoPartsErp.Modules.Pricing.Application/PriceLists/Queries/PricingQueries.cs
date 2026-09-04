using AutoPartsErp.Modules.Pricing.Application.Abstractions;
using AutoPartsErp.Modules.Pricing.Application.Contracts;
using AutoPartsErp.Modules.Pricing.Domain;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Paging;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Pricing.Application.PriceLists.Queries;

/// <summary>Loads one price list.</summary>
/// <param name="PriceListId">The list.</param>
public sealed record GetPriceListQuery(Guid PriceListId) : IQuery<PriceListSummary>;

/// <summary>Loads the list.</summary>
public sealed class GetPriceListQueryHandler : IQueryHandler<GetPriceListQuery, PriceListSummary>
{
    private readonly IPricingReadStore _readStore;

    /// <summary>Initializes the handler.</summary>
    public GetPriceListQueryHandler(IPricingReadStore readStore)
    {
        _readStore = readStore;
    }

    /// <inheritdoc />
    public async Task<Result<PriceListSummary>> HandleAsync(
        GetPriceListQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        PriceListSummary? list = await _readStore
            .GetListAsync(request.PriceListId, cancellationToken)
            .ConfigureAwait(false);

        return list is null
            ? Result.Failure<PriceListSummary>(
                PricingErrors.List.NotFound(request.PriceListId.ToString()))
            : list;
    }
}

/// <summary>Loads one price list by its code.</summary>
/// <param name="Code">The code.</param>
public sealed record GetPriceListByCodeQuery(string Code) : IQuery<PriceListSummary>;

/// <summary>Loads the list.</summary>
public sealed class GetPriceListByCodeQueryHandler
    : IQueryHandler<GetPriceListByCodeQuery, PriceListSummary>
{
    private readonly IPricingReadStore _readStore;

    /// <summary>Initializes the handler.</summary>
    public GetPriceListByCodeQueryHandler(IPricingReadStore readStore)
    {
        _readStore = readStore;
    }

    /// <inheritdoc />
    public async Task<Result<PriceListSummary>> HandleAsync(
        GetPriceListByCodeQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        PriceListSummary? list = await _readStore
            .GetListByCodeAsync(request.Code, cancellationToken)
            .ConfigureAwait(false);

        return list is null
            ? Result.Failure<PriceListSummary>(PricingErrors.List.NotFound(request.Code))
            : list;
    }
}

/// <summary>Lists price lists.</summary>
/// <param name="Criteria">What to look for.</param>
/// <param name="Page">Which page to return.</param>
public sealed record SearchPriceListsQuery(PriceListSearchCriteria Criteria, PageRequest Page)
    : IQuery<PagedResult<PriceListSummary>>;

/// <summary>Runs the search.</summary>
public sealed class SearchPriceListsQueryHandler
    : IQueryHandler<SearchPriceListsQuery, PagedResult<PriceListSummary>>
{
    private readonly IPricingReadStore _readStore;

    /// <summary>Initializes the handler.</summary>
    public SearchPriceListsQueryHandler(IPricingReadStore readStore)
    {
        _readStore = readStore;
    }

    /// <inheritdoc />
    public async Task<Result<PagedResult<PriceListSummary>>> HandleAsync(
        SearchPriceListsQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await _readStore
            .SearchListsAsync(request.Criteria, request.Page, cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>Lists the prices inside one list.</summary>
/// <param name="PriceListId">The list.</param>
/// <param name="Page">Which page to return.</param>
public sealed record ListPricesQuery(Guid PriceListId, PageRequest Page)
    : IQuery<PagedResult<PriceListEntryDto>>;

/// <summary>Lists the prices.</summary>
public sealed class ListPricesQueryHandler
    : IQueryHandler<ListPricesQuery, PagedResult<PriceListEntryDto>>
{
    private readonly IPricingReadStore _readStore;

    /// <summary>Initializes the handler.</summary>
    public ListPricesQueryHandler(IPricingReadStore readStore)
    {
        _readStore = readStore;
    }

    /// <inheritdoc />
    public async Task<Result<PagedResult<PriceListEntryDto>>> HandleAsync(
        ListPricesQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await _readStore
            .ListPricesAsync(request.PriceListId, request.Page, cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>Loads what one list says one part costs.</summary>
/// <param name="PriceListId">The list.</param>
/// <param name="PartId">The part.</param>
public sealed record GetPartPriceQuery(Guid PriceListId, Guid PartId) : IQuery<PriceListEntryDto>;

/// <summary>Loads the price.</summary>
public sealed class GetPartPriceQueryHandler : IQueryHandler<GetPartPriceQuery, PriceListEntryDto>
{
    private readonly IPricingReadStore _readStore;

    /// <summary>Initializes the handler.</summary>
    public GetPartPriceQueryHandler(IPricingReadStore readStore)
    {
        _readStore = readStore;
    }

    /// <inheritdoc />
    public async Task<Result<PriceListEntryDto>> HandleAsync(
        GetPartPriceQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        PriceListEntryDto? entry = await _readStore
            .GetPriceAsync(request.PriceListId, request.PartId, cancellationToken)
            .ConfigureAwait(false);

        return entry is null
            ? Result.Failure<PriceListEntryDto>(
                PricingErrors.Entry.NotFound(request.PartId.ToString()))
            : entry;
    }
}

/// <summary>Loads the terms agreed with one customer.</summary>
/// <param name="CustomerId">The customer.</param>
public sealed record GetCustomerPricingQuery(Guid CustomerId) : IQuery<CustomerPricingDto>;

/// <summary>Loads the agreement.</summary>
public sealed class GetCustomerPricingQueryHandler
    : IQueryHandler<GetCustomerPricingQuery, CustomerPricingDto>
{
    private readonly IPricingReadStore _readStore;

    /// <summary>Initializes the handler.</summary>
    public GetCustomerPricingQueryHandler(IPricingReadStore readStore)
    {
        _readStore = readStore;
    }

    /// <inheritdoc />
    public async Task<Result<CustomerPricingDto>> HandleAsync(
        GetCustomerPricingQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        CustomerPricingDto? agreement = await _readStore
            .GetAgreementAsync(request.CustomerId, cancellationToken)
            .ConfigureAwait(false);

        return agreement is null
            ? Result.Failure<CustomerPricingDto>(
                PricingErrors.Agreement.NotFound(request.CustomerId.ToString()))
            : agreement;
    }
}

/// <summary>
/// Every customer agreement pointing at one list.
/// <para>
/// The question somebody asks before changing a price: who does this reach?
/// </para>
/// </summary>
/// <param name="PriceListId">The list.</param>
/// <param name="Page">Which page to return.</param>
public sealed record ListAgreementsForListQuery(Guid PriceListId, PageRequest Page)
    : IQuery<PagedResult<CustomerPricingDto>>;

/// <summary>Lists the agreements.</summary>
public sealed class ListAgreementsForListQueryHandler
    : IQueryHandler<ListAgreementsForListQuery, PagedResult<CustomerPricingDto>>
{
    private readonly IPricingReadStore _readStore;

    /// <summary>Initializes the handler.</summary>
    public ListAgreementsForListQueryHandler(IPricingReadStore readStore)
    {
        _readStore = readStore;
    }

    /// <inheritdoc />
    public async Task<Result<PagedResult<CustomerPricingDto>>> HandleAsync(
        ListAgreementsForListQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await _readStore
            .ListAgreementsForListAsync(request.PriceListId, request.Page, cancellationToken)
            .ConfigureAwait(false);
    }
}
