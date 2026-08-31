using AutoPartsErp.SharedKernel.Messaging;

namespace AutoPartsErp.IntegrationEvents.Partners;

/// <summary>
/// A partner was stopped from placing new orders, usually because their account is overdue.
/// <para>
/// Sales listens for this. A held customer reaching the counter and being served anyway is the
/// exact failure the hold exists to prevent, and the person on the counter is rarely the person
/// who knows about the unpaid invoice.
/// </para>
/// </summary>
/// <param name="PartnerId">The partner.</param>
/// <param name="Code">Their short code.</param>
/// <param name="Reason">Why they were held, in words somebody can repeat to them.</param>
/// <param name="TenantId">The owning tenant.</param>
public sealed record PartnerPlacedOnHoldIntegrationEvent(
    Guid PartnerId,
    string Code,
    string Reason,
    Guid TenantId) : IntegrationEvent;

/// <summary>A hold was lifted and the partner may trade again.</summary>
/// <param name="PartnerId">The partner.</param>
/// <param name="Code">Their short code.</param>
/// <param name="TenantId">The owning tenant.</param>
public sealed record PartnerHoldReleasedIntegrationEvent(
    Guid PartnerId,
    string Code,
    Guid TenantId) : IntegrationEvent;

/// <summary>
/// A credit limit changed. Finance wants the trail: who raised whose limit, and when, is the
/// first question asked after a bad debt.
/// </summary>
/// <param name="PartnerId">The partner.</param>
/// <param name="Code">Their short code.</param>
/// <param name="PreviousLimit">What it was.</param>
/// <param name="NewLimit">What it became.</param>
/// <param name="TenantId">The owning tenant.</param>
public sealed record CreditLimitChangedIntegrationEvent(
    Guid PartnerId,
    string Code,
    decimal PreviousLimit,
    decimal NewLimit,
    Guid TenantId) : IntegrationEvent;

/// <summary>
/// A supplier's terms were set or changed. Inventory can use the lead time to size reorder
/// points: a supplier who takes two weeks needs a higher trigger than one who delivers overnight.
/// </summary>
/// <param name="PartnerId">The supplier.</param>
/// <param name="Code">Their short code.</param>
/// <param name="LeadTimeDays">Typical days from order to delivery.</param>
/// <param name="TenantId">The owning tenant.</param>
public sealed record SupplierTermsChangedIntegrationEvent(
    Guid PartnerId,
    string Code,
    int LeadTimeDays,
    Guid TenantId) : IntegrationEvent;
