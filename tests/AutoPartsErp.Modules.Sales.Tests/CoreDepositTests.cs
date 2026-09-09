using AutoPartsErp.Modules.Sales.Domain;
using AutoPartsErp.Modules.Sales.Domain.Orders;
using AutoPartsErp.Modules.Sales.Domain.Orders.Events;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Sales.Tests;

/// <summary>
/// The deposit held against a returnable old unit.
/// <para>
/// A remanufactured starter motor is sold twice over: the part, and a sum held until the old one
/// comes back. The whole of this file is about keeping those two things attached to each other —
/// a deposit that drifts away from the part it belongs to is either money the company never took
/// or money it will be asked to give back and cannot account for.
/// </para>
/// <para>
/// It is a line, not a field. It prints as its own line, it is credited on its own, and a part
/// bought without returning the old one is a customer who paid for both — none of which a number
/// tucked onto the goods line can express.
/// </para>
/// </summary>
public sealed class CoreDepositTests
{
    private static Money Eur(decimal amount) => Money.Of(amount, Currency.Eur);

    [Fact]
    public void A_part_sold_on_a_core_gets_a_deposit_line_beside_it()
    {
        (SalesOrder order, SalesOrderLineId goodsId) = WithCore();

        order.Lines.Should().HaveCount(2);

        SalesOrderLine deposit = order.CoreDepositFor(goodsId)!;

        deposit.IsCoreDeposit.Should().BeTrue();
        deposit.CoreForLineId.Should().Be(goodsId);
        deposit.UnitPrice.Amount.Should().Be(30.00m);
        deposit.DiscountPercent.Should().Be(0m);
        deposit.Quantity.Value.Should().Be(2m);
    }

    /// <summary>
    /// One deposit per unit of the part, always. Two starter motors leave two old ones behind,
    /// and a deposit that stayed at one is money the company never took and will be asked to give
    /// back anyway.
    /// </summary>
    [Fact]
    public void The_deposit_follows_the_quantity_of_the_part()
    {
        (SalesOrder order, SalesOrderLineId goodsId) = WithCore();

        order.ChangeLineQuantity(goodsId, Quantity.Each(5)).IsSuccess.Should().BeTrue();

        order.CoreDepositFor(goodsId)!.Quantity.Value.Should().Be(5m);
    }

    /// <summary>
    /// A deposit is a sum held and given back unchanged. Discounting it would mean giving back
    /// more than was taken, and changing its quantity on its own would break the pairing that
    /// makes it mean anything.
    /// </summary>
    [Fact]
    public void A_deposit_is_not_priced_or_counted_on_its_own()
    {
        (SalesOrder order, SalesOrderLineId goodsId) = WithCore();
        SalesOrderLineId depositId = order.CoreDepositFor(goodsId)!.Id;

        order.ChangeLinePricing(depositId, Eur(10.00m), 50m)
            .Error.Code.Should().Be("sales.line.core_deposit_not_priceable");

        order.ChangeLineQuantity(depositId, Quantity.Each(9))
            .Error.Code.Should().Be("sales.line.core_deposit_follows_its_part");

        order.RemoveLine(depositId)
            .Error.Code.Should().Be("sales.line.core_deposit_not_removable");
    }

    /// <summary>An order with a deposit line and no part is a customer charged for nothing.</summary>
    [Fact]
    public void Removing_the_part_takes_its_deposit_with_it()
    {
        (SalesOrder order, SalesOrderLineId goodsId) = WithCore();

        order.RemoveLine(goodsId).IsSuccess.Should().BeTrue();

        order.Lines.Should().BeEmpty();
    }

    /// <summary>
    /// A deposit is money, not stock. Asking Inventory to hold thirty euros of starter motor
    /// against a line nobody will ever pick is how a shelf ends up with a reservation that cannot
    /// be fulfilled.
    /// </summary>
    [Fact]
    public void No_stock_is_ever_held_or_moved_for_a_deposit()
    {
        (SalesOrder order, SalesOrderLineId goodsId) = WithCore();

        order.Confirm(Fixture.Today).IsSuccess.Should().BeTrue();

        order.DomainEvents.OfType<StockReservationRequestedDomainEvent>()
            .Should().ContainSingle().Which.LineId.Should().Be(goodsId);

        order.ClearDomainEvents();

        SalesOrderLineId depositId = order.CoreDepositFor(goodsId)!.Id;

        order.DispatchLine(depositId, Quantity.Each(1))
            .Error.Code.Should().Be("sales.line.core_deposit_follows_its_part");
    }

