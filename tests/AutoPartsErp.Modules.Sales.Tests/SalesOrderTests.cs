using AutoPartsErp.Modules.Sales.Domain;
using AutoPartsErp.Modules.Sales.Domain.Orders;
using AutoPartsErp.Modules.Sales.Domain.Orders.Events;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Sales.Tests;

/// <summary>Shared setup, so each test says only what it is about.</summary>
internal static class Fixture
{
    public static readonly DateOnly Today = new(2026, 9, 2);
    public static readonly CustomerRef Customer = new(Guid.NewGuid());
    public static readonly WarehouseRef Warehouse = new(Guid.NewGuid());

    public static PartRef NewPart() => new(Guid.NewGuid());

    public static SalesOrder NewDraft(
        SalesOrderKind kind = SalesOrderKind.Order,
        string number = "SO-2026-01188") =>
        SalesOrder.Draft(number, kind, Customer, "WKSP", "Workshop Lda", Warehouse, Currency.Eur).Value;

    /// <summary>A draft with one line: <paramref name="quantity"/> at 24.50, 10% off, 23% VAT.</summary>
    public static (SalesOrder Order, SalesOrderLineId LineId) DraftWithLine(
        int quantity = 10,
        SalesOrderKind kind = SalesOrderKind.Order)
    {
        SalesOrder order = NewDraft(kind);
        SalesOrderLineId lineId = order
            .AddLine(
                NewPart(), "BP-1188", "Brake pad set", Quantity.Each(quantity),
                Money.Of(24.50m, Currency.Eur), 10m, 23m)
            .Value;

        return (order, lineId);
    }

    /// <summary>A confirmed order with one line, ready to dispatch.</summary>
    public static (SalesOrder Order, SalesOrderLineId LineId) ConfirmedWithLine(int quantity = 10)
    {
        (SalesOrder order, SalesOrderLineId lineId) = DraftWithLine(quantity);
        order.Confirm(Today);
        order.ClearDomainEvents();

        return (order, lineId);
    }
}

/// <summary>
/// The money. Extend, discount, net, VAT — in that order, rounding at each step.
/// <para>
/// These are the numbers that end up on a Portuguese invoice, so they are asserted exactly
/// rather than approximately. The VAT figures are deliberately chosen to land on a midpoint,
/// because banker's rounding is what <c>Money</c> does and it is not what most people expect.
/// </para>
/// </summary>
public sealed class SalesOrderMoneyTests
{
    [Fact]
    public void A_line_extends_discounts_and_taxes_in_order()
    {
        (SalesOrder order, _) = Fixture.DraftWithLine(10);
        SalesOrderLine line = order.Lines.Single();

        line.ExtendedPrice.Amount.Should().Be(245.00m);
        line.DiscountAmount.Amount.Should().Be(24.50m);
        line.NetTotal.Amount.Should().Be(220.50m);
        line.VatAmount.Amount.Should().Be(50.72m);
        line.GrossTotal.Amount.Should().Be(271.22m);
    }

    [Fact]
    public void Vat_on_a_midpoint_rounds_to_even()
    {
        SalesOrder order = Fixture.NewDraft();

        // 21.75 at 6% is 1.3050 exactly. To even, that is 1.30 - not 1.31.
        order.AddLine(
            Fixture.NewPart(), "OF-3", "Oil filter", Quantity.Each(3),
            Money.Of(7.25m, Currency.Eur), 0m, 6m);

        SalesOrderLine line = order.Lines.Single();

        line.NetTotal.Amount.Should().Be(21.75m);
        line.VatAmount.Amount.Should().Be(1.30m);
    }

    [Fact]
    public void Order_totals_are_the_sum_of_the_lines()
    {
        SalesOrder order = Fixture.NewDraft();
        order.AddLine(
            Fixture.NewPart(), "BP-1188", "Brake pad set", Quantity.Each(10),
            Money.Of(24.50m, Currency.Eur), 10m, 23m);
        order.AddLine(
            Fixture.NewPart(), "OF-3", "Oil filter", Quantity.Each(3),
            Money.Of(7.25m, Currency.Eur), 0m, 6m);

        order.NetTotal.Amount.Should().Be(242.25m);
        order.VatTotal.Amount.Should().Be(52.02m);
        order.GrossTotal.Amount.Should().Be(294.27m);
    }

