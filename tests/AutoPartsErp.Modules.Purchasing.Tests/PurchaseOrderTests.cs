using AutoPartsErp.Modules.Purchasing.Domain;
using AutoPartsErp.Modules.Purchasing.Domain.Orders;
using AutoPartsErp.Modules.Purchasing.Domain.Orders.Events;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Purchasing.Tests;

/// <summary>Shared setup, so each test says only what it is actually about.</summary>
internal static class Fixture
{
    public static readonly DateOnly Today = new(2026, 9, 1);
    public static readonly SupplierRef Supplier = new(Guid.NewGuid());
    public static readonly WarehouseRef Warehouse = new(Guid.NewGuid());

    public static PartRef NewPart() => new(Guid.NewGuid());

    public static PurchaseOrder NewDraft(string number = "PO-2026-00001") =>
        PurchaseOrder.Draft(number, Supplier, "BOSCH", Warehouse, Currency.Eur).Value;

    /// <summary>A draft with one line of <paramref name="quantity"/> at 4.50 each.</summary>
    public static (PurchaseOrder Order, PurchaseOrderLineId LineId) DraftWithLine(int quantity = 10)
    {
        PurchaseOrder order = NewDraft();
        PurchaseOrderLineId lineId = order
            .AddLine(NewPart(), "0986452041", "Oil filter", Quantity.Each(quantity), Money.Of(4.50m, Currency.Eur))
            .Value;

        return (order, lineId);
    }

    /// <summary>A submitted order with one line, ready to receive against.</summary>
    public static (PurchaseOrder Order, PurchaseOrderLineId LineId) SubmittedWithLine(int quantity = 10)
    {
        (PurchaseOrder order, PurchaseOrderLineId lineId) = DraftWithLine(quantity);
        order.Submit(Today);
        order.ClearDomainEvents();

        return (order, lineId);
    }
}

/// <summary>Raising the document, before anything has been committed to anyone.</summary>
public sealed class PurchaseOrderDraftingTests
{
    [Fact]
    public void A_new_order_starts_as_an_editable_draft()
    {
        PurchaseOrder order = Fixture.NewDraft();

        order.Status.Should().Be(PurchaseOrderStatus.Draft);
        order.IsEditable.Should().BeTrue();
        order.CanReceive.Should().BeFalse();
        order.IsClosed.Should().BeFalse();
    }

    [Fact]
    public void The_order_number_and_supplier_code_are_normalised()
    {
        PurchaseOrder order = PurchaseOrder
            .Draft(" po-2026-00001 ", Fixture.Supplier, " bosch ", Fixture.Warehouse, Currency.Eur)
            .Value;

        order.OrderNumber.Should().Be("PO-2026-00001");
        order.SupplierCode.Should().Be("BOSCH");
    }

