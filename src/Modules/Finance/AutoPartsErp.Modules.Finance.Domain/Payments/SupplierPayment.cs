using AutoPartsErp.Modules.Finance.Domain.Payments.Events;
using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Finance.Domain.Payments;

/// <summary>How the money left.</summary>
public enum PaymentMethod
{
    /// <summary>Not set.</summary>
    Unknown = 0,

    /// <summary>Cash out of the till.</summary>
    Cash = 1,

    /// <summary>Bank transfer.</summary>
    BankTransfer = 2,

    /// <summary>Direct debit the supplier collected.</summary>
    DirectDebit = 3,

    /// <summary>Cheque.</summary>
    Cheque = 4,

    /// <summary>Card.</summary>
    Card = 5,
}

/// <summary>How much of a payment has been matched to documents.</summary>
public enum PaymentStatus
{
    /// <summary>Not set.</summary>
    Unknown = 0,

    /// <summary>The money has gone out and nothing has been matched to it yet.</summary>
    Unallocated = 1,

    /// <summary>Some of it is matched, some is not.</summary>
    PartiallyAllocated = 2,

    /// <summary>All of it is matched.</summary>
    Allocated = 3,
}

/// <summary>
/// Money that left the company, and which of the supplier's documents it paid.
/// <para>
/// The mirror of <see cref="Receipts.Receipt"/>, and it carries the same deliberate looseness: a
/// payment can be recorded before anybody has worked out what it settles. A transfer goes out on
/// Friday against a statement, and which of eleven invoices it covered is a question somebody
/// answers on Monday with the remittance in front of them. Forcing the allocation at the moment
/// the money moves would mean either guessing or not recording the payment, and the bank balance
/// would be wrong in the meantime.
/// </para>
/// <para>
/// <see cref="Allocate"/> is internal, like the payable's own half. Settling is one fact about two
/// aggregates, and <see cref="Payables.PayableSettlement"/> does both halves or neither.
/// </para>
/// </summary>
public sealed class SupplierPayment : AggregateRoot<SupplierPaymentId>, ITenantScoped, IAuditable
{
    /// <summary>Longest permitted payment number.</summary>
    public const int MaxNumberLength = 30;

    /// <summary>Longest permitted bank reference.</summary>
    public const int MaxReferenceLength = 120;

    /// <summary>Longest permitted note.</summary>
    public const int MaxNotesLength = 500;

    private readonly List<SupplierPaymentAllocation> _allocations = [];

    private SupplierPayment(
        SupplierPaymentId id,
        string number,
        SupplierRef supplierId,
        string supplierCode,
        Money amount,
        DateOnly paidOn,
        PaymentMethod method,
        string? reference,
        string? notes)
        : base(id)
    {
        Number = number;
        SupplierId = supplierId;
        SupplierCode = supplierCode;
        Amount = amount;
        AllocatedAmount = Money.Zero(amount.Currency);
        PaidOn = paidOn;
        Method = method;
        Reference = reference;
        Notes = notes;
        Status = PaymentStatus.Unallocated;
        CreatedBy = string.Empty;
    }

    /// <summary>Required by EF Core materialization.</summary>
#pragma warning disable CS8618
    private SupplierPayment()
    {
    }
#pragma warning restore CS8618

    /// <summary>Our own number for the payment, from the module's counter.</summary>
    public string Number { get; private set; } = string.Empty;

    /// <summary>Who was paid.</summary>
    public SupplierRef SupplierId { get; private set; }

    /// <summary>Their short code, snapshotted so a screen need not cross the module boundary.</summary>
    public string SupplierCode { get; private set; } = string.Empty;

    /// <summary>How much left.</summary>
    public Money Amount { get; private set; } = null!;

    /// <summary>How much of it has been matched to documents.</summary>
    public Money AllocatedAmount { get; private set; } = null!;

    /// <summary>The day the money left.</summary>
    public DateOnly PaidOn { get; private set; }

    /// <summary>How it left.</summary>
    public PaymentMethod Method { get; private set; }

    /// <summary>The bank reference or cheque number, when there is one.</summary>
    public string? Reference { get; private set; }

    /// <summary>Anything else worth recording.</summary>
    public string? Notes { get; private set; }

