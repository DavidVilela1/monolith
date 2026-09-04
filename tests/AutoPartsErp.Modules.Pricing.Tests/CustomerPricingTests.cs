using AutoPartsErp.Modules.Pricing.Domain;
using AutoPartsErp.Modules.Pricing.Domain.Customers;
using AutoPartsErp.Modules.Pricing.Domain.PriceLists;
using static AutoPartsErp.Modules.Pricing.Tests.PricingTestData;

namespace AutoPartsErp.Modules.Pricing.Tests;

/// <summary>
/// What was agreed with one customer. The interesting part is what counts as a change worth
/// telling anybody about — an agreement re-saved with the same figures is somebody tidying a
/// note, and a price-change alert for that is how people learn to ignore price-change alerts.
/// </summary>
public sealed class CustomerPricingTests
{
    private static readonly CustomerRef Customer = new(Guid.NewGuid());

    [Fact]
    public void An_agreement_needs_a_customer_and_a_list()
    {
        PriceList trade = ActiveList("TRADE", PriceListKind.Standard);

        CustomerPricing.Agree(CustomerRef.Empty, trade.Id)
            .Error.Code.Should().Be("pricing.agreement.customer_required");
        CustomerPricing.Agree(Customer, PriceListId.Empty)
            .Error.Code.Should().Be("pricing.agreement.list_required");
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(100.01)]
    public void A_discount_outside_nought_to_a_hundred_is_not_a_discount(decimal percent)
    {
        PriceList trade = ActiveList("TRADE", PriceListKind.Standard);

        CustomerPricing.Agree(Customer, trade.Id, percent)
            .Error.Code.Should().Be("pricing.agreement.discount_range");
    }

    [Fact]
    public void An_open_ended_agreement_applies_from_the_day_it_is_made()
    {
        PriceList trade = ActiveList("TRADE", PriceListKind.Standard);
        CustomerPricing agreement = CustomerPricing
            .Agree(Customer, trade.Id, 5m, null, null, "  loyal since 2019  ").Value;

        agreement.IsEffectiveOn(Today).Should().BeTrue();
        agreement.Note.Should().Be("loyal since 2019");
        agreement.DomainEvents.Should().ContainSingle();
    }

    [Fact]
    public void Re_saving_the_same_terms_announces_nothing()
    {
        PriceList trade = ActiveList("TRADE", PriceListKind.Standard);
        CustomerPricing agreement = CustomerPricing.Agree(Customer, trade.Id, 5m).Value;
        agreement.ClearDomainEvents();

        agreement.Renegotiate(trade.Id, 5m, note: "tidied up").IsSuccess.Should().BeTrue();

        agreement.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Moving_a_customer_to_another_list_announces_where_they_came_from()
    {
        PriceList trade = ActiveList("TRADE", PriceListKind.Standard);
        PriceList wholesale = ActiveList("WHOLE", PriceListKind.Customer);
        CustomerPricing agreement = CustomerPricing.Agree(Customer, trade.Id, 5m).Value;
        agreement.ClearDomainEvents();

        agreement.Renegotiate(wholesale.Id, 7.5m).IsSuccess.Should().BeTrue();

        agreement.PriceListId.Should().Be(wholesale.Id);
        agreement.DiscountPercent.Should().Be(7.5m);

        var announced = agreement.DomainEvents
            .OfType<Domain.Customers.Events.CustomerPricingRenegotiatedDomainEvent>()
            .Single();

        announced.PreviousPriceListId.Should().Be(trade.Id);
        announced.PreviousDiscountPercent.Should().Be(5m);
    }

    [Fact]
    public void An_agreement_still_applies_on_its_last_day_and_not_the_day_after()
    {
        PriceList trade = ActiveList("TRADE", PriceListKind.Standard);
        CustomerPricing agreement = CustomerPricing.Agree(Customer, trade.Id, 5m).Value;

        agreement.End(Today).IsSuccess.Should().BeTrue();

        agreement.IsEffectiveOn(Today).Should().BeTrue();
        agreement.IsEffectiveOn(Today.AddDays(1)).Should().BeFalse();
        agreement.End(Today).Error.Code.Should().Be("pricing.agreement.already_ended");
    }

    [Fact]
    public void An_agreement_that_has_not_started_yet_does_not_apply_and_cannot_be_ended_early()
    {
        PriceList trade = ActiveList("TRADE", PriceListKind.Standard);
        CustomerPricing agreement = CustomerPricing
            .Agree(Customer, trade.Id, 5m, Today.AddDays(10)).Value;

        agreement.IsEffectiveOn(Today).Should().BeFalse();
        agreement.End(Today).Error.Code.Should().Be("pricing.agreement.end_before_start");
    }
}