    [Fact]
    public void Drafting_raises_an_event()
    {
        PurchaseOrder order = Fixture.NewDraft();

        order.DomainEvents.OfType<PurchaseOrderDraftedDomainEvent>().Should().ContainSingle();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void An_order_needs_a_number(string? number)
    {
        Result<PurchaseOrder> order = PurchaseOrder
            .Draft(number, Fixture.Supplier, "BOSCH", Fixture.Warehouse, Currency.Eur);

        order.IsFailure.Should().BeTrue();
        order.Error.Code.Should().Be("purchasing.order.number_required");
    }

    [Fact]
    public void An_order_needs_a_supplier()
    {
        Result<PurchaseOrder> order = PurchaseOrder
            .Draft("PO-1", SupplierRef.Empty, "BOSCH", Fixture.Warehouse, Currency.Eur);

        order.IsFailure.Should().BeTrue();
        order.Error.Code.Should().Be("purchasing.order.supplier_required");
    }

    [Fact]
    public void An_order_needs_somewhere_to_deliver_to()
    {
        Result<PurchaseOrder> order = PurchaseOrder
            .Draft("PO-1", Fixture.Supplier, "BOSCH", WarehouseRef.Empty, Currency.Eur);

        order.IsFailure.Should().BeTrue();
        order.Error.Code.Should().Be("purchasing.order.warehouse_required");
    }

    [Fact]
    public void A_new_order_has_no_lines_and_no_value()
    {
        PurchaseOrder order = Fixture.NewDraft();

        order.Lines.Should().BeEmpty();
        order.Total.IsZero.Should().BeTrue();
        order.HasOutstandingLines.Should().BeFalse();
    }
}

/// <summary>Putting parts on the document.</summary>
public sealed class PurchaseOrderLineTests
{
    [Fact]
    public void A_line_can_be_added_to_a_draft()
    {
        (PurchaseOrder order, PurchaseOrderLineId lineId) = Fixture.DraftWithLine();

        order.Lines.Should().ContainSingle();
        lineId.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void The_total_is_the_sum_of_the_lines()
    {
        PurchaseOrder order = Fixture.NewDraft();
        order.AddLine(Fixture.NewPart(), "A", "Oil filter", Quantity.Each(10), Money.Of(4.50m, Currency.Eur));
        order.AddLine(Fixture.NewPart(), "B", "Cabin filter", Quantity.Each(4), Money.Of(7.25m, Currency.Eur));

        order.Total.Amount.Should().Be(74.00m);
        order.Total.Currency.Should().Be(Currency.Eur);
    }

    [Fact]
    public void The_same_part_cannot_be_added_twice()
    {
        PurchaseOrder order = Fixture.NewDraft();
        PartRef part = Fixture.NewPart();
        order.AddLine(part, "A", "Oil filter", Quantity.Each(10), Money.Of(4.50m, Currency.Eur));

        Result<PurchaseOrderLineId> second = order
            .AddLine(part, "A", "Oil filter", Quantity.Each(5), Money.Of(4.50m, Currency.Eur));

        second.IsFailure.Should().BeTrue();
        second.Error.Code.Should().Be("purchasing.line.duplicate_part");
    }

    [Fact]
    public void A_line_must_be_priced_in_the_orders_currency()
    {
        PurchaseOrder order = Fixture.NewDraft();

        Result<PurchaseOrderLineId> line = order
            .AddLine(Fixture.NewPart(), "A", "Oil filter", Quantity.Each(10), Money.Of(4.50m, Currency.Usd));

        line.IsFailure.Should().BeTrue();
        line.Error.Code.Should().Be("purchasing.line.currency_mismatch");
    }

    [Fact]
    public void A_line_needs_a_part()
    {
        PurchaseOrder order = Fixture.NewDraft();

        Result<PurchaseOrderLineId> line = order
            .AddLine(PartRef.Empty, "A", "Oil filter", Quantity.Each(10), Money.Of(4.50m, Currency.Eur));

        line.IsFailure.Should().BeTrue();
        line.Error.Code.Should().Be("purchasing.line.part_required");
    }

    [Fact]
    public void Ordering_nothing_is_not_ordering()
    {
        PurchaseOrder order = Fixture.NewDraft();

        Result<PurchaseOrderLineId> line = order
            .AddLine(Fixture.NewPart(), "A", "Oil filter", Quantity.Each(0), Money.Of(4.50m, Currency.Eur));

        line.IsFailure.Should().BeTrue();
        line.Error.Code.Should().Be("purchasing.line.quantity_not_positive");
    }

    [Fact]
    public void A_line_quantity_can_be_changed_on_a_draft()
    {
        (PurchaseOrder order, PurchaseOrderLineId lineId) = Fixture.DraftWithLine();

        order.ChangeLineQuantity(lineId, Quantity.Each(25)).IsSuccess.Should().BeTrue();
        order.Total.Amount.Should().Be(112.50m);
    }

    [Fact]
    public void A_line_price_can_be_changed_on_a_draft()
    {
        (PurchaseOrder order, PurchaseOrderLineId lineId) = Fixture.DraftWithLine();

        order.ChangeLinePrice(lineId, Money.Of(5.00m, Currency.Eur)).IsSuccess.Should().BeTrue();
        order.Total.Amount.Should().Be(50.00m);
    }

    [Fact]
    public void A_line_can_be_removed_from_a_draft()
    {
        (PurchaseOrder order, PurchaseOrderLineId lineId) = Fixture.DraftWithLine();

        order.RemoveLine(lineId).IsSuccess.Should().BeTrue();
        order.Lines.Should().BeEmpty();
    }

    [Fact]
    public void Changing_a_line_that_is_not_there_is_a_not_found()
    {
        (PurchaseOrder order, _) = Fixture.DraftWithLine();

        Result changed = order.ChangeLineQuantity(PurchaseOrderLineId.New(), Quantity.Each(5));

        changed.IsFailure.Should().BeTrue();
        changed.Error.Code.Should().Be("purchasing.line.not_found");
    }

    [Fact]
    public void A_line_carries_its_own_outstanding_quantity_and_value()
    {
        (PurchaseOrder order, _) = Fixture.DraftWithLine(10);
        PurchaseOrderLine line = order.Lines.Single();

        line.OutstandingQuantity.Value.Should().Be(10m);
        line.LineTotal.Amount.Should().Be(45.00m);
        line.OutstandingValue.Amount.Should().Be(45.00m);
        line.IsFullyReceived.Should().BeFalse();
    }
}

/// <summary>
/// The transition from shopping list to commitment, and everything it closes off.
/// </summary>
public sealed class PurchaseOrderSubmissionTests
{
    [Fact]
    public void An_empty_order_cannot_be_sent()
    {
        PurchaseOrder order = Fixture.NewDraft();

        Result submitted = order.Submit(Fixture.Today);

        submitted.IsFailure.Should().BeTrue();
        submitted.Error.Code.Should().Be("purchasing.order.no_lines");
    }