    [Fact]
    public void A_full_discount_leaves_nothing_to_tax()
    {
        SalesOrder order = Fixture.NewDraft();
        order.AddLine(
            Fixture.NewPart(), "GW-1", "Goodwill replacement", Quantity.Each(2),
            Money.Of(50m, Currency.Eur), 100m, 23m);

        SalesOrderLine line = order.Lines.Single();

        line.NetTotal.IsZero.Should().BeTrue();
        line.VatAmount.IsZero.Should().BeTrue();
        line.GrossTotal.IsZero.Should().BeTrue();
    }

    [Fact]
    public void A_zero_rated_line_carries_no_vat()
    {
        SalesOrder order = Fixture.NewDraft();
        order.AddLine(
            Fixture.NewPart(), "EX-1", "Export", Quantity.Each(1),
            Money.Of(100m, Currency.Eur), 0m, 0m);

        order.VatTotal.IsZero.Should().BeTrue();
        order.GrossTotal.Amount.Should().Be(100m);
    }

    [Fact]
    public void An_empty_order_is_worth_nothing_in_its_own_currency()
    {
        SalesOrder order = Fixture.NewDraft();

        order.NetTotal.IsZero.Should().BeTrue();
        order.GrossTotal.Currency.Should().Be(Currency.Eur);
    }
}

/// <summary>Building the document.</summary>
public sealed class SalesOrderDraftingTests
{
    [Fact]
    public void A_new_order_starts_as_an_editable_draft()
    {
        SalesOrder order = Fixture.NewDraft();

        order.Status.Should().Be(SalesOrderStatus.Draft);
        order.IsEditable.Should().BeTrue();
        order.CanDispatch.Should().BeFalse();
        order.IsClosed.Should().BeFalse();
        order.DomainEvents.OfType<SalesOrderDraftedDomainEvent>().Should().ContainSingle();
    }

