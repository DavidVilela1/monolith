using AutoPartsErp.Modules.Inventory.Domain;
using AutoPartsErp.Modules.Inventory.Domain.Stock;
using AutoPartsErp.Modules.Inventory.Domain.Stock.Events;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Inventory.Tests;

/// <summary>
/// What a warehouse knows about goods that are on their way.
/// <para>
/// The bug all of this exists to kill: without an expected figure, a part ordered on Monday sits
/// below its reorder point all week and produces a fresh suggestion every morning until the lorry
/// arrives. The buyer either orders it twice or learns to ignore the list — and a list that is
/// ignored also hides the parts that genuinely need ordering, so the second failure is worse than
/// the first.
/// </para>
/// </summary>
public sealed class IncomingStockTests
{
    private static readonly PurchaseOrderRef Order = new(Guid.NewGuid());
    private static readonly PurchaseOrderRef OtherOrder = new(Guid.NewGuid());
    private static readonly DateOnly Expected = new(2026, 9, 18);

    [Fact]
    public void Expecting_a_delivery_raises_what_is_on_order_and_records_the_order_behind_it()
    {
        StockItem stock = Fixture.WithStock(4m);
        var line = new PurchaseOrderLineRef(Guid.NewGuid());

        stock.ExpectIncoming(Order, line, "PO-2026-00042", 20m, Expected)
            .IsSuccess.Should().BeTrue();

        stock.OnOrder.Value.Should().Be(20m);
        stock.ProjectedAvailable.Value.Should().Be(24m);

        IncomingStock incoming = stock.Incoming.Single();
        incoming.OrderNumber.Should().Be("PO-2026-00042");
        incoming.ExpectedOn.Should().Be(Expected);
        incoming.Outstanding.Value.Should().Be(20m);
        incoming.IsOutstanding.Should().BeTrue();
    }

    /// <summary>
    /// The reason the whole thing exists. Four on the shelf against a reorder point of ten is a
    /// part that needs buying — unless twenty are already coming, in which case buying more is
    /// buying the same thing twice.
    /// </summary>
    [Fact]
    public void Stock_already_on_order_stops_the_part_being_suggested_again()
    {
        StockItem stock = Fixture.WithStock(10m);
        stock.SetReplenishmentPolicy(5m, 20m);
        stock.ExpectIncoming(Order, new PurchaseOrderLineRef(Guid.NewGuid()), "PO-1", 20m, Expected);
        stock.ClearDomainEvents();

        stock.Issue(6m, Fixture.SalesOrder(), Fixture.Now);

        stock.Available.Value.Should().Be(4m);
        stock.NeedsReplenishment.Should().BeFalse();
        stock.DomainEvents.OfType<StockFellBelowReorderPointDomainEvent>().Should().BeEmpty();
    }

    /// <summary>
    /// And the other half of the same rule: an order too small to cover the gap must not suppress
    /// the signal. Suppressing on the existence of an order rather than on its size is how a part
    /// ordered in ones stays permanently out of stock and permanently off the buyer's list.
    /// </summary>
    [Fact]
    public void An_order_too_small_to_cover_the_gap_does_not_suppress_the_signal()
    {
        StockItem stock = Fixture.WithStock(10m);
        stock.SetReplenishmentPolicy(5m, 20m);
        stock.ExpectIncoming(Order, new PurchaseOrderLineRef(Guid.NewGuid()), "PO-1", 1m, Expected);
        stock.ClearDomainEvents();

        stock.Issue(6m, Fixture.SalesOrder(), Fixture.Now);

        stock.NeedsReplenishment.Should().BeTrue();
        stock.DomainEvents.Should().ContainItemsAssignableTo<StockFellBelowReorderPointDomainEvent>();
    }