    [Fact]
    public void Submitting_records_the_date_and_moves_the_status()
    {
        (PurchaseOrder order, _) = Fixture.DraftWithLine();

        order.Submit(Fixture.Today).IsSuccess.Should().BeTrue();

        order.Status.Should().Be(PurchaseOrderStatus.Submitted);
        order.OrderedOn.Should().Be(Fixture.Today);
        order.CanReceive.Should().BeTrue();
    }

    [Fact]
    public void The_submitted_event_carries_the_order_value()
    {
        (PurchaseOrder order, _) = Fixture.DraftWithLine(10);

        order.Submit(Fixture.Today, new DateOnly(2026, 9, 10));

        PurchaseOrderSubmittedDomainEvent submitted =
            order.DomainEvents.OfType<PurchaseOrderSubmittedDomainEvent>().Single();

        submitted.Total.Should().Be(45.00m);
        submitted.CurrencyCode.Should().Be("EUR");
        submitted.ExpectedOn.Should().Be(new DateOnly(2026, 9, 10));
        submitted.WarehouseId.Should().Be(Fixture.Warehouse);
    }

    [Fact]
    public void A_delivery_date_in_the_past_is_not_a_promise()
    {
        (PurchaseOrder order, _) = Fixture.DraftWithLine();

        Result submitted = order.Submit(Fixture.Today, new DateOnly(2026, 8, 30));

        submitted.IsFailure.Should().BeTrue();
        submitted.Error.Code.Should().Be("purchasing.order.expected_date_past");
    }

    [Fact]
    public void A_sent_order_can_no_longer_be_edited()
    {
        (PurchaseOrder order, PurchaseOrderLineId lineId) = Fixture.SubmittedWithLine();

        order.IsEditable.Should().BeFalse();
        order.AddLine(Fixture.NewPart(), "B", "x", Quantity.Each(1), Money.Of(1m, Currency.Eur))
            .Error.Code.Should().Be("purchasing.order.not_editable");
        order.ChangeLineQuantity(lineId, Quantity.Each(1))
            .Error.Code.Should().Be("purchasing.order.not_editable");
        order.RemoveLine(lineId).Error.Code.Should().Be("purchasing.order.not_editable");
    }

