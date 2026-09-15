using AutoPartsErp.Modules.Purchasing.Domain.Agreements.Events;
using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Purchasing.Domain.Agreements;

/// <summary>Where a claim has got to.</summary>
public enum RappelClaimStatus
{
    /// <summary>Not set.</summary>
    Unknown = 0,

    /// <summary>Raised, and the supplier has not been told.</summary>
    Raised = 1,

    /// <summary>The supplier has been asked.</summary>
    Sent = 2,

    /// <summary>They credited all of it.</summary>
    Credited = 3,

    /// <summary>Somebody decided the company will not get it.</summary>
    WrittenOff = 4,
}

/// <summary>
/// What a supplier owes the company for a rebate period, as something somebody can chase.
/// <para>
/// The accrual works out the number; this is what turns it into money the company actually gets.
/// Two gaps close here, and they are the same gap seen from two distances. A rebate settled
/// <b>on the invoice</b> is taken at the rate the period had reached on the day, so crossing a
/// step in October leaves every invoice since January settled short — and a supplier's issued
/// document cannot be re-rated, not by this system and not by theirs, so the difference can only
/// ever be asked for. A rebate settled <b>by credit note</b> is the whole year's, outstanding
/// until the note arrives. In both cases the company has to go and ask.
/// </para>
/// <para>
/// <b>It is not an invoice.</b> The company does not issue a document to its supplier over this;
/// what arrives is their credit note. So this is an internal record of a request — it has a
/// number so two people can talk about the same one, a state so nobody chases it twice, and no
/// legal standing at all.
/// </para>
/// <para>
/// <b>Part of a claim can be credited.</b> A supplier who agrees with four fifths of the figure
/// sends a note for four fifths, and the rest stays outstanding until somebody either gets it or
/// writes it off. Forcing all-or-nothing would mean a screen that reads as though the company got
/// everything it asked for.
/// </para>
/// </summary>
public sealed class RappelClaim : AggregateRoot<RappelClaimId>, IAuditable, ITenantScoped
{
    /// <summary>Longest permitted number.</summary>
    public const int MaxNumberLength = 30;

    /// <summary>Longest permitted note or reason.</summary>
    public const int MaxNoteLength = 500;

    private RappelClaim(
        RappelClaimId id,
        string number,
        RappelAccrualId accrualId,
        SupplierRef supplierId,
        string supplierCode,
        DateOnly periodFrom,
        DateOnly periodTo,
        Money claimed)
        : base(id)
    {
        Number = number;
        AccrualId = accrualId;
        SupplierId = supplierId;
        SupplierCode = supplierCode;
        PeriodFrom = periodFrom;
        PeriodTo = periodTo;
        Claimed = claimed;
        Credited = Money.Zero(claimed.Currency);
        CurrencyCode = claimed.Currency.Code;
        Status = RappelClaimStatus.Raised;
        CreatedBy = string.Empty;
    }

    /// <summary>Required by EF Core materialization.</summary>
#pragma warning disable CS8618
    private RappelClaim()
    {
    }
#pragma warning restore CS8618

    /// <summary>Our number for it, so two people can talk about the same claim.</summary>
    public string Number { get; private set; } = string.Empty;

    /// <summary>The period it came from.</summary>
    public RappelAccrualId AccrualId { get; private set; }

    /// <summary>The supplier.</summary>
    public SupplierRef SupplierId { get; private set; }

    /// <summary>Their short code, snapshotted so a screen need not cross the module boundary.</summary>
    public string SupplierCode { get; private set; } = string.Empty;

    /// <summary>The first day of the period.</summary>
    public DateOnly PeriodFrom { get; private set; }

    /// <summary>The last day of the period, inclusive.</summary>
    public DateOnly PeriodTo { get; private set; }

    /// <summary>What the company asked for.</summary>
    public Money Claimed { get; private set; } = null!;

    /// <summary>What the supplier has actually credited against it.</summary>
    public Money Credited { get; private set; } = null!;

    /// <summary>The currency.</summary>
    public string CurrencyCode { get; private set; } = Currency.Default.Code;

    /// <summary>Where it has got to.</summary>
    public RappelClaimStatus Status { get; private set; }

    /// <summary>When the supplier was asked.</summary>
    public DateOnly? SentOn { get; private set; }

    /// <summary>What was said when it was sent, or why it was written off.</summary>
    public string? Note { get; private set; }

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

    /// <summary>The currency.</summary>
    public Currency Currency => Currency.FromCode(CurrencyCode);

    /// <summary>
    /// What is still to come. Never negative: a supplier who credited more than was asked for is
    /// a different conversation, and one this figure would hide by reporting a debt the other way.
    /// </summary>
    public Money Outstanding =>
        Credited.Amount >= Claimed.Amount
            ? Money.Zero(Currency)
            : Claimed - Credited;

    /// <summary>True while the company is still waiting for something.</summary>
    public bool IsOpen =>
        Status is RappelClaimStatus.Raised or RappelClaimStatus.Sent;

