using AutoPartsErp.SharedKernel.Primitives;

namespace AutoPartsErp.Modules.Partners.Domain.Partners.Events;

/// <summary>Raised when a partner is registered.</summary>
/// <param name="PartnerId">The new partner.</param>
/// <param name="Code">Their short code.</param>
/// <param name="LegalName">Their registered name.</param>
public sealed record PartnerCreatedDomainEvent(
    PartnerId PartnerId,
    string Code,
    string LegalName) : DomainEvent;

/// <summary>
/// Raised when we start selling to a partner.
/// <para>
/// Carries the whole commercial arrangement, not just the identity. Sales keeps its own record
/// of who may buy and on what terms, and an event that made it call back into Partners for the
/// details would not be a boundary at all.
/// </para>
/// </summary>
/// <param name="PartnerId">The partner.</param>
/// <param name="Code">Their short code.</param>
/// <param name="LegalName">Their registered name, as it goes on an invoice.</param>
/// <param name="CreditLimit">The agreed limit.</param>
/// <param name="CurrencyCode">Currency of the limit.</param>
/// <param name="PaymentDueInDays">Days to pay. Zero means on delivery.</param>
/// <param name="PaymentEndOfMonth">True when the days run from the end of the invoice month.</param>
/// <param name="PriceListCode">Which price list applies.</param>
public sealed record CustomerRoleGrantedDomainEvent(
    PartnerId PartnerId,
    string Code,
    string LegalName,
    decimal CreditLimit,
    string CurrencyCode,
    int PaymentDueInDays,
    bool PaymentEndOfMonth,
    string? PriceListCode) : DomainEvent;

/// <summary>Raised when we start buying from a partner.</summary>
/// <param name="PartnerId">The partner.</param>
/// <param name="Code">Their short code.</param>
public sealed record SupplierRoleGrantedDomainEvent(PartnerId PartnerId, string Code) : DomainEvent;

/// <summary>
/// Raised when a credit limit moves. Finance wants the trail: who raised whose limit, and when,
/// is the first question after a bad debt.
/// </summary>
/// <param name="PartnerId">The partner.</param>
/// <param name="Code">Their short code.</param>
/// <param name="PreviousLimit">What it was.</param>
/// <param name="NewLimit">What it became.</param>
public sealed record CreditLimitChangedDomainEvent(
    PartnerId PartnerId,
    string Code,
    decimal PreviousLimit,
    decimal NewLimit) : DomainEvent;

/// <summary>
/// Raised when a partner is stopped from placing new orders.
/// Sales listens for this: a held customer must not get through the counter.
/// </summary>
/// <param name="PartnerId">The partner.</param>
/// <param name="Code">Their short code.</param>
/// <param name="Reason">Why they were held.</param>
public sealed record PartnerPlacedOnHoldDomainEvent(
    PartnerId PartnerId,
    string Code,
    string Reason) : DomainEvent;

/// <summary>Raised when a hold is lifted.</summary>
/// <param name="PartnerId">The partner.</param>
/// <param name="Code">Their short code.</param>
public sealed record PartnerHoldReleasedDomainEvent(PartnerId PartnerId, string Code) : DomainEvent;

/// <summary>Raised when the relationship ends.</summary>
/// <param name="PartnerId">The partner.</param>
/// <param name="Code">Their short code.</param>
public sealed record PartnerClosedDomainEvent(PartnerId PartnerId, string Code) : DomainEvent;
