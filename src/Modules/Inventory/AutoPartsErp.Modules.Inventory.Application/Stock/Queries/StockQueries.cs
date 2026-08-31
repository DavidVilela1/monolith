using AutoPartsErp.Modules.Inventory.Application.Abstractions;
using AutoPartsErp.Modules.Inventory.Application.Contracts;
using AutoPartsErp.Modules.Inventory.Domain;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Paging;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Inventory.Application.Stock.Queries;

/// <summary>Where a part stands across every warehouse.</summary>
/// <param name="PartId">The part.</param>
public sealed record GetPartStockQuery(Guid PartId) : IQuery<PartStockPosition>;

/// <summary>Serves <see cref="GetPartStockQuery"/> from the read store.</summary>
public sealed class GetPartStockQueryHandler : IQueryHandler<GetPartStockQuery, PartStockPosition>
{
    private readonly IInventoryReadStore _readStore;

    /// <summary>Initializes the handler.</summary>
    public GetPartStockQueryHandler(IInventoryReadStore readStore)
    {
        _readStore = readStore;
    }

    /// <inheritdoc />
    public async Task<Result<PartStockPosition>> HandleAsync(
        GetPartStockQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        PartStockPosition? position = await _readStore
            .GetPartStockAsync(request.PartId, cancellationToken)
            .ConfigureAwait(false);

        return position is null
            ? Result.Failure<PartStockPosition>(
                InventoryErrors.Stock.NotFound(request.PartId.ToString(), "any warehouse"))
            : position;
    }
}

/// <summary>The balance for one part in one warehouse.</summary>
/// <param name="PartId">The part.</param>
/// <param name="WarehouseId">The warehouse.</param>
public sealed record GetStockBalanceQuery(Guid PartId, Guid WarehouseId) : IQuery<StockBalance>;

/// <summary>Serves <see cref="GetStockBalanceQuery"/> from the read store.</summary>
public sealed class GetStockBalanceQueryHandler : IQueryHandler<GetStockBalanceQuery, StockBalance>
{
    private readonly IInventoryReadStore _readStore;

    /// <summary>Initializes the handler.</summary>
    public GetStockBalanceQueryHandler(IInventoryReadStore readStore)
    {
        _readStore = readStore;
    }

    /// <inheritdoc />
    public async Task<Result<StockBalance>> HandleAsync(
        GetStockBalanceQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        StockBalance? balance = await _readStore
            .GetBalanceAsync(request.PartId, request.WarehouseId, cancellationToken)
            .ConfigureAwait(false);

        return balance is null
            ? Result.Failure<StockBalance>(
                InventoryErrors.Stock.NotFound(request.PartId.ToString(), request.WarehouseId.ToString()))
            : balance;
    }
}

/// <summary>The claims currently held against a balance.</summary>
/// <param name="PartId">The part.</param>
/// <param name="WarehouseId">The warehouse.</param>
/// <param name="ActiveOnly">True to exclude released, expired and fulfilled claims.</param>
public sealed record GetReservationsQuery(Guid PartId, Guid WarehouseId, bool ActiveOnly = true)
    : IQuery<IReadOnlyList<ReservationDto>>;

/// <summary>Serves <see cref="GetReservationsQuery"/> from the read store.</summary>
public sealed class GetReservationsQueryHandler
    : IQueryHandler<GetReservationsQuery, IReadOnlyList<ReservationDto>>
{
    private readonly IInventoryReadStore _readStore;

    /// <summary>Initializes the handler.</summary>
    public GetReservationsQueryHandler(IInventoryReadStore readStore)
    {
        _readStore = readStore;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<ReservationDto>>> HandleAsync(
        GetReservationsQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        IReadOnlyList<ReservationDto> reservations = await _readStore
            .GetReservationsAsync(request.PartId, request.WarehouseId, request.ActiveOnly, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(reservations);
    }
}

/// <summary>Everything at or below its reorder point.</summary>
/// <param name="WarehouseId">Restrict to one warehouse, or null for all of them.</param>
/// <param name="Page">One-based page number.</param>
/// <param name="PageSize">Rows per page.</param>
public sealed record GetReplenishmentListQuery(
    Guid? WarehouseId = null,
    int Page = 1,
    int PageSize = PageRequest.DefaultPageSize) : IQuery<PagedResult<StockBalance>>;

/// <summary>Serves <see cref="GetReplenishmentListQuery"/> from the read store.</summary>
public sealed class GetReplenishmentListQueryHandler
    : IQueryHandler<GetReplenishmentListQuery, PagedResult<StockBalance>>
{
    private readonly IInventoryReadStore _readStore;

    /// <summary>Initializes the handler.</summary>
    public GetReplenishmentListQueryHandler(IInventoryReadStore readStore)
    {
        _readStore = readStore;
    }

    /// <inheritdoc />
    public async Task<Result<PagedResult<StockBalance>>> HandleAsync(
        GetReplenishmentListQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        PagedResult<StockBalance> page = await _readStore
            .GetReplenishmentListAsync(
                request.WarehouseId,
                PageRequest.Of(request.Page, request.PageSize),
                cancellationToken)
            .ConfigureAwait(false);

        return page;
    }
}

/// <summary>The ledger for a part: every movement, newest first.</summary>
/// <param name="PartId">The part.</param>
/// <param name="WarehouseId">Restrict to one warehouse, or null for all of them.</param>
/// <param name="From">Earliest movement to include.</param>
/// <param name="To">Latest movement to include.</param>
/// <param name="Page">One-based page number.</param>
/// <param name="PageSize">Rows per page.</param>
public sealed record GetStockMovementsQuery(
    Guid PartId,
    Guid? WarehouseId = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    int Page = 1,
    int PageSize = PageRequest.DefaultPageSize) : IQuery<PagedResult<StockMovementDto>>;

/// <summary>Serves <see cref="GetStockMovementsQuery"/> from the read store.</summary>
public sealed class GetStockMovementsQueryHandler
    : IQueryHandler<GetStockMovementsQuery, PagedResult<StockMovementDto>>
{
    private readonly IInventoryReadStore _readStore;

    /// <summary>Initializes the handler.</summary>
    public GetStockMovementsQueryHandler(IInventoryReadStore readStore)
    {
        _readStore = readStore;
    }

    /// <inheritdoc />
    public async Task<Result<PagedResult<StockMovementDto>>> HandleAsync(
        GetStockMovementsQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        PagedResult<StockMovementDto> page = await _readStore
            .GetMovementsAsync(
                request.PartId,
                request.WarehouseId,
                request.From,
                request.To,
                PageRequest.Of(request.Page, request.PageSize),
                cancellationToken)
            .ConfigureAwait(false);

        return page;
    }
}
