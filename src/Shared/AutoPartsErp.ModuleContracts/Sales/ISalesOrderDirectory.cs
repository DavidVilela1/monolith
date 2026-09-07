namespace AutoPartsErp.ModuleContracts.Sales;

/// <summary>
/// What Sales will tell Invoicing about an order that is ready to be billed.
/// <para>
/// The fifth of these contracts, and the first that runs the other way round: the other four are
/// asked by the module that owns a document about parts, stock, partners and prices. This one is
/// Invoicing asking Sales for the thing it is about to turn into a legal document.
/// </para>
/// <para>
/// It deliberately does not expose the order aggregate. What comes back is the billing view — the
/// lines as they will be charged, with the quantity that actually left the building — and a plain
/// answer to "can this be invoiced, and if not, why not". Sales owns that judgement, because
/// Sales owns the order; Invoicing asking "is Status == Dispatched" for itself would be Invoicing
/// reimplementing a rule it does not own.
/// </para>
/// </summary>
public interface ISalesOrderDirectory
{
    /// <summary>
    /// One order as Invoicing needs to see it, or null when no such order exists in this tenant.
    /// </summary>
    /// <param name="salesOrderId">The order.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<BillableOrder?> GetBillableAsync(
        Guid salesOrderId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// An order, seen from the invoice that is about to be drawn from it.
/// </summary>
/// <param name="SalesOrderId">The order.</param>
/// <param name="OrderNumber">Its number, which goes on the document as the customer's reference.</param>
/// <param name="CustomerId">Who it is for. Invoicing resolves their billing identity separately.</param>
/// <param name="CurrencyCode">The currency every figure on it is in.</param>
/// <param name="Kind">
/// <c>CounterSale</c> or <c>Order</c>. It decides the document type: a counter sale is paid as it
/// is handed over, which is an invoice-receipt (FR); an account order is an invoice (FT).
/// </param>
/// <param name="Status">Where the order stands, for a caller that wants to say so.</param>
/// <param name="InvoiceDocumentNumber">
/// The document already drawn from this order, or null when none has been. Present so the refusal
/// can name it rather than merely say no.
/// </param>
/// <param name="CanInvoice">
/// True when a document may be drawn: everything on it has gone out, it was not cancelled, and it
/// has not already been invoiced.
/// </param>
/// <param name="Lines">The lines as they will be charged. Empty when nothing has been dispatched.</param>
public sealed record BillableOrder(
    Guid SalesOrderId,
    string OrderNumber,
    Guid CustomerId,
    string CurrencyCode,
    string Kind,
    string Status,
    string? InvoiceDocumentNumber,
    bool CanInvoice,
    IReadOnlyList<BillableOrderLine> Lines);

/// <summary>
/// One line of an order, priced as it will be charged.
/// <para>
/// The quantity is what was dispatched, not what was ordered. On a fully dispatched order those
/// are the same number, which is the only case this contract admits today — but writing it as the
/// dispatched figure is what makes partial invoicing a change to Sales rather than a change to
/// the meaning of this field.
/// </para>
/// </summary>
/// <param name="LineId">The order line, kept so a future partial invoice can point back at it.</param>
/// <param name="PartId">The part sold.</param>
/// <param name="Sku">Its SKU as the order recorded it.</param>
/// <param name="Description">Its description as the order recorded it.</param>
/// <param name="Quantity">How much went out.</param>
/// <param name="UnitCode">The unit it was sold in.</param>
/// <param name="UnitPrice">The price per unit, before discount.</param>
/// <param name="DiscountPercent">The discount given.</param>
/// <param name="VatRatePercent">
/// The rate the order recorded. A percentage rather than a category, because a percentage is all
/// an order needs — turning it into the category a document must declare is Invoicing's job, and
/// it needs the tax region to do it.
/// </param>
public sealed record BillableOrderLine(
    Guid LineId,
    Guid PartId,
    string Sku,
    string Description,
    decimal Quantity,
    string UnitCode,
    decimal UnitPrice,
    decimal DiscountPercent,
    decimal VatRatePercent);
