using AutoPartsErp.Modules.Sales.Domain;
using AutoPartsErp.Modules.Sales.Domain.Orders;
using AutoPartsErp.Modules.Sales.Domain.Returns;
using AutoPartsErp.Modules.Sales.Domain.Returns.Events;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Sales.Tests;

/// <summary>
/// Goods coming back, and what the document says about them.
/// <para>
/// The rules that matter here are about not inventing things: not stock that never went out, not
/// a credit at a price nobody was charged, and not a shelf entry for a part somebody has already
/// decided is scrap.
/// </para>
/// </summary>
public sealed class CustomerReturnTests
{
    private static readonly DateOnly Today = new(2026, 9, 8);

    [Fact]
    public void A_return_starts_as_a_draft_with_nothing_on_it()
    {
        CustomerReturn returned = NewReturn();

        returned.Status.Should().Be(CustomerReturnStatus.Draft);
        returned.IsDraft.Should().BeTrue();
        returned.Lines.Should().BeEmpty();
        returned.ReceivedOn.Should().BeNull();
        returned.GrossTotal.Amount.Should().Be(0m);
    }

    /// <summary>
    /// The reason is not optional, because it is what decides everything downstream: whether the
    /// part goes on the shelf, whether the supplier hears about it, whether the customer is
    /// charged for the trouble. Nobody reconstructs it from a stock movement six weeks later.
    /// </summary>
    [Fact]
    public void A_return_needs_a_reason()
    {
        Raise(reason: "  ").Error.Code.Should().Be("sales.return.reason_required");
    }

    /// <summary>
    /// The credit is at what they paid. Copying the price rather than looking one up is the whole
    /// point: re-deriving it would mean re-running price rules that have since moved, and
    /// crediting a figure that appears on no document anybody ever sent.
    /// </summary>
    [Fact]
    public void A_line_is_credited_at_what_the_customer_paid()
    {
        CustomerReturn returned = NewReturn();

        returned.AddLine(
            SalesOrderLineId.New(),
            new PartRef(Guid.NewGuid()),
            "BP-1188",
            "Brake pad set",
            Quantity.Each(4),
            Money.Of(24.50m, Currency.Eur),
            discountPercent: 10m,
            vatRatePercent: 23m,
            ReturnDisposition.BackToStock)
            .IsSuccess.Should().BeTrue();

        CustomerReturnLine line = returned.Lines.Single();

        line.ExtendedPrice.Amount.Should().Be(98.00m);
        line.DiscountAmount.Amount.Should().Be(9.80m);
        line.NetTotal.Amount.Should().Be(88.20m);
        line.VatAmount.Amount.Should().Be(20.29m);
        returned.GrossTotal.Amount.Should().Be(108.49m);
    }

    /// <summary>
    /// Two rows for one order line would each be checked against the order separately and both
    /// would pass, which is how somebody gets credited twice for one alternator.
    /// </summary>
    [Fact]
    public void The_same_order_line_cannot_appear_twice_on_one_return()
    {
        CustomerReturn returned = NewReturn();
        var lineId = SalesOrderLineId.New();

        AddLine(returned, lineId, quantity: 2);

        AddLine(returned, lineId, quantity: 1)
            .Error.Code.Should().Be("sales.return.line_already_on_return");
    }

    [Fact]
    public void What_happens_to_the_goods_has_to_be_decided()
    {
        CustomerReturn returned = NewReturn();

        AddLine(returned, SalesOrderLineId.New(), disposition: ReturnDisposition.Unknown)
            .Error.Code.Should().Be("sales.return.disposition_required");
    }

    /// <summary>
    /// Receiving is the step that moves stock, and only saleable lines are announced. A scrapped
    /// part is credited and written off; booking it in and adjusting it straight back out would
    /// put two movements in the ledger for stock that was never on a shelf.
    /// </summary>
    [Fact]
    public void Only_the_saleable_lines_are_announced_to_inventory()
    {
        CustomerReturn returned = NewReturn();
        AddLine(returned, SalesOrderLineId.New(), disposition: ReturnDisposition.BackToStock);
        AddLine(returned, SalesOrderLineId.New(), disposition: ReturnDisposition.Scrap);

        returned.Receive(Today).IsSuccess.Should().BeTrue();

        returned.Status.Should().Be(CustomerReturnStatus.Received);
        returned.ReceivedOn.Should().Be(Today);
        returned.DomainEvents.OfType<GoodsReturnedDomainEvent>().Should().ContainSingle();
        returned.DomainEvents.OfType<CustomerReturnReceivedDomainEvent>().Should().ContainSingle();
    }

    [Fact]
    public void An_empty_return_cannot_be_received()
    {
        NewReturn().Receive(Today).Error.Code.Should().Be("sales.return.no_lines");
    }

