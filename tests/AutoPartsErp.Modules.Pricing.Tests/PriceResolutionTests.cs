using AutoPartsErp.Modules.Pricing.Domain;
using AutoPartsErp.Modules.Pricing.Domain.Customers;
using AutoPartsErp.Modules.Pricing.Domain.PriceLists;
using AutoPartsErp.Modules.Pricing.Domain.Quotes;
using AutoPartsErp.SharedKernel.Results;
using static AutoPartsErp.Modules.Pricing.Tests.PricingTestData;

namespace AutoPartsErp.Modules.Pricing.Tests;

/// <summary>
/// Which price wins. These are the rules that get argued about three weeks after the invoice, so
/// they run without a database, a customer record or a clock — hand-built objects and one date.
/// </summary>
public sealed class PriceResolutionTests
{
    private static readonly CustomerRef Customer = new(Guid.NewGuid());

    [Fact]
    public void A_walk_in_customer_pays_the_standard_price()
    {
        PriceCandidate standard = Standard();

        Result<PriceQuote> quote = PriceResolution.Resolve([standard], agreement: null, 1m, Today);

        quote.IsSuccess.Should().BeTrue();
        quote.Value.NetUnitPrice.Amount.Should().Be(24.50m);
        quote.Value.IsDiscounted.Should().BeFalse();
        quote.Value.PriceListCode.Should().Be("TRADE");
        quote.Value.AppliedBreakQuantity.Should().Be(1m);
    }

    [Fact]
    public void Buying_fifty_takes_the_fifty_up_price()
    {
        Result<PriceQuote> quote = PriceResolution.Resolve([Standard()], agreement: null, 50m, Today);

        quote.Value.GrossUnitPrice.Amount.Should().Be(20m);
        quote.Value.AppliedBreakQuantity.Should().Be(50m);
    }

    [Fact]
    public void A_customers_own_list_beats_the_standard_one()
    {
        PriceList theirs = ActiveList("WHOLE", PriceListKind.Customer);
        var theirCandidate = new PriceCandidate(theirs, Entry(theirs, (1m, 21m)));
        CustomerPricing agreement = CustomerPricing.Agree(Customer, theirs.Id, 5m).Value;

        Result<PriceQuote> quote = PriceResolution.Resolve(
            [Standard(), theirCandidate], agreement, 1m, Today);

        quote.Value.PriceListCode.Should().Be("WHOLE");
        quote.Value.GrossUnitPrice.Amount.Should().Be(21m);
        quote.Value.NetUnitPrice.Amount.Should().Be(19.95m);
    }

    /// <summary>
    /// Five percent off the fifty-up price, not off the price of one. Both orderings are
    /// defensible and only one of them is what everybody assumes.
    /// </summary>
    [Fact]
    public void The_discount_comes_off_after_the_quantity_break()
    {
        PriceList trade = ActiveList("TRADE", PriceListKind.Standard);
        CustomerPricing agreement = CustomerPricing.Agree(Customer, trade.Id, 5m).Value;

        Result<PriceQuote> quote = PriceResolution.Resolve([Standard()], agreement, 50m, Today);

        quote.Value.GrossUnitPrice.Amount.Should().Be(20m);
        quote.Value.NetUnitPrice.Amount.Should().Be(19m);
    }

    /// <summary>
    /// Somebody else's negotiated list must never reach this customer, however cheap it is. This
    /// is the one that would show up on the wrong invoice.
    /// </summary>
    [Fact]
    public void Another_customers_list_is_never_used()
    {
        PriceList theirs = ActiveList("WHOLE", PriceListKind.Customer);
        PriceList somebodyElses = ActiveList("FLEET", PriceListKind.Customer);
        var elsewhere = new PriceCandidate(somebodyElses, Entry(somebodyElses, (1m, 12m)));
        CustomerPricing agreement = CustomerPricing.Agree(Customer, theirs.Id, 5m).Value;

        Result<PriceQuote> quote = PriceResolution.Resolve(
            [Standard(), elsewhere], agreement, 1m, Today);

        quote.Value.PriceListCode.Should().Be("TRADE");
    }

