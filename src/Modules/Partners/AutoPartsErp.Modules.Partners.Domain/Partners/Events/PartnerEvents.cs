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

/// <summary>Raised when we start selling to a partner.</summary>
/// <param name="PartnerId">The partner.</param>
/// <param name="Code">Their short code.</param>
/// <param name="CreditLimit">The agreed limit.</param>
public sealed record CustomerRoleGrantedDomainEvent(
    PartnerId PartnerId,
    string Code,
    decimal CreditLimit) : DomainEvent;

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
