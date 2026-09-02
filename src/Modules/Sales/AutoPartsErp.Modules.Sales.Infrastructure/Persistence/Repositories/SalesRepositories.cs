using System.Globalization;
using AutoPartsErp.Modules.Sales.Domain;
using AutoPartsErp.Modules.Sales.Domain.Customers;
using AutoPartsErp.Modules.Sales.Domain.Orders;
using AutoPartsErp.SharedKernel.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace AutoPartsErp.Modules.Sales.Infrastructure.Persistence.Repositories;

/// <summary>Write-side access to sales orders. Lines are owned, so they load with their order.</summary>
public sealed class SalesOrderRepository : ISalesOrderRepository
{
    private const string NumberPrefix = "SO";

    private readonly SalesDbContext _context;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initializes the repository.</summary>
    public SalesOrderRepository(SalesDbContext context, ITenantContext tenantContext)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public Task<SalesOrder?> GetByIdAsync(SalesOrderId id, CancellationToken cancellationToken = default) =>
        _context.SalesOrders.FirstOrDefaultAsync(order => order.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsAsync(SalesOrderId id, CancellationToken cancellationToken = default) =>
        _context.SalesOrders.AnyAsync(order => order.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<SalesOrder?> GetByNumberAsync(
        string orderNumber,
        CancellationToken cancellationToken = default)
    {
        string normalized = orderNumber?.Trim().ToUpperInvariant() ?? string.Empty;

        return _context.SalesOrders.FirstOrDefaultAsync(
            order => order.OrderNumber == normalized, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<string> NextOrderNumberAsync(int year, CancellationToken cancellationToken = default)
    {
        string prefix = string.Create(CultureInfo.InvariantCulture, $"{NumberPrefix}-{year}-");

        // Query filters ignored and the tenant applied by hand: a soft-deleted order still owns
        // its number. Numbers are zero-padded, so ordering as text orders numerically and this
        // reads one row rather than every order taken this year.
        string? last = await _context.SalesOrders
            .IgnoreQueryFilters()
            .Where(order => order.TenantId == _tenantContext.TenantId)
            .Where(order => EF.Functions.Like(order.OrderNumber, prefix + "%"))
            .OrderByDescending(order => order.OrderNumber)
            .Select(order => order.OrderNumber)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        int next = 1;

        if (last is not null && last.Length > prefix.Length)
        {
            string suffix = last[prefix.Length..];
            if (int.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed))
            {
                next = parsed + 1;
            }
        }

        return string.Create(CultureInfo.InvariantCulture, $"{prefix}{next:D5}");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SalesOrder>> GetOpenForCustomerAsync(
        CustomerRef customerId,
        CancellationToken cancellationToken = default)
    {
        List<SalesOrder> orders = await _context.SalesOrders
            .Where(order => order.CustomerId == customerId)
            .Where(order => order.Status == SalesOrderStatus.Confirmed
                || order.Status == SalesOrderStatus.PartiallyDispatched)
            .OrderBy(order => order.OrderNumber)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return orders;
    }

    /// <inheritdoc />
    public void Add(SalesOrder aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.SalesOrders.Add(aggregate);
    }

    /// <inheritdoc />
    public void Remove(SalesOrder aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.SalesOrders.Remove(aggregate);
    }
}

/// <summary>Write-side access to customer accounts.</summary>
public sealed class CustomerAccountRepository : ICustomerAccountRepository
{
    private readonly SalesDbContext _context;

    /// <summary>Initializes the repository.</summary>
    public CustomerAccountRepository(SalesDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public Task<CustomerAccount?> GetByIdAsync(
        CustomerRef id,
        CancellationToken cancellationToken = default) =>
        _context.CustomerAccounts.FirstOrDefaultAsync(account => account.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsAsync(CustomerRef id, CancellationToken cancellationToken = default) =>
        _context.CustomerAccounts.AnyAsync(account => account.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<CustomerAccount?> GetByCodeAsync(
        string code,
        CancellationToken cancellationToken = default)
    {
        string normalized = code?.Trim().ToUpperInvariant() ?? string.Empty;

        return _context.CustomerAccounts.FirstOrDefaultAsync(
            account => account.Code == normalized, cancellationToken);
    }

    /// <inheritdoc />
    public void Add(CustomerAccount aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.CustomerAccounts.Add(aggregate);
    }

    /// <inheritdoc />
    public void Remove(CustomerAccount aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.CustomerAccounts.Remove(aggregate);
    }
}
