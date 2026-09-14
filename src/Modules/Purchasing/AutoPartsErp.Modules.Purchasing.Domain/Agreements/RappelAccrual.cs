using AutoPartsErp.Modules.Purchasing.Domain.Agreements.Events;
using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Purchasing.Domain.Agreements;

/// <summary>
/// What one supplier's rebate is worth so far this period, and how much of it the company has
/// actually had.
/// <para>
/// One object for what looked like two problems. A rebate settled <b>on the invoice</b> is taken
/// at the rate the period had reached on the day, so crossing a step in October leaves the
/// invoices from March settled two per cent short — a claim nobody was chasing. A rebate settled
/// <b>by credit note</b> is taken at the end, so the whole of it is outstanding all year and the
/// stock is carried too high until it arrives. Those are the same arithmetic seen from two
/// distances: what the scale says the period has earned, less what has already come off documents.
/// </para>
/// <para>
/// It is a running total rather than a query over the invoices, and that is deliberate. The figure
/// has to survive a document being disputed, reopened, corrected and settled again; a sum computed
/// on the fly would move under somebody every time it was asked, and "how much rappel are we owed?"
/// is a question a buyer takes to a supplier meeting.
/// </para>
/// </summary>
public sealed class RappelAccrual : AggregateRoot<RappelAccrualId>, IAuditable, ITenantScoped
{
    private RappelAccrual(
        RappelAccrualId id,
        SupplierRef supplierId,
        string supplierCode,
        RappelBasis basis,
        DateOnly periodFrom,
        DateOnly periodTo,
        Currency currency)
        : base(id)
    {
        SupplierId = supplierId;
        SupplierCode = supplierCode;
        Basis = basis;
        PeriodFrom = periodFrom;
        PeriodTo = periodTo;
        CurrencyCode = currency.Code;
        Purchased = Money.Zero(currency);
        TakenOnInvoices = Money.Zero(currency);
        Earned = Money.Zero(currency);
        Credited = Money.Zero(currency);
        CreatedBy = string.Empty;
    }

    /// <summary>Required by EF Core materialization.</summary>
#pragma warning disable CS8618
    private RappelAccrual()
    {
    }
#pragma warning restore CS8618

    /// <summary>The supplier.</summary>
    public SupplierRef SupplierId { get; private set; }

    /// <summary>Their short code, snapshotted so a screen need not cross the module boundary.</summary>
    public string SupplierCode { get; private set; } = string.Empty;

    /// <summary>
    /// How the rebate is settled, snapshotted when the period opened.
    /// <para>
    /// Snapshotted rather than read from the agreement, because the agreement can change in
    /// August and this period was measured under what was agreed in January. A period that
    /// re-explained itself with today's basis would turn every mid-year renegotiation into a
    /// silent restatement of the months before it.
    /// </para>
    /// </summary>
    public RappelBasis Basis { get; private set; }

    /// <summary>The first day of the period.</summary>
    public DateOnly PeriodFrom { get; private set; }

    /// <summary>The last day of the period, inclusive.</summary>
    public DateOnly PeriodTo { get; private set; }

    /// <summary>The currency everything here is in.</summary>
    public string CurrencyCode { get; private set; } = Currency.Default.Code;

    /// <summary>
    /// What the period has bought, net of anything already taken off the documents.
    /// <para>
    /// Net, because that is the figure a supplier's scale is written against: "a partir de 25.000
    /// de compras" means twenty-five thousand actually invoiced, not twenty-five thousand before
    /// the discount they themselves gave.
    /// </para>
    /// </summary>
    public Money Purchased { get; private set; } = null!;

    /// <summary>What has already come off invoices as a rebate.</summary>
    public Money TakenOnInvoices { get; private set; } = null!;

    /// <summary>What the scale says the period has earned on everything bought so far.</summary>
    public Money Earned { get; private set; } = null!;

    /// <summary>What the supplier has actually credited against this period.</summary>
    public Money Credited { get; private set; } = null!;

    /// <summary>True once the period is over and nothing more will be bought into it.</summary>
    public bool IsClosed { get; private set; }

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

