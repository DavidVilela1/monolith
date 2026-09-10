using AutoPartsErp.Modules.Purchasing.Domain.Agreements.Events;
using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Purchasing.Domain.Agreements;

/// <summary>
/// What was agreed with one supplier, so a delivery can price itself.
/// <para>
/// This is the half of automatic entry that has to be decided in advance. The warehouse counts
/// what came off the van — that stays a person's job, and should — but nobody should be retyping
/// prices that were settled in a negotiation nine months ago onto a document the system could have
/// written itself. What is here is what the buyer agreed; what arrives on a pallet is checked
/// against it.
/// </para>
/// <para>
/// One live agreement per supplier. Not because a distributor never has two — a separate
/// arrangement for tyres is ordinary — but because "which one applies to this delivery?" needs an
/// answer that never depends on the order two rows came back in. When that day arrives the answer
/// is a product family on the agreement, and the resolution stays a single lookup.
/// </para>
/// </summary>
public sealed class SupplierAgreement : AggregateRoot<SupplierAgreementId>, IAuditable, ISoftDeletable, ITenantScoped
{
    /// <summary>Longest permitted supplier code.</summary>
    public const int MaxSupplierCodeLength = 30;

    /// <summary>Longest permitted note.</summary>
    public const int MaxNoteLength = 500;

    private readonly List<RappelStep> _rappelSteps = [];

    private SupplierAgreement(
        SupplierAgreementId id,
        SupplierRef supplierId,
        string supplierCode,
        Currency currency,
        DateOnly effectiveFrom)
        : base(id)
    {
        SupplierId = supplierId;
        SupplierCode = supplierCode;
        CurrencyCode = currency.Code;
        EffectiveFrom = effectiveFrom;
        RappelBasis = RappelBasis.None;
        RappelPeriod = RappelPeriod.None;
        CreatedBy = string.Empty;
    }

    /// <summary>Required by EF Core materialization.</summary>
#pragma warning disable CS8618
    private SupplierAgreement()
    {
    }
#pragma warning restore CS8618

    /// <summary>The supplier this was agreed with.</summary>
    public SupplierRef SupplierId { get; private set; }

    /// <summary>Their short code, snapshotted so a screen need not cross the module boundary.</summary>
    public string SupplierCode { get; private set; } = string.Empty;

    /// <summary>
    /// The currency the agreement is written in.
    /// <para>
    /// Fixed for its life, like a price list's. A rebate scale whose thresholds silently changed
    /// meaning is a contract nobody would sign and a bug nobody would find for a year.
    /// </para>
    /// </summary>
    public string CurrencyCode { get; private set; } = Currency.Default.Code;

    /// <summary>The first day it applies.</summary>
    public DateOnly EffectiveFrom { get; private set; }

    /// <summary>The last day it applies, inclusive, or null while it is still live.</summary>
    public DateOnly? EffectiveTo { get; private set; }

    /// <summary>
    /// How the rebate is settled: on each invoice, as a credit note at the end of a period, or
    /// not at all.
    /// </summary>
    public RappelBasis RappelBasis { get; private set; }

    /// <summary>The period a <see cref="RappelBasis.PeriodCreditNote"/> rebate is measured over.</summary>
    public RappelPeriod RappelPeriod { get; private set; }

    /// <summary>
    /// The rebate steps as they are stored, empty when there is no rebate.
    /// <para>
    /// Rows rather than one serialized figure, so "which suppliers pay more than three per cent?"
    /// stays a question a query can answer.
    /// </para>
    /// </summary>
    public IReadOnlyList<RappelStep> RappelSteps => _rappelSteps;

    /// <summary>What the buyer wants to remember about how this was agreed.</summary>
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