    /// <summary>
    /// The deposit is charged as the part leaves. Without this it is never dispatched, never
    /// billable and therefore never invoiced — the company hands over a starter motor and forgets
    /// to charge the thirty euros.
    /// </summary>
    [Fact]
    public void Dispatching_the_part_charges_the_deposit_with_it()
    {
        (SalesOrder order, SalesOrderLineId goodsId) = WithCore();
        order.Confirm(Fixture.Today);
        order.ClearDomainEvents();

        order.DispatchLine(goodsId, Quantity.Each(2)).IsSuccess.Should().BeTrue();

        SalesOrderLine deposit = order.CoreDepositFor(goodsId)!;

        deposit.DispatchedQuantity.Value.Should().Be(2m);
        deposit.IsBillable.Should().BeTrue();

        // One dispatch event, for the goods. There is nothing on a shelf to take the deposit off.
        order.DomainEvents.OfType<GoodsDispatchedDomainEvent>().Should().ContainSingle();
    }

    /// <summary>The customer pays for the part and the deposit, and the totals say so.</summary>
    [Fact]
    public void The_order_is_worth_the_part_plus_the_deposit()
    {
        (SalesOrder order, _) = WithCore();

        // Two at 120.00 and two at 30.00, both at 23%.
        order.NetTotal.Amount.Should().Be(300.00m);
        order.VatTotal.Amount.Should().Be(69.00m);
        order.GrossTotal.Amount.Should().Be(369.00m);
    }

    [Fact]
    public void A_deposit_has_to_be_worth_something_and_in_the_orders_currency()
    {
        SalesOrder order = Fixture.NewDraft();

        order.AddLine(
            Fixture.NewPart(), "SM-900", "Starter motor", Quantity.Each(1),
            Eur(120.00m), 0m, 23m, null, Eur(0m))
            .Error.Code.Should().Be("sales.line.core_deposit_not_positive");

        order.AddLine(
            Fixture.NewPart(), "SM-900", "Starter motor", Quantity.Each(1),
            Eur(120.00m), 0m, 23m, null, Money.Of(30.00m, Currency.Usd))
            .Error.Code.Should().Be("sales.line.currency_mismatch");

        // Neither attempt left half a pair behind.
        order.Lines.Should().BeEmpty();
    }

    /// <summary>Two starter motors on one order are still one part twice, deposit or not.</summary>
    [Fact]
    public void The_deposit_does_not_make_the_part_look_like_a_duplicate()
    {
        SalesOrder order = Fixture.NewDraft();
        PartRef starter = Fixture.NewPart();

        order.AddLine(
            starter, "SM-900", "Starter motor", Quantity.Each(1),
            Eur(120.00m), 0m, 23m, null, Eur(30.00m)).IsSuccess.Should().BeTrue();

        order.AddLine(
            starter, "SM-900", "Starter motor", Quantity.Each(1),
            Eur(120.00m), 0m, 23m, null, Eur(30.00m))
            .Error.Code.Should().Be("sales.line.duplicate_part");
    }

    /// <summary>A starter motor at 120.00 with a 30.00 deposit, two of them.</summary>
    private static (SalesOrder Order, SalesOrderLineId GoodsId) WithCore()
    {
        SalesOrder order = Fixture.NewDraft();

        SalesOrderLineId goodsId = order.AddLine(
            Fixture.NewPart(),
            "SM-900",
            "Starter motor",
            Quantity.Each(2),
            Eur(120.00m),
            discountPercent: 0m,
            vatRatePercent: 23m,
            priceSource: null,
            coreDeposit: Eur(30.00m)).Value;

        return (order, goodsId);
    }
}