    /// <summary>How much of it has been matched.</summary>
    public PaymentStatus Status { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <inheritdoc />
    public string CreatedBy { get; set; } = string.Empty;

    /// <inheritdoc />
    public DateTimeOffset? ModifiedAtUtc { get; set; }

    /// <inheritdoc />
    public string? ModifiedBy { get; set; }

    /// <summary>Which documents it paid, and how much of each.</summary>
    public IReadOnlyList<SupplierPaymentAllocation> Allocations => _allocations;

    /// <summary>What is still sitting on the supplier's account unmatched.</summary>
    public Money Unallocated => Amount - AllocatedAmount;

    /// <summary>The currency it is in.</summary>
    public Currency Currency => Amount.Currency;

    /// <summary>Records money that has left.</summary>
    /// <param name="number">The payment number, taken from the module's counter.</param>
    /// <param name="supplierId">Who was paid.</param>
    /// <param name="supplierCode">Their short code.</param>
    /// <param name="amount">How much. Positive.</param>
    /// <param name="paidOn">The day it left.</param>
    /// <param name="method">How it left.</param>
    /// <param name="reference">The bank reference or cheque number, when there is one.</param>
    /// <param name="notes">Anything else worth recording.</param>
    public static Result<SupplierPayment> Record(
        string? number,
        SupplierRef supplierId,
        string? supplierCode,
        Money amount,
        DateOnly paidOn,
        PaymentMethod method,
        string? reference = null,
        string? notes = null)
    {
        ArgumentNullException.ThrowIfNull(amount);

        if (string.IsNullOrWhiteSpace(number))
        {
            return FinanceErrors.Payment.NumberRequired;
        }

        if (supplierId.IsEmpty)
        {
            return FinanceErrors.Payable.SupplierRequired;
        }

        if (string.IsNullOrWhiteSpace(supplierCode))
        {
            return FinanceErrors.Payable.SupplierCodeRequired;
        }

        if (!amount.IsPositive)
        {
            return FinanceErrors.Payment.AmountNotPositive;
        }

        if (method == PaymentMethod.Unknown)
        {
            return FinanceErrors.Payment.MethodRequired;
        }

        return new SupplierPayment(
            SupplierPaymentId.New(),
            Clip(number, MaxNumberLength)!,
            supplierId,
            supplierCode.Trim().ToUpperInvariant(),
            amount,
            paidOn,
            method,
            Clip(reference, MaxReferenceLength),
            Clip(notes, MaxNotesLength));
    }

    /// <summary>
    /// Records that part of this payment settled a document.
    /// <para>
    /// Internal for the same reason <see cref="Payables.PayableItem.Settle"/> is: half a match is
    /// not a thing worth being able to write.
    /// </para>
    /// </summary>
    /// <param name="payableItemId">The item being paid.</param>
    /// <param name="documentNumber">Its number, copied so a remittance reads without a join.</param>
    /// <param name="amount">How much of this payment goes against it.</param>
    /// <param name="allocatedOn">The date the match was made.</param>
    internal Result<SupplierPaymentAllocationId> Allocate(
        PayableItemId payableItemId,
        string documentNumber,
        Money amount,
        DateOnly allocatedOn)
    {
        ArgumentNullException.ThrowIfNull(amount);

        if (amount.Currency != Currency)
        {
            return FinanceErrors.Payment.CurrencyMismatch;
        }

        if (!amount.IsPositive)
        {
            return FinanceErrors.Payment.AllocationNotPositive;
        }

        if (amount > Unallocated)
        {
            return FinanceErrors.Payment.ExceedsUnallocated(Number, Unallocated.Amount);
        }

        SupplierPaymentAllocation allocation = SupplierPaymentAllocation.Create(
            payableItemId, documentNumber, amount, allocatedOn);

        _allocations.Add(allocation);
        AllocatedAmount += amount;

        Status = Unallocated.IsZero
            ? PaymentStatus.Allocated
            : PaymentStatus.PartiallyAllocated;

        if (Status == PaymentStatus.Allocated)
        {
            Raise(new SupplierPaymentFullyAllocatedDomainEvent(
                Id, Number, SupplierId, Amount.Amount, Currency.Code));
        }

        return allocation.Id;
    }

    /// <summary>Raises the event that says the money left. Called once, after numbering.</summary>
    public void Announce() =>
        Raise(new SupplierPaymentRecordedDomainEvent(
            Id, Number, SupplierId, SupplierCode, Amount.Amount, Currency.Code, PaidOn, Method));

    private static string? Clip(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string trimmed = value.Trim();

        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
