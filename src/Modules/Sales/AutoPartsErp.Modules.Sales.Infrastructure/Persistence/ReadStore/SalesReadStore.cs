using AutoPartsErp.Modules.Sales.Application.Abstractions;
using AutoPartsErp.Modules.Sales.Application.Contracts;
using AutoPartsErp.Modules.Sales.Domain;
using AutoPartsErp.Modules.Sales.Domain.Customers;
using AutoPartsErp.Modules.Sales.Domain.Orders;
using AutoPartsErp.SharedKernel.Abstractions;
using AutoPartsErp.SharedKernel.Paging;
using Microsoft.EntityFrameworkCore;

namespace AutoPartsErp.Modules.Sales.Infrastructure.Persistence.ReadStore;

/// <summary>Serves the Sales module's queries.</summary>
public sealed class SalesReadStore : ISalesReadStore
{
    /// <summary>Statuses in which an order still owes the customer something.</summary>
    private static readonly SalesOrderStatus[] OpenStatuses =
    [
        SalesOrderStatus.Confirmed,
        SalesOrderStatus.PartiallyDispatched,
    ];

    private readonly SalesDbContext _context;
    private readonly IDateTimeProvider _clock;

    /// <summary>Initializes the read store.</summary>
    public SalesReadStore(SalesDbContext context, IDateTimeProvider clock)
    {
        _context = context;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<SalesOrderDetail?> GetOrderAsync(
        Guid salesOrderId,
        CancellationToken cancellationToken = default)
    {
        var id = new SalesOrderId(salesOrderId);

        SalesOrder? order = await _context.SalesOrders
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken)
            .ConfigureAwait(false);

        return order is null ? null : MapDetail(order);
    }

    /// <inheritdoc />
    public async Task<SalesOrderDetail?> GetOrderByNumberAsync(
        string orderNumber,
        CancellationToken cancellationToken = default)
    {
        string normalized = orderNumber?.Trim().ToUpperInvariant() ?? string.Empty;

        SalesOrder? order = await _context.SalesOrders
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.OrderNumber == normalized, cancellationToken)
            .ConfigureAwait(false);

