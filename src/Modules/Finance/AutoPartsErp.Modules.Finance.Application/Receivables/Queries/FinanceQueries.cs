using AutoPartsErp.Modules.Finance.Application.Abstractions;
using AutoPartsErp.Modules.Finance.Application.Contracts;
using AutoPartsErp.Modules.Finance.Domain;
using AutoPartsErp.SharedKernel.Abstractions;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Paging;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Finance.Application.Receivables.Queries;

/// <summary>
/// What a customer owes, and the documents behind it.
/// </summary>
/// <param name="CustomerId">The customer.</param>
/// <param name="AsAt">
/// The date to judge overdue against. Defaults to today; a date is accepted so that a statement
/// can be reproduced as it looked when it was sent.
/// </param>
public sealed record GetCustomerStatementQuery(Guid CustomerId, DateOnly? AsAt = null)
    : IQuery<CustomerStatement>;

/// <summary>Loads the statement.</summary>
public sealed class GetCustomerStatementQueryHandler
    : IQueryHandler<GetCustomerStatementQuery, CustomerStatement>
{
    private readonly IFinanceReadStore _readStore;
    private readonly IDateTimeProvider _clock;

    /// <summary>Initializes the handler.</summary>
    public GetCustomerStatementQueryHandler(IFinanceReadStore readStore, IDateTimeProvider clock)
    {
        _readStore = readStore;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result<CustomerStatement>> HandleAsync(
        GetCustomerStatementQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        CustomerStatement? statement = await _readStore
            .GetStatementAsync(request.CustomerId, request.AsAt ?? _clock.TodayUtc, cancellationToken)
            .ConfigureAwait(false);

        return statement is null
            ? Result.Failure<CustomerStatement>(
                FinanceErrors.Terms.NotFound(request.CustomerId.ToString()))
            : statement;
    }
}

/// <summary>
/// The ageing report: every customer with a balance, in thirty-day buckets.
/// </summary>
/// <param name="AsAt">The date to age against. Defaults to today.</param>
/// <param name="Page">Which page to return.</param>
public sealed record GetAgingQuery(DateOnly? AsAt = null, PageRequest? Page = null)
    : IQuery<PagedResult<AgingRow>>;

/// <summary>Loads the ageing report.</summary>
public sealed class GetAgingQueryHandler : IQueryHandler<GetAgingQuery, PagedResult<AgingRow>>
{
    private readonly IFinanceReadStore _readStore;
    private readonly IDateTimeProvider _clock;

    /// <summary>Initializes the handler.</summary>
    public GetAgingQueryHandler(IFinanceReadStore readStore, IDateTimeProvider clock)
    {
        _readStore = readStore;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result<PagedResult<AgingRow>>> HandleAsync(
        GetAgingQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        PagedResult<AgingRow> rows = await _readStore
            .GetAgingAsync(
                request.AsAt ?? _clock.TodayUtc,
                request.Page ?? new PageRequest(),
                cancellationToken)
            .ConfigureAwait(false);

        return rows;
    }
}

/// <summary>
/// The documents on a customer's account with something still outstanding, oldest first.
/// </summary>
/// <param name="CustomerId">The customer.</param>
/// <param name="AsAt">The date to judge overdue against. Defaults to today.</param>
public sealed record ListOutstandingItemsQuery(Guid CustomerId, DateOnly? AsAt = null)
    : IQuery<IReadOnlyList<OpenItemDto>>;

/// <summary>Loads the outstanding documents.</summary>
public sealed class ListOutstandingItemsQueryHandler
    : IQueryHandler<ListOutstandingItemsQuery, IReadOnlyList<OpenItemDto>>
{
    private readonly IFinanceReadStore _readStore;
    private readonly IDateTimeProvider _clock;

    /// <summary>Initializes the handler.</summary>
    public ListOutstandingItemsQueryHandler(IFinanceReadStore readStore, IDateTimeProvider clock)
    {
        _readStore = readStore;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<OpenItemDto>>> HandleAsync(
        ListOutstandingItemsQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        IReadOnlyList<OpenItemDto> items = await _readStore
            .ListOutstandingAsync(
                request.CustomerId, request.AsAt ?? _clock.TodayUtc, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(items);
    }
}

/// <summary>Loads one receipt with what it paid.</summary>
/// <param name="ReceiptId">The receipt.</param>
public sealed record GetReceiptQuery(Guid ReceiptId) : IQuery<ReceiptDto>;

/// <summary>Loads the receipt.</summary>
public sealed class GetReceiptQueryHandler : IQueryHandler<GetReceiptQuery, ReceiptDto>
{
    private readonly IFinanceReadStore _readStore;

    /// <summary>Initializes the handler.</summary>
    public GetReceiptQueryHandler(IFinanceReadStore readStore)
    {
        _readStore = readStore;
    }

    /// <inheritdoc />
    public async Task<Result<ReceiptDto>> HandleAsync(
        GetReceiptQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        ReceiptDto? receipt = await _readStore
            .GetReceiptAsync(request.ReceiptId, cancellationToken)
            .ConfigureAwait(false);

        return receipt is null
            ? Result.Failure<ReceiptDto>(
                FinanceErrors.Receipt.NotFound(request.ReceiptId.ToString()))
            : receipt;
    }
}

/// <summary>Lists receipts.</summary>
/// <param name="Criteria">What to look for.</param>
/// <param name="Page">Which page to return.</param>
public sealed record SearchReceiptsQuery(
    ReceiptSearchCriteria? Criteria = null,
    PageRequest? Page = null) : IQuery<PagedResult<ReceiptDto>>;

/// <summary>Loads the receipts.</summary>
public sealed class SearchReceiptsQueryHandler
    : IQueryHandler<SearchReceiptsQuery, PagedResult<ReceiptDto>>
{
    private readonly IFinanceReadStore _readStore;

    /// <summary>Initializes the handler.</summary>
    public SearchReceiptsQueryHandler(IFinanceReadStore readStore)
    {
        _readStore = readStore;
    }

    /// <inheritdoc />
    public async Task<Result<PagedResult<ReceiptDto>>> HandleAsync(
        SearchReceiptsQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        PagedResult<ReceiptDto> receipts = await _readStore
            .SearchReceiptsAsync(
                request.Criteria ?? new ReceiptSearchCriteria(),
                request.Page ?? new PageRequest(),
                cancellationToken)
            .ConfigureAwait(false);

        return receipts;
    }
}
