using AutoPartsErp.SharedKernel.Primitives;

namespace AutoPartsErp.Modules.Pricing.Domain.Customers.Events;

/// <summary>Terms were agreed with a customer.</summary>
/// <param name="AgreementId">The agreement.</param>
/// <param name="CustomerId">The customer.</param>
/// <param name="PriceListId">The list they buy from.</param>
/// <param name="DiscountPercent">What comes off it.</param>
public sealed record CustomerPricingAgreedDomainEvent(
    CustomerPricingId AgreementId,
    CustomerRef CustomerId,
    PriceListId PriceListId,
    decimal DiscountPercent) : DomainEvent;

/// <summary>
/// A customer's terms changed.
/// <para>
/// Carries what they were as well as what they are. "Moved from trade to wholesale" is the
/// question somebody asks six months later, and an event that only says where they ended up
/// cannot answer it.
/// </para>
/// </summary>
/// <param name="AgreementId">The agreement.</param>
/// <param name="CustomerId">The customer.</param>
/// <param name="PreviousPriceListId">The list they were on.</param>
/// <param name="PriceListId">The list they are on now.</param>
/// <param name="PreviousDiscountPercent">What used to come off.</param>
/// <param name="DiscountPercent">What comes off now.</param>
public sealed record CustomerPricingRenegotiatedDomainEvent(
    CustomerPricingId AgreementId,
    CustomerRef CustomerId,
    PriceListId PreviousPriceListId,
    PriceListId PriceListId,
    decimal PreviousDiscountPercent,
    decimal DiscountPercent) : DomainEvent;

/// <summary>A customer's terms were ended, sending them back to the default list.</summary>
/// <param name="AgreementId">The agreement.</param>
/// <param name="CustomerId">The customer.</param>
/// <param name="EndedOn">The last day the terms apply.</param>
public sealed record CustomerPricingEndedDomainEvent(
    CustomerPricingId AgreementId,
    CustomerRef CustomerId,
    DateOnly EndedOn) : DomainEvent;