    /// <summary>The signal carries both figures apart, because the buyer needs them apart.</summary>
    [Fact]
    public void The_signal_says_what_is_on_the_shelf_and_what_is_coming_separately()
    {
        StockItem stock = Fixture.WithStock(10m);
        stock.SetReplenishmentPolicy(8m, 20m);
        stock.ExpectIncoming(Order, new PurchaseOrderLineRef(Guid.NewGuid()), "PO-1", 2m, Expected);
        stock.ClearDomainEvents();

        stock.Issue(6m, Fixture.SalesOrder(), Fixture.Now);

        StockFellBelowReorderPointDomainEvent signal = stock.DomainEvents
            .OfType<StockFellBelowReorderPointDomainEvent>()
            .Single();

        signal.Available.Should().Be(4m);
        signal.OnOrder.Should().Be(2m);
        signal.ReorderPoint.Should().Be(8m);
    }

    [Fact]
    public void Receiving_takes_the_arrival_off_what_is_expected()
    {
        StockItem stock = Fixture.NewStock();
        var line = new PurchaseOrderLineRef(Guid.NewGuid());
        stock.ExpectIncoming(Order, line, "PO-1", 20m, Expected);

        stock.ReceiveIncoming(line, 12m).Value.Should().Be(12m);

        stock.OnOrder.Value.Should().Be(8m);
        stock.Incoming.Single().IsOutstanding.Should().BeTrue();

        stock.ReceiveIncoming(line, 8m);

        stock.OnOrder.Value.Should().Be(0m);
        stock.Incoming.Single().Status.Should().Be(IncomingStockStatus.Received);
    }

    /// <summary>
    /// Suppliers over-deliver. The extra is real stock and the receipt books it onto the shelf,
    /// but it was never on order, so it cannot come off a figure that never counted it — and
    /// letting it would drive on-order negative, which is not a state that means anything.
    /// </summary>
    [Fact]
    public void A_delivery_larger_than_the_order_only_absorbs_what_was_expected()
    {
        StockItem stock = Fixture.NewStock();
        var line = new PurchaseOrderLineRef(Guid.NewGuid());
        stock.ExpectIncoming(Order, line, "PO-1", 20m, Expected);

        stock.ReceiveIncoming(line, 25m).Value.Should().Be(20m);

        stock.OnOrder.Value.Should().Be(0m);
    }

    /// <summary>
    /// A receipt against a line this module never heard of — an order placed before any of this
    /// existed, or a submission that died in the dead letters. The goods are on the shelf either
    /// way, and a bookkeeping counter must never be the reason a delivery cannot be booked in.
    /// </summary>
    [Fact]
    public void A_receipt_for_an_unknown_line_changes_nothing_and_does_not_fail()
    {
        StockItem stock = Fixture.NewStock();
        stock.ExpectIncoming(Order, new PurchaseOrderLineRef(Guid.NewGuid()), "PO-1", 20m, Expected);

        stock.ReceiveIncoming(new PurchaseOrderLineRef(Guid.NewGuid()), 5m).Value.Should().Be(0m);

        stock.OnOrder.Value.Should().Be(20m);
    }

    /// <summary>
    /// The outbox delivers at-least-once. Counting one submission twice would show a part as
    /// twice as covered as it is, which reads as stock arriving that never will.
    /// </summary>
    [Fact]
    public void The_same_order_line_expected_twice_is_only_counted_once()
    {
        StockItem stock = Fixture.NewStock();
        var line = new PurchaseOrderLineRef(Guid.NewGuid());

        stock.ExpectIncoming(Order, line, "PO-1", 20m, Expected);
        stock.ExpectIncoming(Order, line, "PO-1", 20m, Expected).IsSuccess.Should().BeTrue();

        stock.OnOrder.Value.Should().Be(20m);
        stock.Incoming.Should().HaveCount(1);
    }

    [Fact]
    public void An_expectation_has_to_name_the_order_behind_it()
    {
        StockItem stock = Fixture.NewStock();

        stock.ExpectIncoming(
                PurchaseOrderRef.Empty, new PurchaseOrderLineRef(Guid.NewGuid()), "PO-1", 5m, null)
            .Error.Code.Should().Be("inventory.stock.purchase_order_required");

        stock.OnOrder.Value.Should().Be(0m);
    }

