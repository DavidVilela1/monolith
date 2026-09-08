using AutoPartsErp.SharedKernel.Messaging;

namespace AutoPartsErp.IntegrationEvents.Invoicing;

/// <summary>
/// A document was issued and now exists in the world.
/// <para>
/// The most consequential fact this system produces. Finance recognises a receivable, Sales marks
/// the order invoiced, and the tax authority has to be told about the document within days.
/// Carries the totals so no consumer has to ask.
/// </para>
/// </summary>
/// <param name="InvoiceId">The document.</param>
/// <param name="Type">FT, FS, FR, NC or ND.</param>
/// <param name="DocumentNumber">Its number, e.g. <c>FT SERIE2026/35</c>.</param>
/// <param name="Atcud">Its unique code.</param>
/// <param name="CustomerId">Who owes it.</param>
/// <param name="SalesOrderId">The order it was raised against, when there was one.</param>
/// <param name="NetTotal">The value before VAT.</param>
/// <param name="VatTotal">The VAT.</param>
/// <param name="GrossTotal">What the customer is asked to pay.</param>
/// <param name="CurrencyCode">Currency of all three.</param>
/// <param name="DocumentDate">The date on the document.</param>
/// <param name="TenantId">The owning tenant.</param>
public sealed record InvoiceIssuedIntegrationEvent(
    Guid InvoiceId,
    string Type,
    string DocumentNumber,
    string Atcud,
    Guid CustomerId,
    Guid? SalesOrderId,
    decimal NetTotal,
    decimal VatTotal,
    decimal GrossTotal,
    string CurrencyCode,
    DateOnly DocumentDate,
    Guid TenantId) : IntegrationEvent;

/// <summary>
/// A document was voided.
/// <para>
/// Carries the gross total so a consumer can reverse whatever it did on issue without loading the
/// document back. It is the same figure as before — voiding changes a status, never a number.
/// </para>
/// </summary>
/// <param name="InvoiceId">The document.</param>
/// <param name="Type">What kind it is.</param>
/// <param name="DocumentNumber">Its number, which it keeps.</param>
/// <param name="CustomerId">Who it was addressed to.</param>
/// <param name="SalesOrderId">
/// The order it was raised against, when there was one. Carried for the same reason the issued
/// event carries it, and needed more urgently: without it Sales cannot tell which of its orders
/// has just become billable again.
/// </param>
/// <param name="GrossTotal">What it was for.</param>
/// <param name="Reason">Why it was voided.</param>
/// <param name="TenantId">The owning tenant.</param>
public sealed record InvoiceVoidedIntegrationEvent(
    Guid InvoiceId,
    string Type,
    string DocumentNumber,
    Guid CustomerId,
    Guid? SalesOrderId,
    decimal GrossTotal,
    string Reason,
    Guid TenantId) : IntegrationEvent;

/// <summary>
/// One line of a sales order that a document charged for.
/// </summary>
/// <param name="SalesOrderLineId">The line in Sales.</param>
/// <param name="Quantity">How much of it was charged for.</param>
/// <param name="UnitCode">The unit that quantity is in.</param>
public sealed record BilledOrderLine(Guid SalesOrderLineId, decimal Quantity, string UnitCode);

/// <summary>
/// A document charged for part or all of a sales order.
/// <para>
/// Separate from <see cref="InvoiceIssuedIntegrationEvent"/> rather than folded into it, and it
/// has to be: the issued event says a document exists and is read by Finance, which does not care
/// which order lines are behind it. This one says which of an order's lines were charged for and
/// how much of each, which is exactly what Sales needs and what nothing else does. Putting the
/// lines on the issued event would send an order's internals to every consumer of every document,
/// including the counter sales that have no order at all.
/// </para>
/// <para>
/// Published on issue, never on draft. A draft can be abandoned, and an order whose lines counted
/// abandoned drafts against themselves would become unbillable without anybody ever having been
/// charged.
/// </para>
/// </summary>
/// <param name="SalesOrderId">The order that was charged for.</param>
/// <param name="InvoiceId">The document that charged for it.</param>
/// <param name="DocumentNumber">Its number, as printed.</param>
/// <param name="DocumentDate">The date on it.</param>
/// <param name="Lines">Which of the order's lines it charged for, and how much of each.</param>
/// <param name="TenantId">The owning tenant.</param>
public sealed record SalesOrderBilledIntegrationEvent(
    Guid SalesOrderId,
    Guid InvoiceId,
    string DocumentNumber,
    DateOnly DocumentDate,
    IReadOnlyList<BilledOrderLine> Lines,
    Guid TenantId) : IntegrationEvent;

/// <summary>
/// A document that had charged for part of a sales order was voided.
/// <para>
/// What it charged for becomes billable again. A voided document keeps its number and its place
/// in the chain forever, but it bills nobody — so the goods it covered are once more goods that
/// have gone out and not been paid for, which is the only honest state for them to be in.
/// </para>
/// </summary>
/// <param name="SalesOrderId">The order.</param>
/// <param name="InvoiceId">The document that was voided.</param>
/// <param name="DocumentNumber">Its number, which it keeps.</param>
/// <param name="Lines">What it had charged for.</param>
/// <param name="TenantId">The owning tenant.</param>
public sealed record SalesOrderBillingReversedIntegrationEvent(
    Guid SalesOrderId,
    Guid InvoiceId,
    string DocumentNumber,
    IReadOnlyList<BilledOrderLine> Lines,
    Guid TenantId) : IntegrationEvent;
