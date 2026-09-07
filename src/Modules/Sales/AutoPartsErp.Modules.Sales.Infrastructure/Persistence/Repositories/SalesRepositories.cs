using System.Globalization;
using AutoPartsErp.Modules.Sales.Domain;
using AutoPartsErp.Modules.Sales.Domain.Customers;
using AutoPartsErp.Modules.Sales.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace AutoPartsErp.Modules.Sales.Infrastructure.Persistence.Repositories;

/// <summary>Write-side access to sales orders. Lines are owned, so they load with their order.</summary>
public sealed class SalesOrderRepository : ISalesOrderRepository
{
    private const string NumberPrefix = "SO";

    /// <summary>Identifies this run of numbers in the module's counter table.</summary>
    private const string NumberKey = "sales-order";

    private readonly SalesDbContext _context;

    /// <summary>Initializes the repository.</summary>
    public SalesOrderRepository(SalesDbContext context)
    {
        _context = context;
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
        // This used to read the highest number already taken and add one. Two people creating an
        // order in the same moment both read the same highest number, both got the same next one,
        // and nothing anywhere complained - the number was only unique because of the order the
        // reads happened to fall in, and under load they stop falling in that order.
        //
        // The counter increments in the database, in one statement, so the second caller cannot
        // read what the first one has not yet written.
        int next = await _context
            .TakeNextNumberAsync(NumberKey, year, cancellationToken)
            .ConfigureAwait(false);

        return string.Create(CultureInfo.InvariantCulture, $"{NumberPrefix}-{year}-{next:D5}");
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
