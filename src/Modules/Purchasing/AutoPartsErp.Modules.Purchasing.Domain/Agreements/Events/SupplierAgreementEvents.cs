using AutoPartsErp.SharedKernel.Primitives;

namespace AutoPartsErp.Modules.Purchasing.Domain.Agreements.Events;

/// <summary>An agreement was opened with a supplier.</summary>
/// <param name="AgreementId">The agreement.</param>
/// <param name="SupplierId">The supplier.</param>
/// <param name="SupplierCode">Their short code.</param>
/// <param name="CurrencyCode">The currency it is written in.</param>
public sealed record SupplierAgreementOpenedDomainEvent(
    SupplierAgreementId AgreementId,
    SupplierRef SupplierId,
    string SupplierCode,
    string CurrencyCode) : DomainEvent;

/// <summary>
/// A rebate was agreed, or an existing one was changed.
/// <para>
/// Carries the basis rather than the whole scale. What another part of the system needs to know is
/// whether money comes off the invoice or arrives later — the difference between costing the shelf
/// correctly today and having to accrue for it — and the steps themselves are a Purchasing detail
/// nobody else can act on.
/// </para>
/// </summary>
/// <param name="AgreementId">The agreement.</param>
/// <param name="SupplierId">The supplier.</param>
/// <param name="Basis">Whether it comes off the invoice or arrives as a credit note.</param>
/// <param name="Period">The stretch of time it is measured over.</param>
/// <param name="BestRatePercent">The highest rate the scale can reach.</param>
public sealed record SupplierRebateAgreedDomainEvent(
    SupplierAgreementId AgreementId,
    SupplierRef SupplierId,
    RappelBasis Basis,
    RappelPeriod Period,
    decimal BestRatePercent) : DomainEvent;

/// <summary>
/// An agreement was ended. Deliveries after the last day price themselves at list.
/// </summary>
/// <param name="AgreementId">The agreement.</param>
/// <param name="SupplierId">The supplier.</param>
/// <param name="LastDay">The last day it applies, inclusive.</param>
public sealed record SupplierAgreementEndedDomainEvent(
    SupplierAgreementId AgreementId,
    SupplierRef SupplierId,
    DateOnly LastDay) : DomainEvent;

/// <summary>
/// The period crossed a step and the whole of it is now worth more.
/// <para>
/// Announced only when the rate actually moves. A buyer wants to be told the year has gone to
/// three per cent; being told another eleven euros were bought is noise, and noise is what makes
/// people stop reading.
/// </para>
/// </summary>
/// <param name="AccrualId">The period.</param>
/// <param name="SupplierId">The supplier.</param>
/// <param name="SupplierCode">Their short code.</param>
/// <param name="RatePercent">The rate the period has now reached.</param>
/// <param name="PurchasedAmount">What the period has bought, net.</param>
/// <param name="GainedAmount">
/// What crossing the step was worth on its own — the re-rating of everything already bought, which
/// is usually far more than the goods that crossed it.
/// </param>
/// <param name="OutstandingAmount">What the supplier still owes for the period.</param>
/// <param name="CurrencyCode">The currency.</param>
public sealed record RappelStepReachedDomainEvent(
    RappelAccrualId AccrualId,
    SupplierRef SupplierId,
    string SupplierCode,
    decimal RatePercent,
    decimal PurchasedAmount,
    decimal GainedAmount,
    decimal OutstandingAmount,
    string CurrencyCode) : DomainEvent;

/// <summary>The supplier credited some of what the period earned.</summary>
/// <param name="AccrualId">The period.</param>
/// <param name="SupplierId">The supplier.</param>
/// <param name="CreditedAmount">What the credit note was for.</param>
/// <param name="OutstandingAmount">What is still owed after it.</param>
/// <param name="CurrencyCode">The currency.</param>
public sealed record RappelCreditedDomainEvent(
    RappelAccrualId AccrualId,
    SupplierRef SupplierId,
    decimal CreditedAmount,
    decimal OutstandingAmount,
    string CurrencyCode) : DomainEvent;

