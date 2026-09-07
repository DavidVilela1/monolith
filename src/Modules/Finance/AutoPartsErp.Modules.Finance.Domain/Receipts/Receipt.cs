using AutoPartsErp.Modules.Finance.Domain.Receipts.Events;
using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Finance.Domain.Receipts;

/// <summary>How the money arrived.</summary>
public enum ReceiptMethod
{
    /// <summary>Not set.</summary>
    Unknown = 0,

    /// <summary>Notes and coins over the counter.</summary>
    Cash = 1,

    /// <summary>A transfer into the company's bank account.</summary>
    BankTransfer = 2,

    /// <summary>A cheque. Recorded on receipt, which is before it clears.</summary>
    Cheque = 3,

    /// <summary>A card at the counter or online.</summary>
    Card = 4,

    /// <summary>Collected by direct debit.</summary>
    DirectDebit = 5,

    /// <summary>Anything else, described in the reference.</summary>
    Other = 6,
}

/// <summary>Where a receipt stands against the documents it was meant to pay.</summary>
public enum ReceiptStatus
{
    /// <summary>Not set.</summary>
    Unknown = 0,

    /// <summary>Recorded, and none of it matched to a document yet.</summary>
    Unallocated = 1,

    /// <summary>Some of it matched; the rest is sitting on the account.</summary>
    PartiallyAllocated = 2,

    /// <summary>All of it matched.</summary>
    Allocated = 3,
}

/// <summary>
/// Money received from a customer, and the record of which documents it paid.
/// <para>
/// Recording the money and deciding what it pays are two different acts, and this aggregate keeps
/// them apart on purpose. A transfer lands in the bank with a reference nobody can read; it is
/// real money on a real date and the account balance should reflect it immediately, whether or
/// not anybody has worked out which of the eleven open invoices it was for. Until they do, it
/// sits here unallocated — which is the honest state and the one a customer's own ledger will
/// show too.
/// </para>
/// <para>
/// A receipt is never negative and never refunds. Money going the other way is a different
/// document with different paperwork behind it, and modelling it as a receipt with a minus sign
/// is how a sales ledger stops adding up.
/// </para>
/// </summary>
public sealed class Receipt : AggregateRoot<ReceiptId>, ITenantScoped, IAuditable
{
    /// <summary>Longest receipt number.</summary>
    public const int MaxNumberLength = 30;

    /// <summary>Longest bank or cheque reference kept.</summary>
    public const int MaxReferenceLength = 100;

    /// <summary>Longest note kept.</summary>
    public const int MaxNotesLength = 500;

    private readonly List<ReceiptAllocation> _allocations = [];

    private Receipt(
        ReceiptId id,
        string number,
        CustomerRef customerId,
        Money amount,
        DateOnly receivedOn,
        ReceiptMethod method,
        string? reference,
        string? notes)
        : base(id)
    {
        Number = number;
        CustomerId = customerId;
        Amount = amount;
        AllocatedAmount = Money.Zero(amount.Currency);
        ReceivedOn = receivedOn;
        Method = method;
        Reference = reference;
        Notes = notes;
        Status = ReceiptStatus.Unallocated;
    }

    /// <summary>Required by EF Core materialization.</summary>
#pragma warning disable CS8618
    private Receipt()
    {
    }
#pragma warning restore CS8618

    /// <summary>Its number, e.g. <c>RC-2026-00042</c>.</summary>
    public string Number { get; private set; } = string.Empty;

    /// <summary>Who paid.</summary>
    public CustomerRef CustomerId { get; private set; }

    /// <summary>How much arrived. Always positive.</summary>
    public Money Amount { get; private set; } = null!;

    /// <summary>How much of it has been matched to documents.</summary>
    public Money AllocatedAmount { get; private set; } = null!;

    /// <summary>The date the money arrived, which is not the date it was entered.</summary>
    public DateOnly ReceivedOn { get; private set; }

    /// <summary>How it arrived.</summary>
    public ReceiptMethod Method { get; private set; }

    /// <summary>The bank reference, cheque number or whatever identifies it on a statement.</summary>
    public string? Reference { get; private set; }