    /// <summary>The currency everything here is in.</summary>
    public Currency Currency => Currency.FromCode(CurrencyCode);

    /// <summary>
    /// What the company is still owed for this period: what the scale earned, less what it has
    /// already had by either route.
    /// <para>
    /// For a credit-note rebate this is the whole accrual, and it is the figure the shelf is
    /// carrying too high until the note arrives. For an on-invoice rebate it is the shortfall from
    /// crossing a step after documents had already gone out at a lower rate — money the supplier
    /// owes and will not send unasked.
    /// </para>
    /// <para>
    /// Never negative. A company that took more off its invoices than the year earned has a
    /// different problem, and it is one this figure would hide by reporting it as a debt owed the
    /// other way.
    /// </para>
    /// </summary>
    public Money Outstanding
    {
        get
        {
            Money had = TakenOnInvoices + Credited;

            return Earned.Amount <= had.Amount ? Money.Zero(Currency) : Earned - had;
        }
    }

    /// <summary>
    /// True when the company has taken more off its documents than the period turned out to earn.
    /// <para>
    /// Real, and worth surfacing rather than clamping away. A scale reached in October and lost
    /// again — a return, a cancelled order, a document credited — leaves invoices already settled
    /// at a rate the year no longer justifies, and the supplier will notice before the company
    /// does.
    /// </para>
    /// </summary>
    public bool IsOverclaimed => (TakenOnInvoices + Credited).Amount > Earned.Amount;

    /// <summary>Opens a period for a supplier.</summary>
    /// <param name="supplierId">The supplier.</param>
    /// <param name="supplierCode">Their short code.</param>
    /// <param name="basis">How the rebate is settled, as it stood when the period opened.</param>
    /// <param name="periodFrom">The first day.</param>
    /// <param name="periodTo">The last day, inclusive.</param>
    /// <param name="currency">The currency.</param>
    public static Result<RappelAccrual> Open(
        SupplierRef supplierId,
        string? supplierCode,
        RappelBasis basis,
        DateOnly periodFrom,
        DateOnly periodTo,
        Currency currency)
    {
        ArgumentNullException.ThrowIfNull(currency);

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

        if (basis == RappelBasis.None)
        {
            return PurchasingErrors.Accrual.NoRebateToAccrue;
        }

        return new RappelAccrual(
            RappelAccrualId.New(),
            supplierId,
            supplierCode.Trim().ToUpperInvariant(),
            basis,
            periodFrom,
            periodTo,
            currency);
    }

    /// <summary>
    /// Records a settled document against the period, and works out what it is now worth.
    /// <para>
    /// The earned figure is recomputed from the whole period rather than added to, because the
    /// scale is not marginal: crossing a step re-rates everything bought so far, and a running
    /// sum of per-document rebates would keep the earlier ones at the rate they were bought at.
    /// That is precisely the error this object exists to find.
    /// </para>
    /// </summary>
    /// <param name="netAmount">What the document was worth, net of the rebate taken on it.</param>
    /// <param name="rebateTakenOnDocument">What came off that document as a rebate.</param>
    /// <param name="scale">The steps, as agreed.</param>
    public Result Record(Money netAmount, Money rebateTakenOnDocument, RappelScale scale)
    {
        ArgumentNullException.ThrowIfNull(netAmount);
        ArgumentNullException.ThrowIfNull(rebateTakenOnDocument);
        ArgumentNullException.ThrowIfNull(scale);

        if (IsClosed)
        {
            return PurchasingErrors.Accrual.AlreadyClosed;
        }

        if (netAmount.Currency != Currency
            || rebateTakenOnDocument.Currency != Currency
            || scale.Currency != Currency)
        {
            return PurchasingErrors.Agreement.RappelCurrencyMismatch;
        }

        Purchased += netAmount;
        TakenOnInvoices += rebateTakenOnDocument;

        Money earned = scale.EarnedOn(Purchased);
        Money before = Earned;

        Earned = earned;

        // Announced only when a step is actually crossed. A buyer wants to be told the year has
        // moved to three per cent; being told that another eleven euros were bought is noise, and
        // noise is what makes people stop reading.
        if (scale.RateFor(Purchased) > scale.RateFor(Purchased - netAmount))
        {
            Raise(new RappelStepReachedDomainEvent(
                Id,
                SupplierId,
                SupplierCode,
                scale.RateFor(Purchased),
                Purchased.Amount,
                (earned - before).Amount,
                Outstanding.Amount,
                CurrencyCode));
        }

        return Result.Success();
    }

