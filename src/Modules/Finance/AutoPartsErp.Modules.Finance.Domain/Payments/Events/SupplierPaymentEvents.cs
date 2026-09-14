using AutoPartsErp.SharedKernel.Primitives;

namespace AutoPartsErp.Modules.Finance.Domain.Payments.Events;

/// <summary>Money left the company.</summary>
/// <param name="SupplierPaymentId">The payment.</param>
/// <param name="Number">Our number for it.</param>
/// <param name="SupplierId">Who was paid.</param>
/// <param name="SupplierCode">Their short code.</param>
/// <param name="Amount">How much.</param>
/// <param name="CurrencyCode">The currency.</param>
/// <param name="PaidOn">The day it left.</param>
/// <param name="Method">How it left.</param>
public sealed record SupplierPaymentRecordedDomainEvent(
    SupplierPaymentId SupplierPaymentId,
    string Number,
    SupplierRef SupplierId,
    string SupplierCode,
    decimal Amount,
    string CurrencyCode,
    DateOnly PaidOn,
    PaymentMethod Method) : DomainEvent;

/// <summary>
/// Every euro of a payment is now matched to a document.
/// <para>
/// Worth announcing on its own because the opposite state is the one that needs chasing: money
/// that left the bank and sits on a supplier's account against nothing is what makes a statement
/// disagree, and this is the fact that says one more of those is gone.
/// </para>
/// </summary>
/// <param name="SupplierPaymentId">The payment.</param>
/// <param name="Number">Our number for it.</param>
/// <param name="SupplierId">Who was paid.</param>
/// <param name="Amount">How much it was for.</param>
/// <param name="CurrencyCode">The currency.</param>
public sealed record SupplierPaymentFullyAllocatedDomainEvent(
    SupplierPaymentId SupplierPaymentId,
    string Number,
    SupplierRef SupplierId,
    decimal Amount,
    string CurrencyCode) : DomainEvent;
