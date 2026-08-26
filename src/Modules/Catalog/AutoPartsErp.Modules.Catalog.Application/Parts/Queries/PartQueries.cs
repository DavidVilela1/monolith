using AutoPartsErp.Modules.Catalog.Application.Abstractions;
using AutoPartsErp.Modules.Catalog.Application.Contracts;
using AutoPartsErp.Modules.Catalog.Domain;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Paging;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Catalog.Application.Parts.Queries;

/// <summary>Loads the full detail of one part.</summary>
/// <param name="PartId">The part to load.</param>
public sealed record GetPartByIdQuery(Guid PartId) : IQuery<PartDetail>;

/// <summary>Serves <see cref="GetPartByIdQuery"/> from the read store.</summary>
public sealed class GetPartByIdQueryHandler : IQueryHandler<GetPartByIdQuery, PartDetail>
{
    private readonly ICatalogReadStore _readStore;

    /// <summary>Initializes the handler.</summary>
    public GetPartByIdQueryHandler(ICatalogReadStore readStore)
    {
        _readStore = readStore;
    }

    /// <inheritdoc />
    public async Task<Result<PartDetail>> HandleAsync(
        GetPartByIdQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        PartDetail? part = await _readStore.GetPartAsync(request.PartId, cancellationToken)
            .ConfigureAwait(false);

        return part is null
            ? Result.Failure<PartDetail>(CatalogErrors.Part.NotFound(request.PartId.ToString()))
            : part;
    }
}

/// <summary>Loads the full detail of one part by SKU, the way a counter terminal looks it up.</summary>
/// <param name="Sku">The stock keeping unit.</param>
public sealed record GetPartBySkuQuery(string Sku) : IQuery<PartDetail>;

/// <summary>Serves <see cref="GetPartBySkuQuery"/> from the read store.</summary>
public sealed class GetPartBySkuQueryHandler : IQueryHandler<GetPartBySkuQuery, PartDetail>
{
    private readonly ICatalogReadStore _readStore;

    /// <summary>Initializes the handler.</summary>
    public GetPartBySkuQueryHandler(ICatalogReadStore readStore)
    {
        _readStore = readStore;
    }

    /// <inheritdoc />
    public async Task<Result<PartDetail>> HandleAsync(
        GetPartBySkuQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        PartDetail? part = await _readStore
            .GetPartBySkuAsync(request.Sku?.Trim().ToUpperInvariant() ?? string.Empty, cancellationToken)
            .ConfigureAwait(false);

        return part is null
            ? Result.Failure<PartDetail>(CatalogErrors.Part.NotFound(request.Sku ?? string.Empty))
            : part;
    }
}

/// <summary>
/// The counter search: one box, any number, any spelling.
/// </summary>
/// <param name="Term">Free text, matched against SKU, part number and every cross-reference.</param>
/// <param name="BrandId">Optional brand filter.</param>
/// <param name="CategoryId">Optional category filter.</param>
/// <param name="Status">Optional status filter.</param>
/// <param name="RequiresCoreReturn">Optional filter for parts sold against a core.</param>
/// <param name="Page">One-based page number.</param>
/// <param name="PageSize">Rows per page.</param>
public sealed record SearchPartsQuery(
    string? Term = null,
    Guid? BrandId = null,
    Guid? CategoryId = null,
    string? Status = null,
    bool? RequiresCoreReturn = null,
    int Page = 1,
    int PageSize = PageRequest.DefaultPageSize) : IQuery<PagedResult<PartSummary>>;

/// <summary>Serves <see cref="SearchPartsQuery"/> from the read store.</summary>
public sealed class SearchPartsQueryHandler : IQueryHandler<SearchPartsQuery, PagedResult<PartSummary>>
{
    private readonly ICatalogReadStore _readStore;

    /// <summary>Initializes the handler.</summary>
    public SearchPartsQueryHandler(ICatalogReadStore readStore)
    {
        _readStore = readStore;
    }

    /// <inheritdoc />
    public async Task<Result<PagedResult<PartSummary>>> HandleAsync(
        SearchPartsQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var criteria = new PartSearchCriteria
        {
            Term = request.Term,
            BrandId = request.BrandId,
            CategoryId = request.CategoryId,
            Status = request.Status,
            RequiresCoreReturn = request.RequiresCoreReturn,
        };

        PagedResult<PartSummary> page = await _readStore
            .SearchPartsAsync(criteria, PageRequest.Of(request.Page, request.PageSize), cancellationToken)
            .ConfigureAwait(false);

        return page;
    }
}

/// <summary>
/// Finds every part recorded as fitting a vehicle. This is the lookup the whole catalogue
/// exists to serve.
/// </summary>
/// <param name="Make">Vehicle manufacturer.</param>
/// <param name="Model">Model designation.</param>
/// <param name="Year">Model year.</param>
/// <param name="EngineCode">Optional engine or type code.</param>
/// <param name="Page">One-based page number.</param>
/// <param name="PageSize">Rows per page.</param>
public sealed record FindPartsForVehicleQuery(
    string Make,
    string Model,
    int Year,
    string? EngineCode = null,
    int Page = 1,
    int PageSize = PageRequest.DefaultPageSize) : IQuery<PagedResult<PartSummary>>;

/// <summary>Checks the shape of a <see cref="FindPartsForVehicleQuery"/>.</summary>
public sealed class FindPartsForVehicleQueryValidator : IValidator<FindPartsForVehicleQuery>
{
    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(
        FindPartsForVehicleQuery instance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var failures = new List<ValidationFailure>();

        if (string.IsNullOrWhiteSpace(instance.Make))
        {
            failures.Add(new ValidationFailure(nameof(instance.Make), "required", "A vehicle make is required."));
        }

        if (string.IsNullOrWhiteSpace(instance.Model))
        {
            failures.Add(new ValidationFailure(nameof(instance.Model), "required", "A vehicle model is required."));
        }

        if (instance.Year < Domain.Parts.Fitment.EarliestYear)
        {
            failures.Add(new ValidationFailure(
                nameof(instance.Year), "out_of_range", "That model year is not plausible."));
        }

        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}

/// <summary>Serves <see cref="FindPartsForVehicleQuery"/> from the read store.</summary>
public sealed class FindPartsForVehicleQueryHandler
    : IQueryHandler<FindPartsForVehicleQuery, PagedResult<PartSummary>>
{
    private readonly ICatalogReadStore _readStore;

    /// <summary>Initializes the handler.</summary>
    public FindPartsForVehicleQueryHandler(ICatalogReadStore readStore)
    {
        _readStore = readStore;
    }

    /// <inheritdoc />
    public async Task<Result<PagedResult<PartSummary>>> HandleAsync(
        FindPartsForVehicleQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var vehicle = new VehicleCriteria(request.Make, request.Model, request.Year, request.EngineCode);

        PagedResult<PartSummary> page = await _readStore
            .FindPartsForVehicleAsync(vehicle, PageRequest.Of(request.Page, request.PageSize), cancellationToken)
            .ConfigureAwait(false);

        return page;
    }
}