        return order is null ? null : MapDetail(order);
    }

    /// <inheritdoc />
    public async Task<PagedResult<SalesOrderSummary>> SearchOrdersAsync(
        SalesOrderSearchCriteria criteria,
        PageRequest page,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        ArgumentNullException.ThrowIfNull(page);

        DateOnly today = _clock.TodayUtc;

        IQueryable<SalesOrder> query = _context.SalesOrders.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(criteria.Term))
        {
            string term = criteria.Term.Trim();
            string upper = term.ToUpperInvariant();

            query = query.Where(order =>
                EF.Functions.Like(order.OrderNumber, $"%{upper}%")
                || EF.Functions.Like(order.CustomerCode, $"{upper}%")
                || EF.Functions.ILike(order.CustomerName, $"%{term}%")
                || (order.CustomerReference != null
                    && EF.Functions.ILike(order.CustomerReference, $"%{term}%")));
        }

        if (criteria.CustomerId is { } customerId)
        {
            var customer = new CustomerRef(customerId);
            query = query.Where(order => order.CustomerId == customer);
        }

        if (criteria.WarehouseId is { } warehouseId)
        {
            var warehouse = new WarehouseRef(warehouseId);
            query = query.Where(order => order.FromWarehouseId == warehouse);
        }

        if (!string.IsNullOrWhiteSpace(criteria.Status)
            && Enum.TryParse(criteria.Status, ignoreCase: true, out SalesOrderStatus status))
        {
            query = query.Where(order => order.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(criteria.Kind)
            && Enum.TryParse(criteria.Kind, ignoreCase: true, out SalesOrderKind kind)
            && kind != SalesOrderKind.Unknown)
        {
            query = query.Where(order => order.Kind == kind);
        }

        // "Still owed" is a question about status, not arithmetic across lines - which keeps
        // this an index seek rather than a subquery over every line in the table.
        if (criteria.OutstandingOnly || criteria.LateOnly)
        {
            query = query.Where(order => OpenStatuses.Contains(order.Status));
        }

        if (criteria.LateOnly)
        {
            query = query.Where(order => order.RequiredBy != null && order.RequiredBy < today);
        }

        int total = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        if (total == 0)
        {
            return PagedResult<SalesOrderSummary>.Empty(page.Page, page.PageSize);
        }

        List<SalesOrder> rows = await query
            .OrderByDescending(order => order.OrderNumber)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<SalesOrderSummary> items = [.. rows.Select(order => MapSummary(order, today))];

        return PagedResult<SalesOrderSummary>.Create(items, page.Page, page.PageSize, total);
    }

    /// <inheritdoc />
    public async Task<CustomerAccountDto?> GetCustomerAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        var id = new CustomerRef(customerId);

        CustomerAccount? account = await _context.CustomerAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken)
            .ConfigureAwait(false);

        return account is null ? null : MapCustomer(account);
    }

    /// <inheritdoc />
    public async Task<CustomerAccountDto?> GetCustomerByCodeAsync(
        string code,
        CancellationToken cancellationToken = default)
    {
        string normalized = code?.Trim().ToUpperInvariant() ?? string.Empty;

        CustomerAccount? account = await _context.CustomerAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Code == normalized, cancellationToken)
            .ConfigureAwait(false);

        return account is null ? null : MapCustomer(account);
    }

    /// <inheritdoc />
    public async Task<PagedResult<CustomerAccountDto>> SearchCustomersAsync(
        string? term,
        string? status,
        PageRequest page,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);

        IQueryable<CustomerAccount> query = _context.CustomerAccounts.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(term))
        {
            string trimmed = term.Trim();
            string upper = trimmed.ToUpperInvariant();

            query = query.Where(account =>
                EF.Functions.Like(account.Code, $"{upper}%")
                || EF.Functions.ILike(account.LegalName, $"%{trimmed}%"));
        }

        if (!string.IsNullOrWhiteSpace(status)
            && Enum.TryParse(status, ignoreCase: true, out CustomerStatus parsed)
            && parsed != CustomerStatus.Unknown)
        {
            query = query.Where(account => account.Status == parsed);
        }

        int total = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        if (total == 0)
        {
            return PagedResult<CustomerAccountDto>.Empty(page.Page, page.PageSize);
        }

        List<CustomerAccount> rows = await query
            .OrderBy(account => account.Code)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<CustomerAccountDto> items = [.. rows.Select(MapCustomer)];

        return PagedResult<CustomerAccountDto>.Create(items, page.Page, page.PageSize, total);
    }

    private static CustomerAccountDto MapCustomer(CustomerAccount account) => new(
        account.Id.Value,
        account.Code,
        account.LegalName,
        account.Status.ToString(),
        account.HoldReason,
        account.CreditLimit.Amount,
        account.Committed.Amount,
        account.AvailableCredit.Amount,
        account.Currency.Code,
        account.PaymentDueInDays,
        account.PaymentEndOfMonth,
        account.PriceListCode,
        account.CanTakeOrders,
        account.IsCashOnly);

    private static SalesOrderSummary MapSummary(SalesOrder order, DateOnly today) => new(
        order.Id.Value,
        order.OrderNumber,
        order.Kind.ToString(),
        order.CustomerId.Value,
        order.CustomerCode,
        order.CustomerName,
        order.Status.ToString(),
        order.ConfirmedOn,
        order.RequiredBy,
        order.NetTotal.Amount,
        order.VatTotal.Amount,
        order.GrossTotal.Amount,
        order.CurrencyCode,
        order.Lines.Count,
        order.RequiredBy is { } required && required < today && order.HasOutstandingLines && !order.IsClosed);

    private static SalesOrderDetail MapDetail(SalesOrder order) => new()
    {
        Id = order.Id.Value,
        OrderNumber = order.OrderNumber,
        Kind = order.Kind.ToString(),
        CustomerId = order.CustomerId.Value,
        CustomerCode = order.CustomerCode,
        CustomerName = order.CustomerName,
        FromWarehouseId = order.FromWarehouseId.Value,
        Status = order.Status.ToString(),
        CurrencyCode = order.CurrencyCode,
        ConfirmedOn = order.ConfirmedOn,
        RequiredBy = order.RequiredBy,
        CustomerReference = order.CustomerReference,
        Notes = order.Notes,
        ClosureReason = order.ClosureReason,
        NetTotal = order.NetTotal.Amount,
        VatTotal = order.VatTotal.Amount,
        GrossTotal = order.GrossTotal.Amount,
        IsEditable = order.IsEditable,
        CanDispatch = order.CanDispatch,
        Lines = [.. order.Lines.Select(line => new SalesOrderLineDto(
            line.Id.Value,
            line.PartId.Value,
            line.Sku,
            line.Description,
            line.Quantity.Value,
            line.DispatchedQuantity.Value,
            line.OutstandingQuantity.Value,
            line.Quantity.Unit.Code,
            line.UnitPrice.Amount,
            line.DiscountPercent,
            line.NetTotal.Amount,
            line.VatRatePercent,
            line.VatAmount.Amount,
            line.GrossTotal.Amount,
            line.IsFullyDispatched,
            line.PriceSource))],
    };
}