    [Fact]
    public void A_customer_list_is_ignored_for_somebody_with_no_agreement()
    {
        PriceList theirs = ActiveList("WHOLE", PriceListKind.Customer);
        var theirCandidate = new PriceCandidate(theirs, Entry(theirs, (1m, 21m)));

        Result<PriceQuote> quote = PriceResolution.Resolve(
            [Standard(), theirCandidate], agreement: null, 1m, Today);

        quote.Value.PriceListCode.Should().Be("TRADE");
    }

    /// <summary>
    /// A promotion applies because it is a promotion, not because it came out lower. Ranking
    /// before comparing prices is what makes that true even when the campaign price is dearer
    /// than a particular customer's negotiated one.
    /// </summary>
    [Fact]
    public void A_promotion_beats_a_customer_list_even_when_it_is_dearer()
    {
        PriceList theirs = ActiveList("WHOLE", PriceListKind.Customer);
        PriceList promotion = ActiveList("SEP", PriceListKind.Promotion, null, Today.AddDays(20));
        var theirCandidate = new PriceCandidate(theirs, Entry(theirs, (1m, 21m)));
        var promoted = new PriceCandidate(promotion, Entry(promotion, (1m, 23m)));
        CustomerPricing agreement = CustomerPricing.Agree(Customer, theirs.Id, 5m).Value;

        Result<PriceQuote> quote = PriceResolution.Resolve(
            [Standard(), theirCandidate, promoted], agreement, 1m, Today);

        quote.Value.PriceListCode.Should().Be("SEP");
        quote.Value.NetUnitPrice.Amount.Should().Be(21.85m);
    }

    [Fact]
    public void An_expired_promotion_is_ignored_without_anybody_turning_it_off()
    {
        PriceList expired = ActiveList("FEB", PriceListKind.Promotion, null, new DateOnly(2026, 2, 28));
        var stale = new PriceCandidate(expired, Entry(expired, (1m, 5m)));

        Result<PriceQuote> quote = PriceResolution.Resolve([Standard(), stale], null, 1m, Today);

        quote.Value.PriceListCode.Should().Be("TRADE");
    }

    [Fact]
    public void Where_two_lists_rank_the_same_the_customer_gets_the_cheaper_one()
    {
        PriceList other = ActiveList("TRADE2", PriceListKind.Standard);
        var cheaper = new PriceCandidate(other, Entry(other, (1m, 23m)));

        Result<PriceQuote> quote = PriceResolution.Resolve([Standard(), cheaper], null, 1m, Today);

        quote.Value.PriceListCode.Should().Be("TRADE2");
    }

    /// <summary>
    /// "Priced, but not at that quantity" is a different answer from "not priced", and sending
    /// somebody to look for a missing price list that is right there wastes an afternoon.
    /// </summary>
    [Fact]
    public void Asking_for_fewer_than_the_pack_says_so_and_names_the_pack()
    {
        PriceList packOnly = ActiveList("PACK", PriceListKind.Standard);
        var pack = new PriceCandidate(packOnly, Entry(packOnly, (5m, 30m)));

        Result<PriceQuote> quote = PriceResolution.Resolve([pack], null, 2m, Today);

        quote.Error.Code.Should().Be("pricing.quote.below_minimum");
        quote.Error.Description.Should().Contain("5");
    }

    [Fact]
    public void Nothing_to_price_from_is_reported_as_no_price()
    {
        PriceResolution.Resolve([], null, 1m, Today)
            .Error.Code.Should().Be("pricing.quote.no_price");
    }

    [Fact]
    public void An_expired_agreement_falls_back_to_the_standard_list_at_full_price()
    {
        PriceList theirs = ActiveList("WHOLE", PriceListKind.Customer);
        var theirCandidate = new PriceCandidate(theirs, Entry(theirs, (1m, 21m)));
        CustomerPricing lapsed = CustomerPricing.Agree(Customer, theirs.Id, 5m).Value;
        lapsed.End(Today.AddDays(-1));

        Result<PriceQuote> quote = PriceResolution.Resolve(
            [Standard(), theirCandidate], lapsed, 1m, Today);

        quote.Value.PriceListCode.Should().Be("TRADE");
        quote.Value.IsDiscounted.Should().BeFalse();
    }

    private static PriceCandidate Standard()
    {
        PriceList trade = ActiveList("TRADE", PriceListKind.Standard);
        return new PriceCandidate(trade, Entry(trade, (1m, 24.50m), (10m, 22m), (50m, 20m)));
    }
}