    /// <summary>
    /// Takes a settled document back out of the period, because it was credited or withdrawn.
    /// <para>
    /// The mirror of <see cref="Record"/>, and it can take the period back down through a step.
    /// Without it a cancelled order would leave the year permanently claiming a rate it never
    /// reached, which is the kind of error a supplier finds first.
    /// </para>
    /// </summary>
    /// <param name="netAmount">What the document was worth.</param>
    /// <param name="rebateTakenOnDocument">What had come off it as a rebate.</param>
    /// <param name="scale">The steps, as agreed.</param>
    public Result Reverse(Money netAmount, Money rebateTakenOnDocument, RappelScale scale)
    {
        ArgumentNullException.ThrowIfNull(netAmount);
        ArgumentNullException.ThrowIfNull(rebateTakenOnDocument);
        ArgumentNullException.ThrowIfNull(scale);

        if (IsClosed)
        {
            return PurchasingErrors.Accrual.AlreadyClosed;
        }

        if (netAmount.Currency != Currency
            || rebateTakenOnDocument.Currency != Currency
            || scale.Currency != Currency)
        {
            return PurchasingErrors.Agreement.RappelCurrencyMismatch;
        }

        if (netAmount.Amount > Purchased.Amount)
        {
            return PurchasingErrors.Accrual.ReversalTooLarge;
        }

        Purchased -= netAmount;
        TakenOnInvoices -= rebateTakenOnDocument;
        Earned = scale.EarnedOn(Purchased);

        return Result.Success();
    }

    /// <summary>
    /// Records a credit note the supplier sent against this period.
    /// <para>
    /// Does not have to match <see cref="Outstanding"/>, and refusing one that did not would be
    /// the system arguing with a document that already exists. What it does is stop the same note
    /// being recorded twice into a figure somebody is going to chase.
    /// </para>
    /// </summary>
    /// <param name="amount">What the credit note is for.</param>
    public Result RecordCreditNote(Money amount)
    {
        ArgumentNullException.ThrowIfNull(amount);

        if (amount.Currency != Currency)
        {
            return PurchasingErrors.Agreement.RappelCurrencyMismatch;
        }

        if (!amount.IsPositive)
        {
            return PurchasingErrors.Accrual.CreditNotPositive;
        }

        Credited += amount;

        Raise(new RappelCreditedDomainEvent(
            Id, SupplierId, amount.Amount, Outstanding.Amount, CurrencyCode));

        return Result.Success();
    }

    /// <summary>
    /// Closes the period. Nothing more is bought into it, and what is outstanding becomes a claim.
    /// </summary>
    /// <param name="today">The current day. The period has to be over.</param>
    public Result Close(DateOnly today)
    {
        if (IsClosed)
        {
            return PurchasingErrors.Accrual.AlreadyClosed;
        }

        // A period closed early would stop counting purchases that still belong to it, and the
        // year would settle at a step it had not finished climbing.
        if (today <= PeriodTo)
        {
            return PurchasingErrors.Accrual.PeriodNotOver;
        }

        IsClosed = true;

        Raise(new RappelPeriodClosedDomainEvent(
            Id,
            SupplierId,
            SupplierCode,
            PeriodFrom,
            PeriodTo,
            Purchased.Amount,
            Earned.Amount,
            Outstanding.Amount,
            CurrencyCode));

        return Result.Success();
    }

    /// <summary>True when the given day falls inside this period.</summary>
    /// <param name="on">The day.</param>
    public bool Covers(DateOnly on) => on >= PeriodFrom && on <= PeriodTo;
}