    /// <summary>Anything a person needed to say about it.</summary>
    public string? Notes { get; private set; }

    /// <summary>Where it stands.</summary>
    public ReceiptStatus Status { get; private set; }

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

    /// <summary>What it paid, in the order it was matched.</summary>
    public IReadOnlyList<ReceiptAllocation> Allocations =>
        [.. _allocations.OrderBy(allocation => allocation.AllocatedOn)];

    /// <summary>Money still sitting on the account, waiting to be told what it paid.</summary>
    public Money Unallocated => Amount - AllocatedAmount;

    /// <summary>The currency of everything on this receipt.</summary>
    public Currency Currency => Amount.Currency;

    /// <summary>
    /// Records money that has arrived.
    /// </summary>
    /// <param name="number">The receipt number, taken from the module's counter.</param>
    /// <param name="customerId">Who paid.</param>
    /// <param name="amount">How much. Positive.</param>
    /// <param name="receivedOn">The date the money arrived.</param>
    /// <param name="method">How it arrived.</param>
    /// <param name="reference">The bank reference or cheque number, when there is one.</param>
    /// <param name="notes">Anything else worth recording.</param>
    public static Result<Receipt> Record(
        string? number,
        CustomerRef customerId,
        Money amount,
        DateOnly receivedOn,
        ReceiptMethod method,
        string? reference,
        string? notes)
    {
        ArgumentNullException.ThrowIfNull(amount);

        if (string.IsNullOrWhiteSpace(number))
        {
            return FinanceErrors.Receipt.NumberRequired;
        }

        if (customerId.IsEmpty)
        {
            return FinanceErrors.Receipt.CustomerRequired;
        }

        if (!amount.IsPositive)
        {
            return FinanceErrors.Receipt.AmountNotPositive;
        }

        if (method == ReceiptMethod.Unknown)
        {
            return FinanceErrors.Receipt.MethodRequired;
        }

        return new Receipt(
            ReceiptId.New(),
            Clip(number, MaxNumberLength)!,
            customerId,
            amount,
            receivedOn,
            method,
            Clip(reference, MaxReferenceLength),
            Clip(notes, MaxNotesLength));
    }

    /// <summary>
    /// Records that part of this receipt paid a document.
    /// <para>
    /// Internal for the same reason <see cref="Receivables.OpenItem.Settle"/> is: half a match is
    /// not a thing worth being able to write. <see cref="Receivables.Settlement"/> does both
    /// halves or neither.
    /// </para>
    /// </summary>
    /// <param name="openItemId">The item being paid.</param>
    /// <param name="documentNumber">Its number, copied so a statement reads without a join.</param>
    /// <param name="amount">How much of this receipt goes against it.</param>
    /// <param name="allocatedOn">The date the match was made.</param>
    internal Result<ReceiptAllocationId> Allocate(
        OpenItemId openItemId,
        string documentNumber,
        Money amount,
        DateOnly allocatedOn)
    {
        ArgumentNullException.ThrowIfNull(amount);

        if (amount.Currency != Currency)
        {
            return FinanceErrors.Settlement.CurrencyMismatch;
        }

        if (!amount.IsPositive)
        {
            return FinanceErrors.Settlement.AmountNotPositive;
        }

        if (amount > Unallocated)
        {
            return FinanceErrors.Settlement.ExceedsUnallocated(Number, Unallocated.Amount);
        }

        var allocation = ReceiptAllocation.Create(
            openItemId, documentNumber, amount, allocatedOn);

        _allocations.Add(allocation);
        AllocatedAmount += amount;

        Status = Unallocated.IsZero
            ? ReceiptStatus.Allocated
            : ReceiptStatus.PartiallyAllocated;

        if (Status == ReceiptStatus.Allocated)
        {
            Raise(new ReceiptFullyAllocatedDomainEvent(Id, Number, CustomerId, Amount));
        }

        return allocation.Id;
    }

    /// <summary>Raises the event that says the money arrived. Called once, after numbering.</summary>
    public void Announce() =>
        Raise(new ReceiptRecordedDomainEvent(Id, Number, CustomerId, Amount, ReceivedOn, Method));

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