    /// <inheritdoc />
    public bool IsDeleted { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? DeletedAtUtc { get; set; }

    /// <inheritdoc />
    public string? DeletedBy { get; set; }

    /// <summary>The currency the agreement is written in.</summary>
    public Currency Currency => Currency.FromCode(CurrencyCode);

    /// <summary>
    /// The steps as one object, or null when there is no rebate.
    /// <para>
    /// Rebuilt from the rows rather than stored beside them, so there is exactly one copy of the
    /// truth. The same object serves both bases: what differs between them is not the arithmetic
    /// but when it is measured and who sends the paperwork.
    /// </para>
    /// </summary>
    public RappelScale? Scale =>
        _rappelSteps.Count == 0 ? null : RappelScale.FromStored(_rappelSteps);

    /// <summary>True when a rebate is settled on each invoice as it is drafted.</summary>
    public bool RebatesOnInvoice =>
        RappelBasis == RappelBasis.OnInvoice && _rappelSteps.Count > 0;

    /// <summary>True when a rebate builds up over a period and arrives as a credit note.</summary>
    public bool RebatesByCreditNote =>
        RappelBasis == RappelBasis.PeriodCreditNote && _rappelSteps.Count > 0;

    /// <summary>Opens an agreement with a supplier. No rebate until one is set.</summary>
    /// <param name="supplierId">The supplier.</param>
    /// <param name="supplierCode">Their short code.</param>
    /// <param name="currency">The currency it is written in.</param>
    /// <param name="effectiveFrom">The first day it applies.</param>
    public static Result<SupplierAgreement> Open(
        SupplierRef supplierId,
        string? supplierCode,
        Currency currency,
        DateOnly effectiveFrom)
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

        if (supplierCode.Trim().Length > MaxSupplierCodeLength)
        {
            return PurchasingErrors.Agreement.SupplierCodeTooLong;
        }

        var agreement = new SupplierAgreement(
            SupplierAgreementId.New(),
            supplierId,
            supplierCode.Trim().ToUpperInvariant(),
            currency,
            effectiveFrom);

        agreement.Raise(new SupplierAgreementOpenedDomainEvent(
            agreement.Id, supplierId, agreement.SupplierCode, agreement.CurrencyCode));

        return agreement;
    }

    /// <summary>
    /// Settles the rebate on each invoice as it is drafted.
    /// <para>
    /// The supplier takes it off their own document, so the goods land on the shelf already at
    /// the price the company really paid. Margin is right from the first sale, and there is no
    /// figure to accrue and nothing to reconcile in January.
    /// </para>
    /// <para>
    /// The catch, and it is worth knowing before choosing this: a rate reached in October cannot
    /// be applied to invoices that went out in March. Each document is settled at the rate the
    /// year had reached when it was raised, and crossing a step later leaves a claim on the
    /// difference that this system does not yet chase.
    /// </para>
    /// </summary>
    /// <param name="scale">The steps. A flat percentage is a one-step scale.</param>
    public Result RebateOnInvoice(RappelScale scale)
    {
        ArgumentNullException.ThrowIfNull(scale);

        if (scale.Currency != Currency)
        {
            return PurchasingErrors.Agreement.RappelCurrencyMismatch;
        }

        RappelBasis = RappelBasis.OnInvoice;
        RappelPeriod = RappelPeriod.Annual;
        ReplaceSteps(scale);

        Raise(new SupplierRebateAgreedDomainEvent(
            Id, SupplierId, RappelBasis, RappelPeriod, scale.BestRatePercent));

        return Result.Success();
    }

    /// <summary>
    /// Settles the rebate as a credit note at the end of each period.
    /// <para>
    /// Every invoice is paid at list, and what the year earned arrives afterwards. Which means the
    /// goods sit on the shelf costed higher than they really were for the whole period, and the
    /// credit note lands in January looking like profit that fell out of the sky. The figure has
    /// to be accrued as it is earned or the margin on every sale in between is a lie — which is
    /// the next piece of work and is named as such in the README rather than pretended away.
    /// </para>
    /// </summary>
    /// <param name="scale">The steps the period's purchases climb through.</param>
    /// <param name="period">Monthly, quarterly or annual.</param>
    public Result RebateByCreditNote(RappelScale scale, RappelPeriod period)
    {
        ArgumentNullException.ThrowIfNull(scale);

        if (period is RappelPeriod.None)
        {
            return PurchasingErrors.Agreement.RappelPeriodRequired;
        }

        if (scale.Currency != Currency)
        {
            return PurchasingErrors.Agreement.RappelCurrencyMismatch;
        }

        RappelBasis = RappelBasis.PeriodCreditNote;
        RappelPeriod = period;
        ReplaceSteps(scale);

        Raise(new SupplierRebateAgreedDomainEvent(
            Id, SupplierId, RappelBasis, period, scale.BestRatePercent));

        return Result.Success();
    }