    [Fact]
    public void Cancelling_an_order_stops_expecting_everything_still_outstanding_on_it()
    {
        StockItem stock = Fixture.NewStock();
        var line = new PurchaseOrderLineRef(Guid.NewGuid());
        stock.ExpectIncoming(Order, line, "PO-1", 20m, Expected);
        stock.ReceiveIncoming(line, 12m);

        stock.CancelIncoming(Order).Should().Be(1);

        // The eight that never came stop being expected; the twelve that arrived are on the shelf
        // and are none of this method's business.
        stock.OnOrder.Value.Should().Be(0m);
        stock.Incoming.Single().Status.Should().Be(IncomingStockStatus.Cancelled);
    }

    [Fact]
    public void Cancelling_one_order_leaves_another_order_alone()
    {
        StockItem stock = Fixture.NewStock();
        stock.ExpectIncoming(Order, new PurchaseOrderLineRef(Guid.NewGuid()), "PO-1", 20m, Expected);
        stock.ExpectIncoming(OtherOrder, new PurchaseOrderLineRef(Guid.NewGuid()), "PO-2", 5m, Expected);

        stock.CancelIncoming(Order);

        stock.OnOrder.Value.Should().Be(5m);
    }

    /// <summary>
    /// The moment nothing else would notice. No stock moved, and the part just became the most
    /// urgent thing on the buyer's list — because the delivery it was waiting for is not coming.
    /// </summary>
    [Fact]
    public void Losing_the_delivery_puts_the_part_back_on_the_buyers_list()
    {
        StockItem stock = Fixture.WithStock(4m);
        stock.SetReplenishmentPolicy(10m, 20m);
        stock.ExpectIncoming(Order, new PurchaseOrderLineRef(Guid.NewGuid()), "PO-1", 20m, Expected);
        stock.NeedsReplenishment.Should().BeFalse();
        stock.ClearDomainEvents();

        stock.CancelIncoming(Order);

        stock.NeedsReplenishment.Should().BeTrue();
        stock.DomainEvents.Should().ContainItemsAssignableTo<StockFellBelowReorderPointDomainEvent>();
    }

    [Fact]
    public void Cancelling_an_order_this_record_never_expected_changes_nothing()
    {
        StockItem stock = Fixture.NewStock();
        stock.ExpectIncoming(Order, new PurchaseOrderLineRef(Guid.NewGuid()), "PO-1", 20m, Expected);
        stock.ClearDomainEvents();

        stock.CancelIncoming(OtherOrder).Should().Be(0);

        stock.OnOrder.Value.Should().Be(20m);
        stock.DomainEvents.Should().BeEmpty();
    }

    /// <summary>
    /// Cancelling twice is not two cancellations. Redelivery makes it possible, and a version
    /// that subtracted again would drive the expected figure below zero.
    /// </summary>
    [Fact]
    public void Cancelling_the_same_order_twice_only_drops_it_once()
    {
        StockItem stock = Fixture.NewStock();
        stock.ExpectIncoming(Order, new PurchaseOrderLineRef(Guid.NewGuid()), "PO-1", 20m, Expected);

        stock.CancelIncoming(Order).Should().Be(1);
        stock.CancelIncoming(Order).Should().Be(0);

        stock.OnOrder.Value.Should().Be(0m);
    }

    /// <summary>
    /// Projected available is for buying decisions only. A counter that promised it would be
    /// promising goods that are not in the building, on a date nobody has confirmed.
    /// </summary>
    [Fact]
    public void What_is_on_order_never_counts_as_available_to_sell()
    {
        StockItem stock = Fixture.WithStock(2m);
        stock.ExpectIncoming(Order, new PurchaseOrderLineRef(Guid.NewGuid()), "PO-1", 50m, Expected);

        stock.Available.Value.Should().Be(2m);

        stock.Reserve(50m, Fixture.SalesOrder(), Fixture.Now)
            .Error.Code.Should().Be("inventory.stock.insufficient_available");
    }
}
