using AutoPartsErp.Modules.Pricing.Domain;
using AutoPartsErp.Modules.Pricing.Domain.PriceLists;
using AutoPartsErp.SharedKernel.ValueObjects;
using static AutoPartsErp.Modules.Pricing.Tests.PricingTestData;

namespace AutoPartsErp.Modules.Pricing.Tests;

/// <summary>
/// Quantity breaks. The arithmetic is trivial and the off-by-one is not: taking the first break
/// that matches instead of the highest one is how a customer buying fifty gets charged the price
/// of buying one, and it looks right in every test that only ever buys one.
/// </summary>
public sealed class PriceListEntryTests
{
    [Fact]
    public void A_break_has_to_start_above_zero()
    {
        PriceList list = ActiveList("TRADE", PriceListKind.Standard);

        PriceListEntry.Price(list.Id, Pads, 0m, Eur(24.50m))
            .Error.Code.Should().Be("pricing.break.minimum_not_positive");
    }

    /// <summary>
    /// Free of charge is a real price — a warranty replacement has to be quotable. Money going
    /// the other way is a credit note, which is a document and not a price list line.
    /// </summary>
    [Fact]
    public void Zero_is_a_price_and_below_zero_is_not()
    {
        PriceList list = ActiveList("TRADE", PriceListKind.Standard);

        PriceListEntry.Price(list.Id, Pads, 1m, Eur(0m)).IsSuccess.Should().BeTrue();
        PriceListEntry.Price(list.Id, Pads, 1m, Eur(-0.01m))
            .Error.Code.Should().Be("pricing.break.price_negative");
    }

    [Fact]
    public void Breaks_are_kept_sorted_however_they_were_entered()
    {
        PriceList list = ActiveList("TRADE", PriceListKind.Standard);
        PriceListEntry entry = Entry(list, (1m, 24.50m), (50m, 20m), (10m, 22m));

        entry.Breaks.Select(item => item.MinimumQuantity).Should().Equal(1m, 10m, 50m);
        entry.MinimumSaleQuantity.Should().Be(1m);
    }

    [Theory]
    [InlineData(1, 24.50)]
    [InlineData(9, 24.50)]
    [InlineData(10, 22)]
    [InlineData(49, 22)]
    [InlineData(50, 20)]
    [InlineData(500, 20)]
    public void The_price_is_the_highest_break_that_still_applies(decimal quantity, decimal expected)
    {
        PriceList list = ActiveList("TRADE", PriceListKind.Standard);
        PriceListEntry entry = Entry(list, (1m, 24.50m), (10m, 22m), (50m, 20m));

        entry.BreakFor(quantity)!.UnitPrice.Amount.Should().Be(expected);
    }

    /// <summary>
    /// "Ten or more is €22" is one intention. The caller should not have to know whether that
    /// break exists yet, and setting it twice is somebody correcting a figure, not a conflict.
    /// </summary>
    [Fact]
    public void Setting_a_break_that_already_exists_replaces_it()
    {
        PriceList list = ActiveList("TRADE", PriceListKind.Standard);
        PriceListEntry entry = Entry(list, (1m, 24.50m), (10m, 22m));

        entry.SetBreak(10m, Eur(21.50m)).IsSuccess.Should().BeTrue();

        entry.Breaks.Should().HaveCount(2);
        entry.BreakFor(10m)!.UnitPrice.Amount.Should().Be(21.50m);
        entry.DomainEvents.Should().ContainSingle();
    }

    [Fact]
    public void Every_break_has_to_be_in_the_lists_currency()
    {
        PriceList list = ActiveList("TRADE", PriceListKind.Standard);
        PriceListEntry entry = Entry(list, (1m, 24.50m));

        entry.SetBreak(10m, Money.Of(20m, Currency.Usd))
            .Error.Code.Should().Be("pricing.entry.currency_mismatch");
    }

    [Fact]
    public void A_break_that_is_not_there_cannot_be_removed()
    {
        PriceList list = ActiveList("TRADE", PriceListKind.Standard);
        PriceListEntry entry = Entry(list, (1m, 24.50m), (10m, 22m));

        entry.RemoveBreak(7m).Error.Code.Should().Be("pricing.entry.break_not_found");
        entry.RemoveBreak(10m).IsSuccess.Should().BeTrue();
        entry.Breaks.Should().ContainSingle();
    }

    /// <summary>
    /// An entry with no breaks cannot answer the question it exists to answer. Withdrawing a part
    /// from a list is deleting the entry, not emptying it.
    /// </summary>
    [Fact]
    public void The_last_break_cannot_be_removed()
    {
        PriceList list = ActiveList("TRADE", PriceListKind.Standard);
        PriceListEntry entry = Entry(list, (5m, 30m));

        entry.RemoveBreak(5m).Error.Code.Should().Be("pricing.entry.last_break");
    }

    /// <summary>
    /// "We do not sell fewer than five of these" is a normal thing for a distributor to say, and
    /// no price below the smallest break is how it gets said.
    /// </summary>
    [Fact]
    public void Below_the_smallest_break_there_is_no_price_at_all()
    {
        PriceList list = ActiveList("PACK", PriceListKind.Standard);
        PriceListEntry entry = Entry(list, (5m, 30m));

        entry.BreakFor(4m).Should().BeNull();
        entry.BreakFor(5m)!.UnitPrice.Amount.Should().Be(30m);
    }

    [Fact]
    public void There_is_a_ceiling_on_how_many_breaks_one_price_can_carry()
    {
        PriceList list = ActiveList("TRADE", PriceListKind.Standard);
        PriceListEntry entry = Entry(list, (1m, 10m));

        for (int quantity = 2; quantity <= PriceListEntry.MaxBreaks; quantity++)
        {
            entry.SetBreak(quantity, Eur(10m - (quantity * 0.1m))).IsSuccess.Should().BeTrue();
        }

        entry.Breaks.Should().HaveCount(PriceListEntry.MaxBreaks);
        entry.SetBreak(999m, Eur(1m)).Error.Code.Should().Be("pricing.entry.too_many_breaks");
    }
}