    /// <summary>
    /// Raises a claim for what a closed period turned out to be owed.
    /// </summary>
    /// <param name="number">Our number for it.</param>
    /// <param name="accrualId">The period.</param>
    /// <param name="supplierId">The supplier.</param>
    /// <param name="supplierCode">Their short code.</param>
    /// <param name="periodFrom">The first day of the period.</param>
    /// <param name="periodTo">The last day of the period.</param>
    /// <param name="claimed">What is owed.</param>
    public static Result<RappelClaim> Raise(
        string? number,
        RappelAccrualId accrualId,
        SupplierRef supplierId,
        string? supplierCode,
        DateOnly periodFrom,
        DateOnly periodTo,
        Money claimed)
    {
        ArgumentNullException.ThrowIfNull(claimed);

        if (string.IsNullOrWhiteSpace(number))
        {
            return PurchasingErrors.Claim.NumberRequired;
        }

        if (number.Trim().Length > MaxNumberLength)
        {
            return PurchasingErrors.Claim.NumberTooLong;
        }

        if (accrualId.IsEmpty)
        {
            return PurchasingErrors.Claim.PeriodRequired;
        }

        if (supplierId.IsEmpty)
        {
            return PurchasingErrors.Agreement.SupplierRequired;
        }

        if (string.IsNullOrWhiteSpace(supplierCode))
        {
            return PurchasingErrors.Agreement.SupplierCodeRequired;
        }

        if (periodTo < periodFrom)
        {
            return PurchasingErrors.Agreement.PeriodInverted;
        }

        // A claim for nothing is a line on a screen that wastes somebody's attention every time
        // they look at it, and a supplier meeting is short.
        if (!claimed.IsPositive)
        {
            return PurchasingErrors.Claim.NothingToClaim;
        }

        var claim = new RappelClaim(
            RappelClaimId.New(),
            number.Trim().ToUpperInvariant(),
            accrualId,
            supplierId,
            supplierCode.Trim().ToUpperInvariant(),
            periodFrom,
            periodTo,
            claimed);

        claim.Raise(new RappelClaimRaisedDomainEvent(
            claim.Id,
            accrualId,
            supplierId,
            claim.SupplierCode,
            claim.Number,
            periodFrom,
            periodTo,
            claimed.Amount,
            claimed.Currency.Code));

        return claim;
    }

    /// <summary>Records that the supplier has been asked.</summary>
    /// <param name="on">The day they were asked.</param>
    /// <param name="note">What was said, or how.</param>
    public Result Send(DateOnly on, string? note)
    {
        if (Status != RappelClaimStatus.Raised)
        {
            return Status == RappelClaimStatus.Sent
                ? PurchasingErrors.Claim.AlreadySent
                : PurchasingErrors.Claim.Closed;
        }

        if (note is not null && note.Trim().Length > MaxNoteLength)
        {
            return PurchasingErrors.Claim.NoteTooLong;
        }

        Status = RappelClaimStatus.Sent;
        SentOn = on;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

        Raise(new RappelClaimSentDomainEvent(
            Id, SupplierId, SupplierCode, Number, on, Claimed.Amount, CurrencyCode));

        return Result.Success();
    }

    /// <summary>
    /// Records a credit note the supplier sent against this claim.
    /// <para>
    /// Does not have to settle it. A supplier who agrees with four fifths sends four fifths, and
    /// what is left stays outstanding until somebody gets it or writes it off.
    /// </para>
    /// </summary>
    /// <param name="amount">What the credit note is for.</param>
    /// <param name="creditNoteNumber">Their number for it.</param>
    public Result Credit(Money amount, string? creditNoteNumber)
    {
        ArgumentNullException.ThrowIfNull(amount);

        if (!IsOpen)
        {
            return Status == RappelClaimStatus.Credited
                ? PurchasingErrors.Claim.AlreadyCredited
                : PurchasingErrors.Claim.Closed;
        }

        if (amount.Currency != Currency)
        {
            return PurchasingErrors.Agreement.RappelCurrencyMismatch;
        }

        if (!amount.IsPositive)
        {
            return PurchasingErrors.Accrual.CreditNotPositive;
        }

        // More than was asked for is refused rather than absorbed. It means the claim, the
        // accrual or the supplier's arithmetic is wrong, and quietly taking the money is how a
        // company finds out eighteen months later when they ask for it back.
        if ((Credited + amount).Amount > Claimed.Amount)
        {
            return PurchasingErrors.Claim.MoreThanClaimed(
                Outstanding.Amount, Claimed.Currency.Code);
        }

        Credited += amount;

        if (Credited.Amount >= Claimed.Amount)
        {
            Status = RappelClaimStatus.Credited;
        }

        Raise(new RappelClaimCreditedDomainEvent(
            Id,
            AccrualId,
            SupplierId,
            Number,
            creditNoteNumber,
            amount.Amount,
            Outstanding.Amount,
            CurrencyCode));

        return Result.Success();
    }

    /// <summary>
    /// Gives up on what is left, with a reason.
    /// <para>
    /// A real outcome and worth recording as one. A supplier who disputes the scale, a period
    /// nobody noticed until the relationship ended, a figure too small to argue over — all of
    /// them end here, and a claim quietly deleted instead would take the reason with it.
    /// </para>
    /// </summary>
    /// <param name="reason">Why the company will not get it.</param>
    public Result WriteOff(string? reason)
    {
        if (!IsOpen)
        {
            return Status == RappelClaimStatus.Credited
                ? PurchasingErrors.Claim.AlreadyCredited
                : PurchasingErrors.Claim.Closed;
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return PurchasingErrors.Claim.WriteOffReasonRequired;
        }

        if (reason.Trim().Length > MaxNoteLength)
        {
            return PurchasingErrors.Claim.NoteTooLong;
        }

        Money givenUp = Outstanding;

        Status = RappelClaimStatus.WrittenOff;
        Note = reason.Trim();

        Raise(new RappelClaimWrittenOffDomainEvent(
            Id, SupplierId, SupplierCode, Number, givenUp.Amount, Note, CurrencyCode));

        return Result.Success();
    }
}