    [Fact]
    public void The_number_and_customer_code_are_normalised()
    {
        SalesOrder order = SalesOrder
            .Draft(" so-2026-01188 ", SalesOrderKind.Order, Fixture.Customer, " wksp ",
                " Workshop Lda ", Fixture.Warehouse, Currency.Eur)
            .Value;

        order.OrderNumber.Should().Be("SO-2026-01188");
        order.CustomerCode.Should().Be("WKSP");
        order.CustomerName.Should().Be("Workshop Lda");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void An_order_needs_a_number(string? number)
    {
        SalesOrder
            .Draft(number, SalesOrderKind.Order, Fixture.Customer, "WKSP", "Workshop Lda",
                Fixture.Warehouse, Currency.Eur)
            .Error.Code.Should().Be("sales.order.number_required");
    }

    [Fact]
    public void An_order_needs_a_customer()
    {
        SalesOrder
            .Draft("SO-1", SalesOrderKind.Order, CustomerRef.Empty, "WKSP", "Workshop Lda",
                Fixture.Warehouse, Currency.Eur)
            .Error.Code.Should().Be("sales.order.customer_required");
    }

    [Fact]
    public void An_order_needs_somewhere_to_ship_from()
    {
        SalesOrder
            .Draft("SO-1", SalesOrderKind.Order, Fixture.Customer, "WKSP", "Workshop Lda",
                WarehouseRef.Empty, Currency.Eur)
            .Error.Code.Should().Be("sales.order.warehouse_required");
    }

    [Fact]
    public void A_counter_sale_puts_no_credit_at_risk()
    {
        Fixture.NewDraft(SalesOrderKind.CounterSale).ConsumesCredit.Should().BeFalse();
        Fixture.NewDraft().ConsumesCredit.Should().BeTrue();
    }

    [Fact]
    public void The_same_part_cannot_be_added_twice()
    {
        SalesOrder order = Fixture.NewDraft();
        PartRef part = Fixture.NewPart();
        order.AddLine(part, "A", "Brake pads", Quantity.Each(10), Money.Of(24.50m, Currency.Eur));

        order.AddLine(part, "A", "Brake pads", Quantity.Each(5), Money.Of(24.50m, Currency.Eur))
            .Error.Code.Should().Be("sales.line.duplicate_part");
    }

    [Fact]
    public void A_line_must_be_priced_in_the_orders_currency()
    {
        Fixture.NewDraft()
            .AddLine(Fixture.NewPart(), "A", "x", Quantity.Each(1), Money.Of(1m, Currency.Usd))
            .Error.Code.Should().Be("sales.line.currency_mismatch");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void A_discount_outside_nought_to_a_hundred_is_refused(decimal discount)
    {
        Fixture.NewDraft()
            .AddLine(Fixture.NewPart(), "A", "x", Quantity.Each(1), Money.Of(1m, Currency.Eur), discount)
            .Error.Code.Should().Be("sales.line.discount_range");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void A_vat_rate_outside_nought_to_a_hundred_is_refused(decimal rate)
    {
        Fixture.NewDraft()
            .AddLine(Fixture.NewPart(), "A", "x", Quantity.Each(1), Money.Of(1m, Currency.Eur), 0m, rate)
            .Error.Code.Should().Be("sales.line.vat_rate_range");
    }

    [Fact]
    public void Selling_nothing_is_not_selling()
    {
        Fixture.NewDraft()
            .AddLine(Fixture.NewPart(), "A", "x", Quantity.Each(0), Money.Of(1m, Currency.Eur))
            .Error.Code.Should().Be("sales.line.quantity_not_positive");
    }

    [Fact]
    public void A_line_can_be_repriced_and_requantified_on_a_draft()
    {
        (SalesOrder order, SalesOrderLineId lineId) = Fixture.DraftWithLine(10);

        order.ChangeLineQuantity(lineId, Quantity.Each(4)).IsSuccess.Should().BeTrue();
        order.ChangeLinePricing(lineId, Money.Of(30m, Currency.Eur), 0m).IsSuccess.Should().BeTrue();

        order.NetTotal.Amount.Should().Be(120m);
    }

    [Fact]
    public void A_line_can_be_removed_from_a_draft()
    {
        (SalesOrder order, SalesOrderLineId lineId) = Fixture.DraftWithLine();

        order.RemoveLine(lineId).IsSuccess.Should().BeTrue();
        order.Lines.Should().BeEmpty();
    }

    [Fact]
    public void Changing_a_line_that_is_not_there_is_a_not_found()
    {
        (SalesOrder order, _) = Fixture.DraftWithLine();

        order.ChangeLineQuantity(SalesOrderLineId.New(), Quantity.Each(1))
            .Error.Code.Should().Be("sales.line.not_found");
    }
}

/// <summary>Confirming: the point at which the customer has been quoted a figure.</summary>
public sealed class SalesOrderConfirmationTests
{
    [Fact]
    public void An_empty_order_cannot_be_confirmed()
    {
        Fixture.NewDraft().Confirm(Fixture.Today).Error.Code.Should().Be("sales.order.no_lines");
    }

    [Fact]
    public void Confirming_records_the_date_and_moves_the_status()
    {
        (SalesOrder order, _) = Fixture.DraftWithLine();

        order.Confirm(Fixture.Today, new DateOnly(2026, 9, 10)).IsSuccess.Should().BeTrue();

        order.Status.Should().Be(SalesOrderStatus.Confirmed);
        order.ConfirmedOn.Should().Be(Fixture.Today);
        order.RequiredBy.Should().Be(new DateOnly(2026, 9, 10));
        order.CanDispatch.Should().BeTrue();
    }

    [Fact]
    public void The_confirmed_event_carries_all_three_totals()
    {
        (SalesOrder order, _) = Fixture.DraftWithLine(10);

        order.Confirm(Fixture.Today);

        SalesOrderConfirmedDomainEvent confirmed =
            order.DomainEvents.OfType<SalesOrderConfirmedDomainEvent>().Single();

        confirmed.NetTotal.Should().Be(220.50m);
        confirmed.VatTotal.Should().Be(50.72m);
        confirmed.GrossTotal.Should().Be(271.22m);
        confirmed.CurrencyCode.Should().Be("EUR");
    }

    [Fact]
    public void Confirming_asks_for_stock_to_be_held_one_line_at_a_time()
    {
        SalesOrder order = Fixture.NewDraft();
        order.AddLine(Fixture.NewPart(), "A", "x", Quantity.Each(2), Money.Of(10m, Currency.Eur));
        order.AddLine(Fixture.NewPart(), "B", "y", Quantity.Each(3), Money.Of(10m, Currency.Eur));
        order.ClearDomainEvents();

        order.Confirm(Fixture.Today);

        order.DomainEvents.OfType<StockReservationRequestedDomainEvent>().Should().HaveCount(2);
    }

    [Fact]
    public void A_required_date_in_the_past_is_refused()
    {
        (SalesOrder order, _) = Fixture.DraftWithLine();

        order.Confirm(Fixture.Today, new DateOnly(2026, 9, 1))
            .Error.Code.Should().Be("sales.order.required_date_past");
    }

    [Fact]
    public void A_confirmed_order_can_no_longer_be_edited()
    {
        (SalesOrder order, SalesOrderLineId lineId) = Fixture.ConfirmedWithLine();

        order.IsEditable.Should().BeFalse();
        order.AddLine(Fixture.NewPart(), "B", "y", Quantity.Each(1), Money.Of(1m, Currency.Eur))
            .Error.Code.Should().Be("sales.order.not_editable");
        order.RemoveLine(lineId).Error.Code.Should().Be("sales.order.not_editable");
    }

    [Fact]
    public void An_order_cannot_be_confirmed_twice()
    {
        (SalesOrder order, _) = Fixture.ConfirmedWithLine();

        order.Confirm(Fixture.Today).Error.Code.Should().Be("sales.order.already_confirmed");
    }

    [Fact]
    public void Goods_cannot_go_out_against_a_draft()
    {
        (SalesOrder order, SalesOrderLineId lineId) = Fixture.DraftWithLine();

        order.DispatchLine(lineId, Quantity.Each(1))
            .Error.Code.Should().Be("sales.order.not_dispatchable");
    }
}

/// <summary>Dispatch: the part where goods actually leave.</summary>
public sealed class SalesOrderDispatchTests
{
    [Fact]
    public void A_partial_dispatch_leaves_the_order_partially_dispatched()
    {
        (SalesOrder order, SalesOrderLineId lineId) = Fixture.ConfirmedWithLine(10);

        order.DispatchLine(lineId, Quantity.Each(4)).IsSuccess.Should().BeTrue();

        order.Status.Should().Be(SalesOrderStatus.PartiallyDispatched);
        order.Lines.Single().OutstandingQuantity.Value.Should().Be(6m);
        order.HasOutstandingLines.Should().BeTrue();
    }

    [Fact]
    public void The_dispatch_event_carries_this_delivery_not_the_running_total()
    {
        (SalesOrder order, SalesOrderLineId lineId) = Fixture.ConfirmedWithLine(10);
        order.DispatchLine(lineId, Quantity.Each(4));
        order.ClearDomainEvents();

        order.DispatchLine(lineId, Quantity.Each(6));

        GoodsDispatchedDomainEvent dispatched =
            order.DomainEvents.OfType<GoodsDispatchedDomainEvent>().Single();

        dispatched.Quantity.Should().Be(6m);
        dispatched.UnitCode.Should().Be("EA");
        dispatched.WarehouseId.Should().Be(Fixture.Warehouse);
        dispatched.LineId.Should().Be(lineId);
    }

    [Fact]
    public void Dispatching_everything_completes_the_order()
    {
        (SalesOrder order, SalesOrderLineId lineId) = Fixture.ConfirmedWithLine(10);

        order.DispatchLine(lineId, Quantity.Each(10)).IsSuccess.Should().BeTrue();

        order.Status.Should().Be(SalesOrderStatus.Dispatched);
        order.IsClosed.Should().BeTrue();
        order.HasOutstandingLines.Should().BeFalse();
        order.DomainEvents.OfType<SalesOrderCompletedDomainEvent>().Should().ContainSingle();
    }

    [Fact]
    public void An_order_is_only_complete_when_every_line_is()
    {
        SalesOrder order = Fixture.NewDraft();
        SalesOrderLineId first = order
            .AddLine(Fixture.NewPart(), "A", "x", Quantity.Each(2), Money.Of(10m, Currency.Eur)).Value;
        order.AddLine(Fixture.NewPart(), "B", "y", Quantity.Each(3), Money.Of(10m, Currency.Eur));
        order.Confirm(Fixture.Today);

        order.DispatchLine(first, Quantity.Each(2));

        order.Status.Should().Be(SalesOrderStatus.PartiallyDispatched);
        order.DomainEvents.OfType<SalesOrderCompletedDomainEvent>().Should().BeEmpty();
    }

    [Fact]
    public void More_than_was_sold_cannot_go_out()
    {
        (SalesOrder order, SalesOrderLineId lineId) = Fixture.ConfirmedWithLine(10);

        order.DispatchLine(lineId, Quantity.Each(11))
            .Error.Code.Should().Be("sales.line.over_dispatch");
    }

    [Fact]
    public void Over_dispatch_is_measured_against_what_is_still_owed()
    {
        (SalesOrder order, SalesOrderLineId lineId) = Fixture.ConfirmedWithLine(10);
        order.DispatchLine(lineId, Quantity.Each(8));

        order.DispatchLine(lineId, Quantity.Each(3))
            .Error.Code.Should().Be("sales.line.over_dispatch");
    }

    [Fact]
    public void Dispatching_nothing_is_not_dispatching()
    {
        (SalesOrder order, SalesOrderLineId lineId) = Fixture.ConfirmedWithLine();

        order.DispatchLine(lineId, Quantity.Each(0))
            .Error.Code.Should().Be("sales.line.dispatch_not_positive");
    }

    [Fact]
    public void A_quantity_in_the_wrong_unit_is_refused()
    {
        (SalesOrder order, SalesOrderLineId lineId) = Fixture.ConfirmedWithLine(10);

        order.DispatchLine(lineId, Quantity.Of(5m, UnitOfMeasure.Litre))
            .Error.Code.Should().Be("sales.line.unit_mismatch");
    }

    [Fact]
    public void A_completed_order_takes_no_more_dispatches()
    {
        (SalesOrder order, SalesOrderLineId lineId) = Fixture.ConfirmedWithLine(10);
        order.DispatchLine(lineId, Quantity.Each(10));

        order.DispatchLine(lineId, Quantity.Each(1))
            .Error.Code.Should().Be("sales.order.already_closed");
    }
}

/// <summary>Cancelling, and the line it cannot be used to cross.</summary>
public sealed class SalesOrderCancellationTests
{
    [Fact]
    public void An_order_can_be_cancelled_before_anything_goes_out()
    {
        (SalesOrder order, _) = Fixture.ConfirmedWithLine();

        order.Cancel("Customer changed their mind.").IsSuccess.Should().BeTrue();

        order.Status.Should().Be(SalesOrderStatus.Cancelled);
        order.IsClosed.Should().BeTrue();
        order.ClosureReason.Should().Be("Customer changed their mind.");
        order.DomainEvents.OfType<SalesOrderCancelledDomainEvent>().Should().ContainSingle();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void A_cancellation_needs_an_explanation(string? reason)
    {
        (SalesOrder order, _) = Fixture.ConfirmedWithLine();

        order.Cancel(reason).Error.Code.Should().Be("sales.order.cancel_reason_required");
    }

    [Fact]
    public void An_order_with_goods_already_out_cannot_be_cancelled()
    {
        (SalesOrder order, SalesOrderLineId lineId) = Fixture.ConfirmedWithLine(10);
        order.DispatchLine(lineId, Quantity.Each(4));

        order.Cancel("Changed our mind.")
            .Error.Code.Should().Be("sales.order.cannot_cancel_after_dispatch");
    }

    [Fact]
    public void An_order_cannot_be_cancelled_twice()
    {
        (SalesOrder order, _) = Fixture.ConfirmedWithLine();
        order.Cancel("First reason.");

        order.Cancel("Second reason.").Error.Code.Should().Be("sales.order.already_closed");
    }

    [Fact]
    public void A_draft_can_be_cancelled_too()
    {
        (SalesOrder order, _) = Fixture.DraftWithLine();

        order.Cancel("Quote not taken up.").IsSuccess.Should().BeTrue();
        order.Status.Should().Be(SalesOrderStatus.Cancelled);
    }
}