    [Fact]
    public void An_order_cannot_be_sent_twice()
    {
        (PurchaseOrder order, _) = Fixture.SubmittedWithLine();

        Result again = order.Submit(Fixture.Today);

        again.IsFailure.Should().BeTrue();
        again.Error.Code.Should().Be("purchasing.order.already_submitted");
    }

    [Fact]
    public void Confirming_records_the_promised_date_and_their_reference()
    {
        (PurchaseOrder order, _) = Fixture.SubmittedWithLine();

        order.Confirm(new DateOnly(2026, 9, 12), Fixture.Today, "ACK-99").IsSuccess.Should().BeTrue();

        order.Status.Should().Be(PurchaseOrderStatus.Confirmed);
        order.ExpectedOn.Should().Be(new DateOnly(2026, 9, 12));
        order.SupplierReference.Should().Be("ACK-99");
        order.DomainEvents.OfType<PurchaseOrderConfirmedDomainEvent>().Should().ContainSingle();
    }

    [Fact]
    public void Only_an_unacknowledged_order_can_be_confirmed()
    {
        (PurchaseOrder order, _) = Fixture.DraftWithLine();

        Result confirmed = order.Confirm(new DateOnly(2026, 9, 12), Fixture.Today);

        confirmed.IsFailure.Should().BeTrue();
        confirmed.Error.Code.Should().Be("purchasing.order.not_awaiting_confirmation");
    }

    [Fact]
    public void Goods_cannot_be_received_against_a_draft()
    {
        (PurchaseOrder order, PurchaseOrderLineId lineId) = Fixture.DraftWithLine();

        Result received = order.ReceiveLine(lineId, Quantity.Each(1));

        received.IsFailure.Should().BeTrue();
        received.Error.Code.Should().Be("purchasing.order.not_receivable");
    }
}

/// <summary>Deliveries: the part of a purchase order that touches the real world.</summary>
public sealed class PurchaseOrderReceiptTests
{
    [Fact]
    public void A_partial_delivery_leaves_the_order_partially_received()
    {
        (PurchaseOrder order, PurchaseOrderLineId lineId) = Fixture.SubmittedWithLine(10);

        order.ReceiveLine(lineId, Quantity.Each(6)).IsSuccess.Should().BeTrue();

        order.Status.Should().Be(PurchaseOrderStatus.PartiallyReceived);
        order.Lines.Single().OutstandingQuantity.Value.Should().Be(4m);
        order.OutstandingValue.Amount.Should().Be(18.00m);
        order.HasOutstandingLines.Should().BeTrue();
    }

    [Fact]
    public void The_receipt_event_carries_this_delivery_not_the_running_total()
    {
        (PurchaseOrder order, PurchaseOrderLineId lineId) = Fixture.SubmittedWithLine(10);

        order.ReceiveLine(lineId, Quantity.Each(6));
        order.ClearDomainEvents();
        order.ReceiveLine(lineId, Quantity.Each(4));

        GoodsReceivedDomainEvent received = order.DomainEvents.OfType<GoodsReceivedDomainEvent>().Single();

        received.Quantity.Should().Be(4m);
        received.UnitCode.Should().Be("EA");
        received.UnitPrice.Should().Be(4.50m);
        received.CurrencyCode.Should().Be("EUR");
        received.WarehouseId.Should().Be(Fixture.Warehouse);
        received.LineId.Should().Be(lineId);
    }

