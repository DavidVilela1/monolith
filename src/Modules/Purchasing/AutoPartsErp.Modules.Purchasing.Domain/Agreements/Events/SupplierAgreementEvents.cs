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
