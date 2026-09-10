using AutoPartsErp.Modules.Sales.Domain;
using AutoPartsErp.Modules.Sales.Domain.Orders;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Sales.Tests;

/// <summary>
/// The least a line is allowed to make, and the half of it that refuses.
/// <para>
/// Recording a margin was the easy half. This is the half with an opinion: a price list that says
/// twenty per cent is a company decision, and a system that records the decision without enforcing
/// it has produced a report nobody acts on. These run the aggregate rather than the handler,
/// because the resolution of <i>which</i> floor applies belongs to Pricing and what happens once
/// there is one belongs here.
/// </para>
/// </summary>
public sealed class MarginFloorTests
{
    private static Money Eur(decimal amount) => Money.Of(amount, Currency.Eur);

    /// <summary>
    /// The line is refused and the order is left as it was. Half a line on a draft order is worse
    /// than no line, because nothing downstream is looking for it.
    /// </summary>
    [Fact]
    public void A_line_below_the_floor_never_reaches_the_order()
    {
        SalesOrder order = Fixture.NewDraft();

        Result<SalesOrderLineId> added = Add(order, price: 100m, cost: 85m, floor: 20m);

        added.Error.Code.Should().Be("sales.line.below_margin_floor");
        order.Lines.Should().BeEmpty();
    }

    /// <summary>
    /// A part sold on a core is refused on the goods, not on the pair — and neither line is left
    /// behind. A deposit with no part is a customer charged thirty euros for nothing.
    /// </summary>
    [Fact]
    public void A_core_part_refused_by_the_floor_leaves_no_deposit_behind()
    {
        SalesOrder order = Fixture.NewDraft();

        Result<SalesOrderLineId> added = order.AddLine(
            Fixture.NewPart(),
            "SM-900",
            "Starter motor",
            Quantity.Each(1),
            Eur(100m),
            discountPercent: 0m,
            vatRatePercent: 23m,
            priceSource: null,
            coreDeposit: Eur(30m),
            unitCost: Eur(85m),
            minimumMarginPercent: 20m);

        added.Error.Code.Should().Be("sales.line.below_margin_floor");
        order.Lines.Should().BeEmpty();
    }

    /// <summary>
    /// The discount is where a floor is usually breached, not the price. Somebody types 40% off a
    /// list price that was already thin, and the check has to see the figure after the discount.
    /// </summary>
    [Fact]
    public void The_floor_is_measured_after_the_discount()
    {
        SalesOrder order = Fixture.NewDraft();

        // 100.00 less nothing makes 15% against a cost of 85. Less 10% it makes 5.56%.
        Add(order, price: 100m, cost: 85m, floor: 5m).IsSuccess.Should().BeTrue();

        SalesOrderLineId lineId = order.Lines.Single().Id;

        order.ChangeLinePricing(lineId, Eur(100m), 10m, minimumMarginPercent: 5m)
            .IsSuccess.Should().BeTrue();

        order.ChangeLinePricing(lineId, Eur(100m), 20m, minimumMarginPercent: 5m)
            .Error.Code.Should().Be("sales.line.below_margin_floor");
    }

    /// <summary>
    /// A refused re-price leaves the line at the price it already had. Assigning first and
    /// refusing afterwards would leave a line carrying a figure the domain has just said no to,
    /// relying on nobody saving — a rule enforced by a habit rather than by the code.
    /// </summary>
    [Fact]
    public void A_refused_reprice_does_not_move_the_line()
    {
        SalesOrder order = Fixture.NewDraft();

        Add(order, price: 100m, cost: 85m, floor: 5m);

        SalesOrderLine line = order.Lines.Single();

        order.ChangeLinePricing(line.Id, Eur(88m), 0m, minimumMarginPercent: 5m)
            .Error.Code.Should().Be("sales.line.below_margin_floor");

        line.UnitPrice.Amount.Should().Be(100m);
        line.DiscountPercent.Should().Be(0m);
        line.PriceSource.Should().Be("TRADE");
    }

