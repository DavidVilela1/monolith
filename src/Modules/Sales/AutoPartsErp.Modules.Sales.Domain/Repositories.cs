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
    /// Reserves the next order number for the given year, e.g. "SO-2026-01188".
    /// <para>
    /// Same max-plus-one as Purchasing, and the same caveat: it will collide under genuine
    /// concurrency, and a sequence table is the real answer. Sales needs that answer sooner,
    /// because a Portuguese invoice number has to be gapless and sequential by law — which is
    /// the ATCUD work, and is why this is deliberately not being papered over here.
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