    [Fact]
    public void Receiving_everything_completes_the_order()
    {
        (PurchaseOrder order, PurchaseOrderLineId lineId) = Fixture.SubmittedWithLine(10);

        order.ReceiveLine(lineId, Quantity.Each(10)).IsSuccess.Should().BeTrue();

        order.Status.Should().Be(PurchaseOrderStatus.Received);
        order.IsClosed.Should().BeTrue();
        order.HasOutstandingLines.Should().BeFalse();
        order.OutstandingValue.IsZero.Should().BeTrue();
        order.DomainEvents.OfType<PurchaseOrderCompletedDomainEvent>().Should().ContainSingle();
    }

    [Fact]
    public void An_order_is_only_complete_when_every_line_is()
    {
        PurchaseOrder order = Fixture.NewDraft();
        PurchaseOrderLineId first = order
            .AddLine(Fixture.NewPart(), "A", "Oil filter", Quantity.Each(10), Money.Of(4.50m, Currency.Eur)).Value;
        order.AddLine(Fixture.NewPart(), "B", "Cabin filter", Quantity.Each(4), Money.Of(7.25m, Currency.Eur));
        order.Submit(Fixture.Today);

        order.ReceiveLine(first, Quantity.Each(10));

        order.Status.Should().Be(PurchaseOrderStatus.PartiallyReceived);
        order.DomainEvents.OfType<PurchaseOrderCompletedDomainEvent>().Should().BeEmpty();
    }

    [Fact]
    public void More_than_was_ordered_is_refused()
    {
        (PurchaseOrder order, PurchaseOrderLineId lineId) = Fixture.SubmittedWithLine(10);

        Result received = order.ReceiveLine(lineId, Quantity.Each(11));

        received.IsFailure.Should().BeTrue();
        received.Error.Code.Should().Be("purchasing.line.over_receipt");
    }

    [Fact]
    public void Over_receipt_is_measured_against_what_is_still_outstanding()
    {
        (PurchaseOrder order, PurchaseOrderLineId lineId) = Fixture.SubmittedWithLine(10);
        order.ReceiveLine(lineId, Quantity.Each(8));

        Result received = order.ReceiveLine(lineId, Quantity.Each(3));

        received.IsFailure.Should().BeTrue();
        received.Error.Code.Should().Be("purchasing.line.over_receipt");
    }

    [Fact]
    public void Receiving_nothing_is_not_receiving()
    {
        (PurchaseOrder order, PurchaseOrderLineId lineId) = Fixture.SubmittedWithLine();

        order.ReceiveLine(lineId, Quantity.Each(0))
            .Error.Code.Should().Be("purchasing.line.receipt_not_positive");
    }

    [Fact]
    public void A_line_that_is_already_full_takes_no_more()
    {
        (PurchaseOrder order, PurchaseOrderLineId lineId) = Fixture.SubmittedWithLine(10);
        order.ReceiveLine(lineId, Quantity.Each(10));

        Result received = order.ReceiveLine(lineId, Quantity.Each(1));

        received.IsFailure.Should().BeTrue();
        received.Error.Code.Should().Be("purchasing.order.already_closed");
    }

    [Fact]
    public void Receiving_against_a_line_that_is_not_there_is_a_not_found()
    {
        (PurchaseOrder order, _) = Fixture.SubmittedWithLine();

        order.ReceiveLine(PurchaseOrderLineId.New(), Quantity.Each(1))
            .Error.Code.Should().Be("purchasing.line.not_found");
    }

    [Fact]
    public void A_quantity_in_the_wrong_unit_is_refused()
    {
        (PurchaseOrder order, PurchaseOrderLineId lineId) = Fixture.SubmittedWithLine(10);

        Result received = order.ReceiveLine(lineId, Quantity.Of(5m, UnitOfMeasure.Litre));

        received.IsFailure.Should().BeTrue();
        received.Error.Code.Should().Be("purchasing.line.unit_mismatch");
    }