    /// <summary>
    /// Once the goods are on the shelf there is nothing to undo. The way out of a mistake is
    /// another movement, which is the same rule the stock ledger has lived by since it was
    /// written.
    /// </summary>
    [Fact]
    public void A_received_return_is_closed_to_changes_and_to_cancelling()
    {
        CustomerReturn returned = NewReturn();
        AddLine(returned, SalesOrderLineId.New());
        returned.Receive(Today);

        returned.IsDraft.Should().BeFalse();
        AddLine(returned, SalesOrderLineId.New()).Error.Code.Should().Be("sales.return.not_editable");
        returned.Receive(Today).Error.Code.Should().Be("sales.return.already_received");
        returned.Cancel("Changed their mind.").Error.Code.Should().Be("sales.return.already_received");
    }

    [Fact]
    public void Cancelling_a_draft_needs_a_reason_and_closes_it()
    {
        CustomerReturn returned = NewReturn();
        AddLine(returned, SalesOrderLineId.New());

        returned.Cancel(null).Error.Code.Should().Be("sales.return.closure_reason_required");

        returned.Cancel("They found the old one.").IsSuccess.Should().BeTrue();
        returned.Status.Should().Be(CustomerReturnStatus.Cancelled);
        returned.Receive(Today).Error.Code.Should().Be("sales.return.already_closed");
    }

    private static Result<CustomerReturn> Raise(string? reason = "Wrong part supplied.") =>
        CustomerReturn.Raise(
            "RET-2026-00014",
            SalesOrderId.New(),
            "SO-2026-01188",
            Fixture.Customer,
            "WKSP",
            "Workshop Lda",
            Fixture.Warehouse,
            "EUR",
            reason);

    private static CustomerReturn NewReturn() => Raise().Value;

    private static Result<CustomerReturnLineId> AddLine(
        CustomerReturn returned,
        SalesOrderLineId lineId,
        int quantity = 1,
        ReturnDisposition disposition = ReturnDisposition.BackToStock) =>
        returned.AddLine(
            lineId,
            new PartRef(Guid.NewGuid()),
            "BP-1188",
            "Brake pad set",
            Quantity.Each(quantity),
            Money.Of(24.50m, Currency.Eur),
            discountPercent: 0m,
            vatRatePercent: 23m,
            disposition);
}

/// <summary>
/// What the order remembers about goods that have come back.
/// <para>
/// One rule, and it is arithmetic: no more can come back than went out and has not already come
/// back. Whether it should have been taken back at all is a conversation at a counter, and a
/// system that refused those would be overruled by somebody typing an adjustment instead.
/// </para>
/// </summary>
public sealed class SalesOrderReturnTests
{
    [Fact]
    public void Goods_that_never_left_cannot_come_back()
    {
        (SalesOrder order, SalesOrderLineId lineId) = Fixture.ConfirmedWithLine(10);
        order.DispatchLine(lineId, Quantity.Each(4));

        order.RecordReturn(lineId, Quantity.Each(5))
            .Error.Code.Should().Be("sales.return.exceeds_dispatched");

        order.RecordReturn(lineId, Quantity.Each(4)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void What_has_come_back_is_counted_and_cannot_come_back_twice()
    {
        (SalesOrder order, SalesOrderLineId lineId) = Fixture.ConfirmedWithLine(10);
        order.DispatchLine(lineId, Quantity.Each(10));

        order.RecordReturn(lineId, Quantity.Each(3)).IsSuccess.Should().BeTrue();
        order.RecordReturn(lineId, Quantity.Each(3)).IsSuccess.Should().BeTrue();

        SalesOrderLine line = order.Lines.Single();
        line.ReturnedQuantity.Value.Should().Be(6m);
        line.ReturnableQuantity.Value.Should().Be(4m);

        order.RecordReturn(lineId, Quantity.Each(5))
            .Error.Code.Should().Be("sales.return.exceeds_dispatched");
    }

    /// <summary>
    /// A return does not un-dispatch anything. A line that quietly gave itself back would make
    /// the order outstanding again, put it on the picking list, and promise the customer goods
    /// they have just sent back.
    /// </summary>
    [Fact]
    public void A_return_does_not_put_the_order_back_on_the_picking_list()
    {
        (SalesOrder order, SalesOrderLineId lineId) = Fixture.ConfirmedWithLine(10);
        order.DispatchLine(lineId, Quantity.Each(10));

        order.RecordReturn(lineId, Quantity.Each(10));

        order.Status.Should().Be(SalesOrderStatus.Dispatched);
        order.HasOutstandingLines.Should().BeFalse();
        order.Lines.Single().DispatchedQuantity.Value.Should().Be(10m);
    }
}
