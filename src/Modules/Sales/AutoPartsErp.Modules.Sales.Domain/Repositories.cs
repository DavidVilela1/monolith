using AutoPartsErp.Modules.Sales.Domain.Customers;
using AutoPartsErp.Modules.Sales.Domain.Orders;
using AutoPartsErp.SharedKernel.Abstractions;

namespace AutoPartsErp.Modules.Sales.Domain;

/// <summary>Write-side access to sales orders.</summary>
public interface ISalesOrderRepository : IRepository<SalesOrder, SalesOrderId>
{
    /// <summary>Loads an order together with its lines, or null when there is no such order.</summary>
    Task<SalesOrder?> GetByNumberAsync(string orderNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes the next order number for the given year, e.g. "SO-2026-01188".
    /// <para>
    /// The number comes from a counter the database increments in one statement, not from reading
    /// the highest one already taken and adding to it. Two people creating an order in the same
    /// moment therefore get two numbers, which was not true before and failed silently when it
    /// was not.
    /// </para>
    /// <para>
    /// The number is spent as soon as it is taken. Nothing wraps creating an order in a
    /// transaction, so an order that fails after this point leaves a gap in the run — untidy for a
    /// commercial document and nothing worse. Invoicing is the one place where a gap is not
    /// allowed, and it pays for that with a lock held across the whole issue.
    /// </para>
    /// </summary>
    /// <param name="year">The year to number within.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<string> NextOrderNumberAsync(int year, CancellationToken cancellationToken = default);

    /// <summary>Every order for a customer that still owes them something.</summary>
    Task<IReadOnlyList<SalesOrder>> GetOpenForCustomerAsync(
        CustomerRef customerId,
        CancellationToken cancellationToken = default);
}

/// <summary>Write-side access to customer accounts.</summary>
public interface ICustomerAccountRepository : IRepository<CustomerAccount, CustomerRef>
{
    /// <summary>Loads an account by code, the way the counter looks one up.</summary>
    Task<CustomerAccount?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);
}

/// <summary>The Sales module's unit of work.</summary>
public interface ISalesUnitOfWork : IUnitOfWork;
