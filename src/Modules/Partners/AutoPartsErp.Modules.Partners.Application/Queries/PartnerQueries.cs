using AutoPartsErp.Modules.Partners.Application.Abstractions;
using AutoPartsErp.Modules.Partners.Application.Contracts;
using AutoPartsErp.Modules.Partners.Domain;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Paging;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Partners.Application.Queries;

/// <summary>Loads one partner in full.</summary>
/// <param name="PartnerId">The partner.</param>
public sealed record GetPartnerQuery(Guid PartnerId) : IQuery<PartnerDetail>;

/// <summary>Serves <see cref="GetPartnerQuery"/> from the read store.</summary>
public sealed class GetPartnerQueryHandler : IQueryHandler<GetPartnerQuery, PartnerDetail>
{
    private readonly IPartnersReadStore _readStore;

    /// <summary>Initializes the handler.</summary>
    public GetPartnerQueryHandler(IPartnersReadStore readStore)
    {
        _readStore = readStore;
    }

    /// <inheritdoc />
    public async Task<Result<PartnerDetail>> HandleAsync(
        GetPartnerQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        PartnerDetail? partner = await _readStore
            .GetPartnerAsync(request.PartnerId, cancellationToken)
            .ConfigureAwait(false);

        return partner is null
            ? Result.Failure<PartnerDetail>(PartnerErrors.Partner.NotFound(request.PartnerId.ToString()))
            : partner;
    }
}

/// <summary>Loads one partner by code, the way the counter looks one up.</summary>
/// <param name="Code">Their short code.</param>
public sealed record GetPartnerByCodeQuery(string Code) : IQuery<PartnerDetail>;

/// <summary>Serves <see cref="GetPartnerByCodeQuery"/> from the read store.</summary>
public sealed class GetPartnerByCodeQueryHandler : IQueryHandler<GetPartnerByCodeQuery, PartnerDetail>
{
    private readonly IPartnersReadStore _readStore;

    /// <summary>Initializes the handler.</summary>
    public GetPartnerByCodeQueryHandler(IPartnersReadStore readStore)
    {
        _readStore = readStore;
    }

    /// <inheritdoc />
    public async Task<Result<PartnerDetail>> HandleAsync(
        GetPartnerByCodeQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string code = request.Code?.Trim().ToUpperInvariant() ?? string.Empty;

        PartnerDetail? partner = await _readStore
            .GetPartnerByCodeAsync(code, cancellationToken)
            .ConfigureAwait(false);

        return partner is null
            ? Result.Failure<PartnerDetail>(PartnerErrors.Partner.NotFound(code))
            : partner;
    }
}

/// <summary>Searches partners by code, name or tax number.</summary>
/// <param name="Term">Free text.</param>
/// <param name="IsCustomer">Restrict to customers.</param>
/// <param name="IsSupplier">Restrict to suppliers.</param>
/// <param name="Status">Restrict to Active, OnHold or Closed.</param>
/// <param name="Page">One-based page number.</param>
/// <param name="PageSize">Rows per page.</param>
public sealed record SearchPartnersQuery(
    string? Term = null,
    bool? IsCustomer = null,
    bool? IsSupplier = null,
    string? Status = null,
    int Page = 1,
    int PageSize = PageRequest.DefaultPageSize) : IQuery<PagedResult<PartnerSummary>>;

/// <summary>Serves <see cref="SearchPartnersQuery"/> from the read store.</summary>
public sealed class SearchPartnersQueryHandler
    : IQueryHandler<SearchPartnersQuery, PagedResult<PartnerSummary>>
{
    private readonly IPartnersReadStore _readStore;

    /// <summary>Initializes the handler.</summary>
    public SearchPartnersQueryHandler(IPartnersReadStore readStore)
    {
        _readStore = readStore;
    }

    /// <inheritdoc />
    public async Task<Result<PagedResult<PartnerSummary>>> HandleAsync(
        SearchPartnersQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var criteria = new PartnerSearchCriteria
        {
            Term = request.Term,
            IsCustomer = request.IsCustomer,
            IsSupplier = request.IsSupplier,
            Status = request.Status,
        };

        PagedResult<PartnerSummary> page = await _readStore
            .SearchAsync(criteria, PageRequest.Of(request.Page, request.PageSize), cancellationToken)
            .ConfigureAwait(false);

        return page;
    }
}
