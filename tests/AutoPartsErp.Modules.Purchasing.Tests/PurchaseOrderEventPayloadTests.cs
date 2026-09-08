using AutoPartsErp.Modules.Purchasing.Domain;
using AutoPartsErp.Modules.Purchasing.Domain.Orders;
using AutoPartsErp.Modules.Purchasing.Domain.Orders.Events;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Purchasing.Tests;

/// <summary>
/// What the events leaving this module actually say.
/// <para>
/// These assert a payload rather than a state transition, which is unusual and is the point. The
/// header of a purchase order tells another module that money was committed; only the lines tell
/// a warehouse which parts to expect. An order header travelling on its own is exactly what these
/// events used to be, and the consequence was an expected-delivery figure that was always zero and
/// a buyer's list that suggested buying what had already been bought.
/// </para>
/// <para>
/// A payload is also the one kind of mistake the compiler cannot catch on the far side. The
/// consumer reads what it is given; if a line is missing, nothing fails — the warehouse simply
/// never learns the goods are coming.
/// </para>
/// </summary>
public sealed class PurchaseOrderEventPayloadTests
{
    [Fact]
    public void Submitting_says_which_parts_are_coming_and_how_many()
    {
        PurchaseOrder order = Fixture.NewDraft();
        PartRef part = Fixture.NewPart();

        order.AddLine(part, "0986452041", "Oil filter", Quantity.Each(24), Money.Of(4.50m, Currency.Eur));
        order.Submit(Fixture.Today, Fixture.Today.AddDays(7));

        PurchaseOrderSubmittedDomainEvent submitted = order.DomainEvents
            .OfType<PurchaseOrderSubmittedDomainEvent>()
            .Single();

        OrderedLine line = submitted.Lines.Single();
        line.PartId.Should().Be(part);
        line.Quantity.Should().Be(24m);
        line.UnitCode.Should().Be(UnitOfMeasure.Each.Code);
        submitted.WarehouseId.Should().Be(Fixture.Warehouse);
    }

    /// <summary>
    /// A cancellation reports what is still outstanding, not what was ordered. The two are the
    /// same on a cancellation today, because nothing may be cancelled after a receipt — reading
    /// it from the outstanding quantity means the payload cannot drift from the rule if that ever
    /// changes.
    /// </summary>
    [Fact]
    public void Cancelling_says_what_stops_being_expected()
    {
        (PurchaseOrder order, _) = Fixture.SubmittedWithLine(10);

        order.Cancel("Supplier discontinued the line");

        PurchaseOrderCancelledDomainEvent cancelled = order.DomainEvents
            .OfType<PurchaseOrderCancelledDomainEvent>()
            .Single();

        cancelled.WarehouseId.Should().Be(Fixture.Warehouse);
        cancelled.Lines.Single().Quantity.Should().Be(10m);
    }

    /// <summary>
    /// The event that did not exist, and the figure it protects. Closing short leaves four the
    /// supplier will never send; without this they stay on the expected quantity for ever and
    /// every reorder check afterwards believes they are on their way.
    /// </summary>
    [Fact]
    public void Closing_short_says_only_the_balance_that_never_arrived()
    {
        (PurchaseOrder order, PurchaseOrderLineId lineId) = Fixture.SubmittedWithLine(10);
        order.ReceiveLine(lineId, Quantity.Each(6));

        order.CloseShort("Supplier cannot complete; agreed to close").IsSuccess.Should().BeTrue();

        PurchaseOrderClosedShortDomainEvent closed = order.DomainEvents
            .OfType<PurchaseOrderClosedShortDomainEvent>()
            .Single();

        closed.WarehouseId.Should().Be(Fixture.Warehouse);
        closed.Lines.Single().Quantity.Should().Be(4m);
    }

    /// <summary>
    /// A line already received in full has nothing to say about a close. Sending it as a zero
    /// would make the consumer decide what a zero means, and the consumer would be guessing.
    /// </summary>
    [Fact]
    public void A_line_already_received_in_full_is_left_out_of_a_close()
    {
        PurchaseOrder order = Fixture.NewDraft();

        PurchaseOrderLineId first = order
            .AddLine(Fixture.NewPart(), "0986452041", "Oil filter", Quantity.Each(10), Money.Of(4.50m, Currency.Eur))
            .Value;

        PurchaseOrderLineId second = order
            .AddLine(Fixture.NewPart(), "0986424815", "Brake pad set", Quantity.Each(4), Money.Of(28.00m, Currency.Eur))
            .Value;

        order.Submit(Fixture.Today);
        order.ReceiveLine(first, Quantity.Each(10));
        order.ReceiveLine(second, Quantity.Each(1));

        order.CloseShort("Agreed to close");

        PurchaseOrderClosedShortDomainEvent closed = order.DomainEvents
            .OfType<PurchaseOrderClosedShortDomainEvent>()
            .Single();

        OrderedLine line = closed.Lines.Single();
        line.LineId.Should().Be(second);
        line.Quantity.Should().Be(3m);
    }
}
