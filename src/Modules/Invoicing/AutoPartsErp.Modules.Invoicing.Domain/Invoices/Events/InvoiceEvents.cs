using AutoPartsErp.Modules.Invoicing.Domain.Series;
using AutoPartsErp.SharedKernel.Primitives;

namespace AutoPartsErp.Modules.Invoicing.Domain.Invoices.Events;

/// <summary>
/// A document was issued and now exists in the world.
/// <para>
/// The most consequential fact this system produces. Finance recognises a receivable, Sales marks
/// the order invoiced, and the AT has to be told about the document within days. Everything that
/// happens next hangs off this event, which is why it carries the totals rather than making
/// consumers ask.
/// </para>
/// </summary>
/// <param name="InvoiceId">The document.</param>
/// <param name="Type">What kind it is.</param>
/// <param name="DocumentNumber">Its number, e.g. <c>FT SERIE2026/35</c>.</param>
/// <param name="Atcud">Its unique code.</param>
/// <param name="CustomerId">Who owes it.</param>
/// <param name="NetTotal">The value before VAT.</param>
/// <param name="VatTotal">The VAT.</param>
/// <param name="GrossTotal">What the customer is asked to pay.</param>
/// <param name="CurrencyCode">Currency of all three.</param>
/// <param name="DocumentDate">The date on the document.</param>
public sealed record InvoiceIssuedDomainEvent(
    InvoiceId InvoiceId,
    DocumentType Type,
    string DocumentNumber,
    string Atcud,
    CustomerRef CustomerId,
    decimal NetTotal,
    decimal VatTotal,
    decimal GrossTotal,
    string CurrencyCode,
    DateOnly DocumentDate) : DomainEvent;

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
/// <param name="GrossTotal">What it was for.</param>
/// <param name="Reason">Why it was voided.</param>
public sealed record InvoiceVoidedDomainEvent(
    InvoiceId InvoiceId,
    DocumentType Type,
    string DocumentNumber,
    CustomerRef CustomerId,
    decimal GrossTotal,
    string Reason) : DomainEvent;
