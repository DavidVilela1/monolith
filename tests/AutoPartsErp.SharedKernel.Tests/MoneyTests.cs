using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.SharedKernel.Tests;

/// <summary>
/// Money is the type an ERP is least allowed to get wrong, so its rules are pinned down here
/// before anything is built on top of it.
/// </summary>
public sealed class MoneyTests
{
    [Fact]
    public void Rounds_to_the_currency_minor_units()
    {
        Money amount = Money.Of(12.3456m, Currency.Eur);

        amount.Amount.Should().Be(12.35m);
    }

    [Fact]
    public void Rounds_to_whole_units_for_currencies_without_minor_units()
    {
        Money amount = Money.Of(1234.56m, Currency.Jpy);

        amount.Amount.Should().Be(1235m);
    }

    [Fact]
    public void Uses_bankers_rounding_so_repeated_roundings_do_not_drift_upward()
    {
        Money.Of(2.125m, Currency.Eur).Amount.Should().Be(2.12m);
        Money.Of(2.135m, Currency.Eur).Amount.Should().Be(2.14m);
    }

    [Fact]
    public void Adds_amounts_of_the_same_currency()
    {
        Money total = Money.Of(10.50m, Currency.Eur) + Money.Of(4.25m, Currency.Eur);

        total.Amount.Should().Be(14.75m);
        total.Currency.Should().Be(Currency.Eur);
    }

    [Fact]
    public void Refuses_to_mix_currencies_rather_than_guessing_a_rate()
    {
        Money euros = Money.Of(10m, Currency.Eur);
        Money dollars = Money.Of(10m, Currency.Usd);

        Action mixing = () => _ = euros + dollars;

        mixing.Should().Throw<InvalidOperationException>()
            .WithMessage("*EUR*USD*");
    }

    [Fact]
    public void Applies_a_percentage_the_way_a_vat_line_does()
    {
        Money net = Money.Of(100m, Currency.Eur);

        net.Percentage(23m).Amount.Should().Be(23m);
    }

    [Fact]
    public void Refuses_to_divide_by_zero()
    {
        Action divide = () => _ = Money.Of(10m, Currency.Eur) / 0m;

        divide.Should().Throw<DivideByZeroException>();
    }

    [Fact]
    public void Compares_equal_when_amount_and_currency_match()
    {
        Money.Of(10m, Currency.Eur).Should().Be(Money.Of(10m, Currency.Eur));
        Money.Of(10m, Currency.Eur).Should().NotBe(Money.Of(10m, Currency.Usd));
    }

    [Fact]
    public void Orders_amounts_of_the_same_currency()
    {
        (Money.Of(10m, Currency.Eur) > Money.Of(9.99m, Currency.Eur)).Should().BeTrue();
        (Money.Of(10m, Currency.Eur) <= Money.Of(10m, Currency.Eur)).Should().BeTrue();
    }
}

/// <summary>Quantities carry their unit so that stock movements cannot be misread.</summary>
public sealed class QuantityTests
{
    [Fact]
    public void Rejects_a_fraction_of_a_discrete_unit()
    {
        Action half = () => Quantity.Of(2.5m, UnitOfMeasure.Each);

        half.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Allows_fractions_for_continuous_units()
    {
        Quantity oil = Quantity.Of(4.75m, UnitOfMeasure.Litre);

        oil.Value.Should().Be(4.75m);
    }

    [Fact]
    public void Refuses_to_combine_different_units()
    {
        Action mixing = () => _ = Quantity.Of(1m, UnitOfMeasure.Litre) + Quantity.Each(1);

        mixing.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Subtracts_within_the_same_unit()
    {
        Quantity remaining = Quantity.Each(10) - Quantity.Each(3);

        remaining.Value.Should().Be(7m);
        remaining.Unit.Should().Be(UnitOfMeasure.Each);
    }
}