    /// <summary>
    /// Drops the rebate. Documents already raised keep the rate they were raised at.
    /// <para>
    /// Which is the point of the rate being written onto each document rather than looked up when
    /// somebody asks. A supplier who withdraws a rebate in June does not thereby un-earn what was
    /// taken off in May, and a system that recomputed it would say they did.
    /// </para>
    /// </summary>
    public void DropRebate()
    {
        RappelBasis = RappelBasis.None;
        RappelPeriod = RappelPeriod.None;
        _rappelSteps.Clear();
    }

    /// <summary>
    /// The rebate rate to put on a document being raised now, given what the period has bought.
    /// <para>
    /// Zero unless the rebate is settled on the invoice. A credit-note rebate takes nothing off
    /// the document — the company pays the full figure and is credited later — and returning its
    /// rate here is how a system ends up taking the same discount twice.
    /// </para>
    /// </summary>
    /// <param name="purchasedThisPeriod">Net purchases so far in the period, this document aside.</param>
    public decimal InvoiceRateOn(Money purchasedThisPeriod)
    {
        ArgumentNullException.ThrowIfNull(purchasedThisPeriod);

        return RebatesOnInvoice ? Scale!.RateFor(purchasedThisPeriod) : 0m;
    }

    /// <summary>Records the buyer's note about how this was agreed.</summary>
    /// <param name="note">Free text, or null to clear.</param>
    public Result SetNote(string? note)
    {
        if (note is not null && note.Trim().Length > MaxNoteLength)
        {
            return PurchasingErrors.Agreement.NoteTooLong;
        }

        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

        return Result.Success();
    }

    /// <summary>
    /// Ends the agreement on a given day. Deliveries after it price themselves at list.
    /// </summary>
    /// <param name="lastDay">The last day it applies, inclusive.</param>
    public Result End(DateOnly lastDay)
    {
        if (lastDay < EffectiveFrom)
        {
            return PurchasingErrors.Agreement.PeriodInverted;
        }

        EffectiveTo = lastDay;

        Raise(new SupplierAgreementEndedDomainEvent(Id, SupplierId, lastDay));

        return Result.Success();
    }

    private void ReplaceSteps(RappelScale scale)
    {
        _rappelSteps.Clear();
        _rappelSteps.AddRange(scale.Steps);
    }

    /// <summary>True when the given day falls inside the agreement's life.</summary>
    /// <param name="on">The day being priced for.</param>
    public bool IsEffectiveOn(DateOnly on) =>
        on >= EffectiveFrom && (EffectiveTo is null || on <= EffectiveTo.Value);
}

/// <summary>How a supplier's rebate is settled.</summary>
public enum RappelBasis
{
    /// <summary>Nothing agreed. The company pays what the invoice says.</summary>
    None = 0,

    /// <summary>
    /// Taken off the supplier's own invoice, so the goods are costed correctly from the moment
    /// they land.
    /// </summary>
    OnInvoice = 1,

    /// <summary>
    /// Paid as a credit note once the period is over, against what the period bought.
    /// </summary>
    PeriodCreditNote = 2,
}

/// <summary>The stretch of time a period rebate is measured over.</summary>
public enum RappelPeriod
{
    /// <summary>Not applicable.</summary>
    None = 0,

    /// <summary>Calendar month.</summary>
    Monthly = 1,

    /// <summary>Calendar quarter.</summary>
    Quarterly = 2,

    /// <summary>Calendar year.</summary>
    Annual = 3,
}
