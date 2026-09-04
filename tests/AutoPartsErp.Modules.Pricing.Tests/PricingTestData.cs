using AutoPartsErp.Modules.Pricing.Domain;
using AutoPartsErp.Modules.Pricing.Domain.PriceLists;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Pricing.Tests;

/// <summary>
/// The scaffolding these tests share. Deliberately thin: a helper that hides which break was set
/// or which kind a list is would hide the thing most of these tests are about.
/// </summary>
internal static class PricingTestData
{
    /// <summary>A fixed "today", so nothing here depends on when it is run.</summary>
    public static readonly DateOnly Today = new(2026, 9, 4);

    /// <summary>The part every test prices.</summary>
    public static readonly PartRef Pads = new(Guid.NewGuid());

    /// <summary>An amount in euros.</summary>
    public static Money Eur(decimal amount) => Money.Of(amount, Currency.Eur);

    /// <summary>A list that is already live.</summary>
    public static PriceList ActiveList(
        string code,
        PriceListKind kind,
        DateOnly? from = null,
        DateOnly? to = null)
    {
        PriceList list = PriceList.Open(code, code + " list", Currency.Eur, kind, from, to).Value;
        list.Activate(hasAnyPrice: true);
        list.ClearDomainEvents();
        return list;
    }

    /// <summary>A priced part, given as (minimum quantity, unit price) pairs in any order.</summary>
    public static PriceListEntry Entry(PriceList list, params (decimal Min, decimal Price)[] breaks)
    {
        PriceListEntry entry = PriceListEntry
            .Price(list.Id, Pads, breaks[0].Min, Eur(breaks[0].Price))
            .Value;

        foreach ((decimal min, decimal price) in breaks.Skip(1))
        {
            entry.SetBreak(min, Eur(price));
        }

        entry.ClearDomainEvents();
        return entry;
    }
}