/// <summary>
/// A rebate period ended, and what it is still owed became a claim.
/// </summary>
/// <param name="AccrualId">The period.</param>
/// <param name="SupplierId">The supplier.</param>
/// <param name="SupplierCode">Their short code.</param>
/// <param name="PeriodFrom">The first day.</param>
/// <param name="PeriodTo">The last day, inclusive.</param>
/// <param name="PurchasedAmount">What the period bought, net.</param>
/// <param name="EarnedAmount">What the scale says it earned.</param>
/// <param name="OutstandingAmount">What the supplier still owes for it.</param>
/// <param name="CurrencyCode">The currency.</param>
public sealed record RappelPeriodClosedDomainEvent(
    RappelAccrualId AccrualId,
    SupplierRef SupplierId,
    string SupplierCode,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    decimal PurchasedAmount,
    decimal EarnedAmount,
    decimal OutstandingAmount,
    string CurrencyCode) : DomainEvent;

/// <summary>
/// A closed period turned out to be owed something, and somebody has to ask for it.
/// <para>
/// The moment the arithmetic becomes a job. Up to here the shortfall was a number on a screen;
/// from here it is a thing with a number and a state that somebody chases.
/// </para>
/// </summary>
/// <param name="ClaimId">The claim.</param>
/// <param name="AccrualId">The period it came from.</param>
/// <param name="SupplierId">The supplier.</param>
/// <param name="SupplierCode">Their short code.</param>
/// <param name="Number">Our number for the claim.</param>
/// <param name="PeriodFrom">The first day of the period.</param>
/// <param name="PeriodTo">The last day of the period.</param>
/// <param name="ClaimedAmount">What is being asked for.</param>
/// <param name="CurrencyCode">The currency.</param>
public sealed record RappelClaimRaisedDomainEvent(
    RappelClaimId ClaimId,
    RappelAccrualId AccrualId,
    SupplierRef SupplierId,
    string SupplierCode,
    string Number,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    decimal ClaimedAmount,
    string CurrencyCode) : DomainEvent;

/// <summary>The supplier was asked.</summary>
/// <param name="ClaimId">The claim.</param>
/// <param name="SupplierId">The supplier.</param>
/// <param name="SupplierCode">Their short code.</param>
/// <param name="Number">Our number for the claim.</param>
/// <param name="SentOn">The day they were asked.</param>
/// <param name="ClaimedAmount">What was asked for.</param>
/// <param name="CurrencyCode">The currency.</param>
public sealed record RappelClaimSentDomainEvent(
    RappelClaimId ClaimId,
    SupplierRef SupplierId,
    string SupplierCode,
    string Number,
    DateOnly SentOn,
    decimal ClaimedAmount,
    string CurrencyCode) : DomainEvent;

/// <summary>The supplier credited something against a claim.</summary>
/// <param name="ClaimId">The claim.</param>
/// <param name="AccrualId">The period it came from.</param>
/// <param name="SupplierId">The supplier.</param>
/// <param name="Number">Our number for the claim.</param>
/// <param name="CreditNoteNumber">Their number for the credit note, when they gave one.</param>
/// <param name="Amount">What they credited.</param>
/// <param name="OutstandingAmount">What is still to come.</param>
/// <param name="CurrencyCode">The currency.</param>
public sealed record RappelClaimCreditedDomainEvent(
    RappelClaimId ClaimId,
    RappelAccrualId AccrualId,
    SupplierRef SupplierId,
    string Number,
    string? CreditNoteNumber,
    decimal Amount,
    decimal OutstandingAmount,
    string CurrencyCode) : DomainEvent;

/// <summary>
/// Somebody gave up on what was left of a claim.
/// <para>
/// Its own event rather than a status change folded into something else. Writing off money the
/// company was owed is a decision, and the sentence behind it is what a buyer's successor needs.
/// </para>
/// </summary>
/// <param name="ClaimId">The claim.</param>
/// <param name="SupplierId">The supplier.</param>
/// <param name="SupplierCode">Their short code.</param>
/// <param name="Number">Our number for the claim.</param>
/// <param name="Amount">What was given up.</param>
/// <param name="Reason">Why.</param>
/// <param name="CurrencyCode">The currency.</param>
public sealed record RappelClaimWrittenOffDomainEvent(
    RappelClaimId ClaimId,
    SupplierRef SupplierId,
    string SupplierCode,
    string Number,
    decimal Amount,
    string Reason,
    string CurrencyCode) : DomainEvent;
