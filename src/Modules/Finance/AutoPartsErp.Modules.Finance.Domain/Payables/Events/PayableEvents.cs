using AutoPartsErp.SharedKernel.Primitives;

namespace AutoPartsErp.Modules.Finance.Domain.Payables.Events;

/// <summary>
/// A supplier's document landed on the purchase ledger and the company owes it.
/// </summary>
/// <param name="PayableItemId">The item.</param>
/// <param name="SupplierId">The supplier.</param>
/// <param name="SupplierCode">Their short code.</param>
/// <param name="DocumentNumber">Their document number.</param>
/// <param name="Kind">Invoice, credit note or debit note.</param>
/// <param name="Amount">The gross total.</param>
/// <param name="CurrencyCode">The currency.</param>
/// <param name="DueDate">When it has to be paid.</param>
public sealed record PayableItemRaisedDomainEvent(
    PayableItemId PayableItemId,
    SupplierRef SupplierId,
    string SupplierCode,
    string DocumentNumber,
    PayableItemKind Kind,
    decimal Amount,
    string CurrencyCode,
    DateOnly DueDate) : DomainEvent;

/// <summary>Nothing is left outstanding on the item.</summary>
/// <param name="PayableItemId">The item.</param>
/// <param name="SupplierId">The supplier.</param>
/// <param name="DocumentNumber">Their document number.</param>
/// <param name="Amount">What it was for.</param>
/// <param name="CurrencyCode">The currency.</param>
public sealed record PayableItemSettledDomainEvent(
    PayableItemId PayableItemId,
    SupplierRef SupplierId,
    string DocumentNumber,
    decimal Amount,
    string CurrencyCode) : DomainEvent;