    /// <summary>
    /// The override is a record of one decision, not a permanent exemption. A flag that stayed set
    /// would let the next edit through unexamined, which is the opposite of what it is for.
    /// </summary>
    [Fact]
    public void Pricing_the_line_back_above_the_floor_forgets_the_override()
    {
        SalesOrder order = Fixture.NewDraft();

        Add(order, price: 100m, cost: 85m, floor: 20m, overrideFloor: true)
            .IsSuccess.Should().BeTrue();

        SalesOrderLine line = order.Lines.Single();
        line.MarginFloorOverridden.Should().BeTrue();

        order.ChangeLinePricing(line.Id, Eur(120m), 0m, minimumMarginPercent: 20m)
            .IsSuccess.Should().BeTrue();

        line.MarginFloorOverridden.Should().BeFalse();
    }

    /// <summary>
    /// A deposit is outside the margin, so it is outside the floor. Testing it would refuse every
    /// core part in the catalogue on a line that has no cost and was never meant to make anything.
    /// </summary>
    [Fact]
    public void A_core_deposit_is_never_measured_against_the_floor()
    {
        SalesOrder order = Fixture.NewDraft();

        order.AddLine(
            Fixture.NewPart(),
            "SM-900",
            "Starter motor",
            Quantity.Each(1),
            Eur(200m),
            discountPercent: 0m,
            vatRatePercent: 23m,
            priceSource: null,
            coreDeposit: Eur(30m),
            unitCost: Eur(100m),
            minimumMarginPercent: 40m).IsSuccess.Should().BeTrue();

        SalesOrderLine deposit = order.Lines.Single(line => line.IsCoreDeposit);

        deposit.HasMargin.Should().BeFalse();
        deposit.MarginFloorOverridden.Should().BeFalse();
    }

    /// <summary>
    /// A giveaway with a cost makes less than nothing, and no percentage exists to describe it.
    /// Comparing in money rather than in percentages is what lets the floor see it at all — a
    /// check written against <c>MarginPercent</c> would read null and wave it through.
    /// </summary>
    [Fact]
    public void A_line_given_away_at_a_cost_is_below_any_floor()
    {
        SalesOrder order = Fixture.NewDraft();

        Result<SalesOrderLineId> added = Add(order, price: 0m, cost: 40m, floor: 0m);

        added.Error.Code.Should().Be("sales.line.below_margin_floor");
        added.Error.Description.Should().Contain("given away");
    }

    /// <summary>
    /// A floor of zero says the company never sells below cost, and it says nothing about a line
    /// sold at exactly cost. That is a real thing a distributor does — matching a price to keep an
    /// account — and it is not what a floor of zero was written to stop.
    /// </summary>
    [Fact]
    public void A_floor_of_zero_allows_a_sale_at_cost_and_refuses_one_under_it()
    {
        SalesOrder order = Fixture.NewDraft();

        Add(order, price: 40m, cost: 40m, floor: 0m).IsSuccess.Should().BeTrue();

        SalesOrder other = Fixture.NewDraft();

        Add(other, price: 39.99m, cost: 40m, floor: 0m)
            .Error.Code.Should().Be("sales.line.below_margin_floor");
    }

    private static Result<SalesOrderLineId> Add(
        SalesOrder order,
        decimal price,
        decimal cost,
        decimal? floor,
        bool overrideFloor = false) =>
        order.AddLine(
            Fixture.NewPart(),
            "BP-1188",
            "Brake pad set, front axle",
            Quantity.Each(1),
            Eur(price),
            discountPercent: 0m,
            vatRatePercent: 23m,
            priceSource: "TRADE",
            coreDeposit: null,
            unitCost: Eur(cost),
            minimumMarginPercent: floor,
            overrideMarginFloor: overrideFloor);
}
