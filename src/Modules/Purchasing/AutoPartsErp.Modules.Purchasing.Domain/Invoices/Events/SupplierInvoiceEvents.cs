using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Purchasing.Domain.Invoices.Events;

/// <summary>A draft was opened for one supplier's delivery on one day.</summary>
/// <param name="SupplierInvoiceId">The document.</param>
/// <param name="SupplierId">The supplier.</param>
/// <param name="SupplierCode">Their short code.</param>
/// <param name="ReceivedOn">The day the goods were counted.</param>
/// <param name="RappelRatePercent">The rebate stamped onto it.</param>
public sealed record SupplierInvoiceDraftedDomainEvent(
    SupplierInvoiceId SupplierInvoiceId,
    SupplierRef SupplierId,
    string SupplierCode,
    DateOnly ReceivedOn,
    decimal RappelRatePercent) : DomainEvent;

/// <summary>
/// The supplier's document says something different from what was counted and agreed.
/// <para>
/// Carries both figures rather than only the gap. "We are 12,40 apart" is a number somebody has to
/// go and reconstruct; "they say 1.847,90 and we make it 1.835,50" is one they can act on with the
/// paper in their hand.
/// </para>
/// </summary>
/// <param name="SupplierInvoiceId">The document.</param>
/// <param name="SupplierId">The supplier.</param>
/// <param name="SupplierDocumentNumber">Their document number.</param>
/// <param name="ComputedGrossTotal">What the system worked out from receipts and the agreement.</param>
/// <param name="StatedGrossTotal">What their document says.</param>
/// <param name="Difference">Theirs less ours. Positive means they are charging more.</param>
public sealed record SupplierInvoiceDisputedDomainEvent(
    SupplierInvoiceId SupplierInvoiceId,
    SupplierRef SupplierId,
    string SupplierDocumentNumber,
    Money ComputedGrossTotal,
    Money StatedGrossTotal,
    Money Difference) : DomainEvent;

/// <summary>
/// The document is settled and the company owes the money.
/// <para>
/// Flattened to amounts and a currency code rather than carrying <c>Money</c>, because this is the
/// event that will leave the module: Finance opens a payable from it, and a module boundary is not
/// a place to hand over another module's value objects.
/// </para>
/// </summary>
/// <param name="SupplierInvoiceId">The document.</param>
/// <param name="SupplierId">The supplier.</param>
/// <param name="SupplierCode">Their short code.</param>
/// <param name="SupplierDocumentNumber">Their document number, which is what a payment references.</param>
/// <param name="DocumentDate">The date on their document, from which payment terms run.</param>
/// <param name="NetAmount">What is owed before VAT, after any rebate on the document.</param>
/// <param name="VatAmount">The VAT the company can deduct.</param>
/// <param name="GrossAmount">
/// What will actually be paid, which is the supplier's stated figure and not the computed one. A
/// payable opened for anything else would never match the money leaving the bank.
/// </param>
/// <param name="CurrencyCode">The currency.</param>
public sealed record SupplierInvoiceSettledDomainEvent(
    SupplierInvoiceId SupplierInvoiceId,
    SupplierRef SupplierId,
    string SupplierCode,
    string SupplierDocumentNumber,
    DateOnly DocumentDate,
    decimal NetAmount,
    decimal VatAmount,
    decimal GrossAmount,
    string CurrencyCode) : DomainEvent;

/// <summary>A draft was withdrawn before anything was owed against it.</summary>
/// <param name="SupplierInvoiceId">The document.</param>
/// <param name="SupplierId">The supplier.</param>
/// <param name="Reason">Why.</param>
public sealed record SupplierInvoiceCancelledDomainEvent(
    SupplierInvoiceId SupplierInvoiceId,
    SupplierRef SupplierId,
    string Reason) : DomainEvent;
