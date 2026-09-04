using AutoPartsErp.SharedKernel.Messaging;

namespace AutoPartsErp.IntegrationEvents.Pricing;

/// <summary>
/// A price moved.
/// <para>
/// The one fact from this module that anybody outside it needs. A counter screen holding a
/// quote from five minutes ago wants to know it is stale; a report wants the history; and a
/// customer-facing catalogue wants to refresh what it shows. None of them should be polling a
/// price list to find out.
/// </para>
/// <para>
/// Carries the new price and not the old one. Pricing keeps its history in its own schema, and an
/// integration event that promised "what it was before" would be promising something the
/// publisher cannot guarantee once the same part is repriced twice in a second.
/// </para>
/// </summary>
/// <param name="PriceListId">The list the price is in.</param>
/// <param name="PartId">The part.</param>
/// <param name="MinimumQuantity">The quantity break that moved.</param>
/// <param name="UnitPrice">What it moved to.</param>
/// <param name="CurrencyCode">The currency the list prices in.</param>
/// <param name="TenantId">The owning tenant.</param>
public sealed record PriceChangedIntegrationEvent(
    Guid PriceListId,
    Guid PartId,
    decimal MinimumQuantity,
    decimal UnitPrice,
    string CurrencyCode,
    Guid TenantId) : IntegrationEvent;

/// <summary>
/// A price list went live, so quotes start coming from it.
/// <para>
/// Worth announcing on its own because activation can change what a customer pays without any
/// individual price moving — a promotion going live reprices every part it touches at once, and
/// nothing would say so if only price changes were published.
/// </para>
/// </summary>
/// <param name="PriceListId">The list.</param>
/// <param name="Code">Its code.</param>
/// <param name="Kind">Standard, Customer or Promotion.</param>
/// <param name="CurrencyCode">The currency it prices in.</param>
/// <param name="TenantId">The owning tenant.</param>
public sealed record PriceListActivatedIntegrationEvent(
    Guid PriceListId,
    string Code,
    string Kind,
    string CurrencyCode,
    Guid TenantId) : IntegrationEvent;

/// <summary>
/// A customer's terms changed: a different list, a different discount, or both.
/// <para>
/// Sales cares because an open quotation for that customer is now wrong. Finance cares because
/// the margin on everything outstanding just moved. Carries where they came from as well as where
/// they went, because "moved from trade to wholesale" is the question asked six months later and
/// an event that only says where they ended up cannot answer it.
/// </para>
/// </summary>
/// <param name="CustomerId">The customer.</param>
/// <param name="PreviousPriceListId">The list they were on.</param>
/// <param name="PriceListId">The list they are on now.</param>
/// <param name="PreviousDiscountPercent">What used to come off.</param>
/// <param name="DiscountPercent">What comes off now.</param>
/// <param name="TenantId">The owning tenant.</param>
public sealed record CustomerPricingChangedIntegrationEvent(
    Guid CustomerId,
    Guid PreviousPriceListId,
    Guid PriceListId,
    decimal PreviousDiscountPercent,
    decimal DiscountPercent,
    Guid TenantId) : IntegrationEvent;
