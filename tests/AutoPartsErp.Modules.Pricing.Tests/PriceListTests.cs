using AutoPartsErp.Modules.Pricing.Domain.PriceLists;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;
using static AutoPartsErp.Modules.Pricing.Tests.PricingTestData;

namespace AutoPartsErp.Modules.Pricing.Tests;

/// <summary>
/// The rules that keep a price list from becoming something nobody can reason about: a promotion
/// that never ends, a default that expires, a live list with nothing in it.
/// </summary>
public sealed class PriceListTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_list_needs_a_code(string? code)
    {
        Result<PriceList> result = PriceList.Open(code, "Trade", Currency.Eur, PriceListKind.Standard);

        result.Error.Code.Should().Be("pricing.list.code_required");
    }

    [Fact]
    public void A_list_needs_a_name()
    {
        PriceList.Open("TRADE", " ", Currency.Eur, PriceListKind.Standard)
            .Error.Code.Should().Be("pricing.list.name_required");
    }

    [Fact]
    public void A_list_has_to_say_what_it_is_for()
    {
        PriceList.Open("TRADE", "Trade", Currency.Eur, PriceListKind.Unknown)
            .Error.Code.Should().Be("pricing.list.kind_required");
    }

    [Fact]
    public void The_code_is_normalized_so_trade_and_TRADE_cannot_both_exist()
    {
        PriceList.Open(" trade ", "Trade", Currency.Eur, PriceListKind.Standard)
            .Value.Code.Should().Be("TRADE");
    }

    [Fact]
    public void A_list_cannot_stop_applying_before_it_starts()
    {
        PriceList.Open(
                "TRADE",
                "Trade",
                Currency.Eur,
                PriceListKind.Standard,
                new DateOnly(2026, 5, 1),
                new DateOnly(2026, 4, 1))
            .Error.Code.Should().Be("pricing.list.period_inverted");
    }

    /// <summary>
    /// A promotion with no end date is a price change wearing a costume, and the costume is what
    /// stops anybody noticing it is still running in November.
    /// </summary>
    [Fact]
    public void A_promotion_needs_a_last_day()
    {
        PriceList.Open("FEB", "February", Currency.Eur, PriceListKind.Promotion)
            .Error.Code.Should().Be("pricing.list.promotion_needs_end");
    }

    [Fact]
    public void A_new_list_starts_in_draft_and_prices_nothing()
    {
        PriceList list = PriceList.Open("TRADE", "Trade", Currency.Eur, PriceListKind.Standard).Value;

        list.Status.Should().Be(PriceListStatus.Draft);
        list.IsEffectiveOn(Today).Should().BeFalse();
        list.DomainEvents.Should().ContainSingle();
    }

    [Fact]
    public void An_empty_list_cannot_go_live()
    {
        PriceList list = PriceList.Open("TRADE", "Trade", Currency.Eur, PriceListKind.Standard).Value;

        list.Activate(hasAnyPrice: false).Error.Code.Should().Be("pricing.list.no_prices");
        list.Status.Should().Be(PriceListStatus.Draft);
    }

    [Fact]
    public void A_live_list_prices_things_and_cannot_go_live_twice()
    {
        PriceList list = PriceList.Open("TRADE", "Trade", Currency.Eur, PriceListKind.Standard).Value;

        list.Activate(hasAnyPrice: true).IsSuccess.Should().BeTrue();
        list.IsEffectiveOn(Today).Should().BeTrue();
        list.Activate(hasAnyPrice: true).Error.Code.Should().Be("pricing.list.already_active");
    }

    /// <summary>
    /// Precedence is the whole reason a February campaign does not mean editing four hundred
    /// customer agreements and then editing them all back in March.
    /// </summary>
    [Fact]
    public void A_promotion_outranks_a_customer_list_which_outranks_the_standard_one()
    {
        int standard = ActiveList("TRADE", PriceListKind.Standard).Precedence;
        int customer = ActiveList("WHOLE", PriceListKind.Customer).Precedence;
        int promotion = ActiveList("SEP", PriceListKind.Promotion, null, Today.AddDays(10)).Precedence;

        standard.Should().BeLessThan(customer);
        customer.Should().BeLessThan(promotion);
    }

    [Fact]
    public void The_default_list_cannot_be_withdrawn_while_it_is_the_default()
    {
        PriceList list = ActiveList("TRADE", PriceListKind.Standard);
        list.MakeDefault().IsSuccess.Should().BeTrue();

        list.Archive().Error.Code.Should().Be("pricing.list.cannot_archive_default");

        list.ClearDefault();
        list.Archive().IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void A_withdrawn_list_prices_nothing_and_cannot_be_changed()
    {
        PriceList list = ActiveList("TRADE", PriceListKind.Standard);
        list.Archive();

        list.IsEffectiveOn(Today).Should().BeFalse();
        list.Amend("Trade 2026", null, null).Error.Code.Should().Be("pricing.list.archived");
    }

    [Fact]
    public void Only_a_live_standard_list_that_never_expires_can_be_the_default()
    {
        PriceList draft = PriceList.Open("D", "Draft", Currency.Eur, PriceListKind.Standard).Value;
        draft.MakeDefault().Error.Code.Should().Be("pricing.list.default_must_be_active");

        PriceList promotion = ActiveList("SEP", PriceListKind.Promotion, null, Today.AddDays(10));
        promotion.MakeDefault().Error.Code.Should().Be("pricing.list.default_must_be_standard");

        PriceList expiring = ActiveList("EXP", PriceListKind.Standard, null, Today.AddDays(30));
        expiring.MakeDefault().Error.Code.Should().Be("pricing.list.default_cannot_expire");
    }

    /// <summary>
    /// A campaign stops applying on its own. Nobody has to remember to turn it off, which is the
    /// only version of "remember to turn it off" that actually works.
    /// </summary>
    [Fact]
    public void A_promotion_applies_inside_its_window_and_nowhere_else()
    {
        PriceList promotion = ActiveList(
            "FEB", PriceListKind.Promotion, new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28));

        promotion.IsEffectiveOn(new DateOnly(2026, 1, 31)).Should().BeFalse();
        promotion.IsEffectiveOn(new DateOnly(2026, 2, 1)).Should().BeTrue();
        promotion.IsEffectiveOn(new DateOnly(2026, 2, 28)).Should().BeTrue();
        promotion.IsEffectiveOn(new DateOnly(2026, 3, 1)).Should().BeFalse();
    }
}