    [Fact]
    public void A_part_delivered_order_still_refuses_edits()
    {
        (PurchaseOrder order, PurchaseOrderLineId lineId) = Fixture.SubmittedWithLine(10);
        order.ReceiveLine(lineId, Quantity.Each(6));

        // The line also guards against cutting below what arrived, but the order-level rule
        // gets there first: nothing about a sent document is editable, delivered or not.
        order.ChangeLineQuantity(lineId, Quantity.Each(2))
            .Error.Code.Should().Be("purchasing.order.not_editable");
    }
}

/// <summary>Ending an order, in both of the ways it ends badly.</summary>
public sealed class PurchaseOrderClosureTests
{
    [Fact]
    public void An_order_can_be_cancelled_before_anything_arrives()
    {
        (PurchaseOrder order, _) = Fixture.SubmittedWithLine();

        order.Cancel("Found them cheaper elsewhere.").IsSuccess.Should().BeTrue();

        order.Status.Should().Be(PurchaseOrderStatus.Cancelled);
        order.IsClosed.Should().BeTrue();
        order.ClosureReason.Should().Be("Found them cheaper elsewhere.");
        order.DomainEvents.OfType<PurchaseOrderCancelledDomainEvent>().Should().ContainSingle();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void A_cancellation_needs_an_explanation(string? reason)
    {
        (PurchaseOrder order, _) = Fixture.SubmittedWithLine();

        Result cancelled = order.Cancel(reason);

        cancelled.IsFailure.Should().BeTrue();
        cancelled.Error.Code.Should().Be("purchasing.order.cancel_reason_required");
    }

    [Fact]
    public void An_order_with_goods_already_in_cannot_be_cancelled()
    {
        (PurchaseOrder order, PurchaseOrderLineId lineId) = Fixture.SubmittedWithLine(10);
        order.ReceiveLine(lineId, Quantity.Each(6));

        Result cancelled = order.Cancel("Changed our mind.");

        cancelled.IsFailure.Should().BeTrue();
        cancelled.Error.Code.Should().Be("purchasing.order.cannot_cancel_after_receipt");
    }

    [Fact]
    public void An_order_cannot_be_cancelled_twice()
    {
        (PurchaseOrder order, _) = Fixture.SubmittedWithLine();
        order.Cancel("First reason.");

        order.Cancel("Second reason.").Error.Code.Should().Be("purchasing.order.already_closed");
    }

    [Fact]
    public void A_short_delivery_can_be_accepted_and_closed()
    {
        (PurchaseOrder order, PurchaseOrderLineId lineId) = Fixture.SubmittedWithLine(100);
        order.ReceiveLine(lineId, Quantity.Each(96));

        order.CloseShort("Supplier had no more; agreed to leave it.").IsSuccess.Should().BeTrue();

        order.Status.Should().Be(PurchaseOrderStatus.ClosedShort);
        order.IsClosed.Should().BeTrue();
        order.DomainEvents.OfType<PurchaseOrderClosedShortDomainEvent>().Should().ContainSingle();
    }

    [Fact]
    public void Closing_short_needs_an_explanation()
    {
        (PurchaseOrder order, PurchaseOrderLineId lineId) = Fixture.SubmittedWithLine(100);
        order.ReceiveLine(lineId, Quantity.Each(96));

        order.CloseShort("  ").Error.Code.Should().Be("purchasing.order.cancel_reason_required");
    }

    [Fact]
    public void An_order_with_nothing_received_is_cancelled_rather_than_closed_short()
    {
        (PurchaseOrder order, _) = Fixture.SubmittedWithLine();

        Result closed = order.CloseShort("Nothing turned up.");

        closed.IsFailure.Should().BeTrue();
        closed.Error.Code.Should().Be("purchasing.order.not_receivable");
    }

    [Fact]
    public void A_closed_order_takes_no_further_deliveries()
    {
        (PurchaseOrder order, PurchaseOrderLineId lineId) = Fixture.SubmittedWithLine(100);
        order.ReceiveLine(lineId, Quantity.Each(96));
        order.CloseShort("Agreed to leave it.");

        order.ReceiveLine(lineId, Quantity.Each(1))
            .Error.Code.Should().Be("purchasing.order.already_closed");
    }
}
