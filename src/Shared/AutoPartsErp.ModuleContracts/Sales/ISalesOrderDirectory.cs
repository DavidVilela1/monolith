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

    /// <summary>
    /// One customer return as Invoicing needs to see it, or null when there is no such return in
    /// this tenant.
    /// <para>
    /// The same shape of question as the one above, asked about the other direction. Invoicing
    /// draws the credit note; what came back, how much of it, and against which line of which
    /// order are facts Sales owns.
    /// </para>
    /// <para>
    /// Every line comes back, including the scrapped ones. A part written off is still a part the
    /// customer is owed money for — what happened to it afterwards is a fact about a shelf, not
    /// about the debt.
    /// </para>
    /// </summary>
    /// <param name="customerReturnId">The return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<CreditableReturn?> GetCreditableReturnAsync(
        Guid customerReturnId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A customer return, seen from the credit note that is about to be drawn from it.
/// </summary>
/// <param name="CustomerReturnId">The return.</param>
/// <param name="ReturnNumber">Its number, which goes on the credit note as the reference.</param>
/// <param name="SalesOrderId">The order the goods went out on.</param>
/// <param name="CustomerId">Who is owed the money.</param>
/// <param name="CurrencyCode">The currency every figure on it is in.</param>
/// <param name="Status">Draft, Received or Cancelled.</param>
/// <param name="CanCredit">
/// True when a credit note may be drawn: the goods are physically back and the return was not
/// called off. Sales owns that judgement, because Sales owns the return — Invoicing testing
/// <c>Status == "Received"</c> for itself would be Invoicing reimplementing a rule it does not
/// own.
/// </param>
/// <param name="Lines">What is coming back, at the price it went out at.</param>
public sealed record CreditableReturn(
    Guid CustomerReturnId,
    string ReturnNumber,
    Guid SalesOrderId,
    Guid CustomerId,
    string CurrencyCode,
    string Status,
    bool CanCredit,
    IReadOnlyList<CreditableReturnLine> Lines);

/// <summary>
/// One part coming back, as the credit note will bill it.
/// </summary>
/// <param name="SalesOrderLineId">
/// The line of the original order. It is how Invoicing finds the document line to credit: an
/// invoice line carries the order line it charged for, so matching on it is what makes crediting
/// the right line of the right document possible without either module knowing the other's keys.
/// </param>
/// <param name="PartId">The part.</param>
/// <param name="Sku">Its SKU as the order recorded it.</param>
/// <param name="Description">Its description as the order recorded it.</param>
/// <param name="Quantity">How much is coming back.</param>
/// <param name="UnitCode">The unit it was sold in.</param>
public sealed record CreditableReturnLine(
    Guid SalesOrderLineId,
    Guid PartId,
    string Sku,
    string Description,
    decimal Quantity,
    string UnitCode);

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
/// <param name="InvoicingStatus">
/// <c>NotInvoiced</c>, <c>PartiallyInvoiced</c> or <c>Invoiced</c>. Which documents were drawn is
/// deliberately not here: an order can be billed across three invoices as the goods leave in
/// three lorries, and Invoicing is the module that has them.
/// </param>
/// <param name="CanInvoice">
/// True when a document may be drawn: something has gone out, nobody has charged for it yet, and
/// the order was not cancelled. The order does not have to be finished.
/// </param>
/// <param name="Lines">The lines as they will be charged. Empty when there is nothing to bill.</param>
public sealed record BillableOrder(
    Guid SalesOrderId,
    string OrderNumber,
    Guid CustomerId,
    string CurrencyCode,
    string Kind,
    string Status,
    string InvoicingStatus,
    bool CanInvoice,
    IReadOnlyList<BillableOrderLine> Lines);

/// <summary>
/// One line of an order, priced as it will be charged.
/// <para>
/// The quantity is what has gone out and not yet been charged for — dispatched less already
/// invoiced. That was written as the dispatched figure from the first version of this contract
/// precisely so that partial invoicing could arrive as a change to Sales rather than a change to
/// what this field means, and it did.
/// </para>
/// <para>
/// A line with nothing left to bill is not returned at all. An empty <c>Lines</c> therefore means
/// there is nothing to invoice today, which may be because nothing has shipped or because
/// everything that shipped has already been charged for; <c>CanInvoice</c> is the single answer
/// to which.
/// </para>
/// </summary>
/// <param name="LineId">
/// The order line. Copied onto the invoice line, so that issuing the document can tell Sales how
/// much of this line was charged for.
/// </param>
/// <param name="PartId">The part sold.</param>
/// <param name="Sku">Its SKU as the order recorded it.</param>
/// <param name="Description">Its description as the order recorded it.</param>
/// <param name="Quantity">How much has gone out and not yet been charged for.</param>
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
