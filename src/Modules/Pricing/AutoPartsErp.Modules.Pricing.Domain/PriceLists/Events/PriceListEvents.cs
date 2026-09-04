using AutoPartsErp.SharedKernel.Primitives;

namespace AutoPartsErp.Modules.Pricing.Domain.PriceLists.Events;

/// <summary>A price list was opened.</summary>
/// <param name="PriceListId">The list.</param>
/// <param name="Code">Its code.</param>
/// <param name="Kind">What it is for.</param>
/// <param name="CurrencyCode">The currency its prices are in.</param>
public sealed record PriceListOpenedDomainEvent(
    PriceListId PriceListId,
    string Code,
    PriceListKind Kind,
    string CurrencyCode) : DomainEvent;

/// <summary>A price list went live.</summary>
/// <param name="PriceListId">The list.</param>
/// <param name="Code">Its code.</param>
public sealed record PriceListActivatedDomainEvent(
    PriceListId PriceListId,
    string Code) : DomainEvent;

/// <summary>A price list was withdrawn.</summary>
/// <param name="PriceListId">The list.</param>
/// <param name="Code">Its code.</param>
public sealed record PriceListArchivedDomainEvent(
    PriceListId PriceListId,
    string Code) : DomainEvent;

/// <summary>A price list became the fallback for customers with no agreement.</summary>
/// <param name="PriceListId">The list.</param>
/// <param name="Code">Its code.</param>
public sealed record PriceListMadeDefaultDomainEvent(
    PriceListId PriceListId,
    string Code) : DomainEvent;

/// <summary>
/// A price moved.
/// <para>
/// Carries the amount as a bare decimal rather than a <c>Money</c>: the currency is a property of
/// the list, not of the change, and an event that repeats it is an event that can contradict it.
/// </para>
/// </summary>
/// <param name="EntryId">The entry that changed.</param>
/// <param name="PriceListId">The list it belongs to.</param>
/// <param name="PartId">The part.</param>
/// <param name="MinimumQuantity">The break that moved.</param>
/// <param name="UnitPrice">What it moved to.</param>
public sealed record PriceChangedDomainEvent(
    PriceListEntryId EntryId,
    PriceListId PriceListId,
    PartRef PartId,
    decimal MinimumQuantity,
    decimal UnitPrice) : DomainEvent;
