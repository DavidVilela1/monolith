using AutoPartsErp.ModuleContracts.Sales;
using AutoPartsErp.Modules.Sales.Domain;
using AutoPartsErp.Modules.Sales.Domain.Orders;
using AutoPartsErp.Modules.Sales.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AutoPartsErp.Modules.Sales.Infrastructure.Contracts;

/// <summary>
/// Sales' answer to "is this order ready to invoice, and what is on it?".
/// <para>
/// The whole aggregate rather than a projection, and for the same reason
/// <c>PartnerDirectory</c> loads a whole partner: <see cref="SalesOrder.CanInvoice"/> is a rule
/// that lives on the aggregate, and reimplementing "dispatched and not yet invoiced" in an
/// adapter is how Sales and Invoicing start disagreeing about whether something has been billed.
/// </para>
/// </summary>
public sealed class SalesOrderDirectory : ISalesOrderDirectory
{
    private readonly SalesDbContext _context;

    /// <summary>Initializes the adapter.</summary>
    public SalesOrderDirectory(SalesDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<BillableOrder?> GetBillableAsync(
        Guid salesOrderId,
        CancellationToken cancellationToken = default)
    {
        var id = new SalesOrderId(salesOrderId);

        SalesOrder? order = await _context.SalesOrders
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (order is null)
        {
            return null;
        }

        // Only the lines that have something dispatched. On a fully dispatched order — the only
        // kind CanInvoice admits today — that is all of them, so this filter does nothing yet. It
        // is here because the field it feeds is documented as the dispatched quantity, and a
        // filter that is currently a no-op is cheaper than a contract that quietly means
        // something else the day partial invoicing arrives.
        List<BillableOrderLine> lines =
        [
            .. order.Lines
                .Where(line => line.DispatchedQuantity.Value > 0m)
                .Select(line => new BillableOrderLine(
                    line.Id.Value,
                    line.PartId.Value,
                    line.Sku,
                    line.Description,
                    line.DispatchedQuantity.Value,
                    line.DispatchedQuantity.Unit.Code,
                    line.UnitPrice.Amount,
                    line.DiscountPercent,
                    line.VatRatePercent)),
        ];

        return new BillableOrder(
            order.Id.Value,
            order.OrderNumber,
            order.CustomerId.Value,
            order.CurrencyCode,
            order.Kind.ToString(),
            order.Status.ToString(),
            order.InvoiceDocumentNumber,
            order.CanInvoice,
            lines);
    }
}
