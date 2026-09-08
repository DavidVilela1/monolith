using AutoPartsErp.Modules.Inventory.Domain;
using AutoPartsErp.Modules.Inventory.Domain.Stock;
using AutoPartsErp.Modules.Inventory.Domain.Transfers;
using AutoPartsErp.Modules.Inventory.Domain.Transfers.Events;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Inventory.Tests;

/// <summary>
/// Stock moving between two of the company's own warehouses, and the days it spends on a van.
/// <para>
/// The reason this is a document rather than two movements in one transaction: a transfer that
/// issued from the branch and received into the depot in one breath would put the stock on the
/// depot's shelf on Monday, where a salesperson could promise it to a customer collecting that
/// afternoon. And if the van never arrives, the loss surfaces weeks later at the depot as an
/// unexplained count variance.
/// </para>
/// </summary>
public sealed class StockTransferTests
{
    private static readonly WarehouseId From = WarehouseId.New();
    private static readonly WarehouseId To = WarehouseId.New();
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 8, 0, 0, TimeSpan.Zero);

    private static Money Eur(decimal amount) => Money.Of(amount, Currency.Eur);

    [Fact]
    public void A_new_transfer_is_a_draft_between_two_places()
    {
        StockTransfer transfer = NewTransfer();

        transfer.Status.Should().Be(StockTransferStatus.Draft);
        transfer.FromWarehouseId.Should().Be(From);
        transfer.ToWarehouseId.Should().Be(To);
        transfer.Lines.Should().BeEmpty();
    }

    /// <summary>
    /// Sending stock to the shelf it is already on nets to nothing and leaves two ledger rows
    /// explaining it.
    /// </summary>
    [Fact]
    public void A_transfer_has_to_go_somewhere_else()
    {
        StockTransfer.Draft("TR-2026-00031", From, From)
            .Error.Code.Should().Be("inventory.transfer.same_warehouse");
    }

    [Fact]
    public void A_part_cannot_be_on_the_same_transfer_twice()
    {
        StockTransfer transfer = NewTransfer();
        PartRef part = Part();

        transfer.AddLine(part, "BP-1188", "Brake pads", Quantity.Each(10));

        transfer.AddLine(part, "BP-1188", "Brake pads", Quantity.Each(4))
            .Error.Code.Should().Be("inventory.transfer.part_already_on_transfer");
    }

    [Fact]
    public void Dispatching_puts_the_whole_line_on_the_van_with_its_value()
    {
        (StockTransfer transfer, StockTransferLineId lineId) = TransferWithLine(10);

        transfer.Dispatch(Values(lineId, Eur(40.00m)), "ana", Now).IsSuccess.Should().BeTrue();

        transfer.Status.Should().Be(StockTransferStatus.InTransit);
        transfer.DispatchedBy.Should().Be("ana");
        transfer.HasStockInTransit.Should().BeTrue();

        StockTransferLine line = transfer.Lines.Single();
        line.DispatchedQuantity.Should().Be(Quantity.Each(10));
        line.InTransitQuantity.Should().Be(Quantity.Each(10));
        line.ValueInTransit!.Amount.Should().Be(40.00m);

        transfer.DomainEvents.Should().ContainItemsAssignableTo<StockTransferDispatchedDomainEvent>();
    }

    [Fact]
    public void A_dispatched_transfer_can_no_longer_be_edited_or_cancelled()
    {
        StockTransfer transfer = Dispatched(10, Eur(40.00m));

        transfer.AddLine(Part(), "OF-4471", "Oil filter", Quantity.Each(2))
            .Error.Code.Should().Be("inventory.transfer.not_draft");

        transfer.Cancel("Changed my mind")
            .Error.Code.Should().Be("inventory.transfer.cannot_cancel_in_transit");
    }

    /// <summary>
    /// A transfer can arrive on two vans, and the second one can be a week later. What is still
    /// outstanding stays on the document until it arrives or somebody accepts that it will not.
    /// </summary>
    [Fact]
    public void Receiving_part_of_a_line_leaves_the_rest_on_the_van()
    {
        StockTransfer transfer = Dispatched(10, Eur(40.00m));
        StockTransferLineId lineId = transfer.Lines.Single().Id;

        Money? arrived = transfer.Receive(lineId, Quantity.Each(6), Now).Value;

        arrived!.Amount.Should().Be(24.00m);
        transfer.Status.Should().Be(StockTransferStatus.PartiallyReceived);

        StockTransferLine line = transfer.Lines.Single();
        line.ReceivedQuantity.Should().Be(Quantity.Each(6));
        line.InTransitQuantity.Should().Be(Quantity.Each(4));
        line.ValueInTransit!.Amount.Should().Be(16.00m);
    }

    [Fact]
    public void Receiving_the_last_of_it_closes_the_transfer()
    {
        StockTransfer transfer = Dispatched(10, Eur(40.00m));
        StockTransferLineId lineId = transfer.Lines.Single().Id;

        transfer.Receive(lineId, Quantity.Each(6), Now);
        transfer.Receive(lineId, Quantity.Each(4), Now.AddDays(1));

        transfer.Status.Should().Be(StockTransferStatus.Received);
        transfer.CompletedAtUtc.Should().Be(Now.AddDays(1));
        transfer.HasStockInTransit.Should().BeFalse();
        transfer.DomainEvents.Should().ContainItemsAssignableTo<StockTransferReceivedDomainEvent>();
    }

    /// <summary>
    /// The value has to arrive exactly, or a van journey between two branches would quietly
    /// revalue the company's stock. The last arrival takes whatever is left rather than its
    /// proportional share, for the same reason emptying a shelf does: proportions round.
    /// </summary>
    [Fact]
    public void The_value_that_left_is_exactly_the_value_that_arrives()
    {
        StockTransfer transfer = Dispatched(3, Eur(10.00m));
        StockTransferLineId lineId = transfer.Lines.Single().Id;

        Money? first = transfer.Receive(lineId, Quantity.Each(1), Now).Value;
        Money? second = transfer.Receive(lineId, Quantity.Each(1), Now).Value;
        Money? third = transfer.Receive(lineId, Quantity.Each(1), Now).Value;

        (first!.Amount + second!.Amount + third!.Amount).Should().Be(10.00m);
        transfer.Lines.Single().ValueInTransit!.Amount.Should().Be(0m);
    }

    [Fact]
    public void Booking_in_more_than_ever_left_is_refused()
    {
        StockTransfer transfer = Dispatched(10, Eur(40.00m));
        StockTransferLineId lineId = transfer.Lines.Single().Id;

        transfer.Receive(lineId, Quantity.Each(11), Now)
            .Error.Code.Should().Be("inventory.transfer.over_received");

        transfer.Lines.Single().ReceivedQuantity.Value.Should().Be(0m);
    }

    [Fact]
    public void Nothing_can_be_received_before_the_transfer_has_left()
    {
        (StockTransfer transfer, StockTransferLineId lineId) = TransferWithLine(10);

        transfer.Receive(lineId, Quantity.Each(1), Now)
            .Error.Code.Should().Be("inventory.transfer.not_in_transit");
    }

    /// <summary>
    /// Four boxes never turned up. The stock left the sending warehouse and its value left with
    /// it, so the company is already short; what the write-off adds is a document saying so, with
    /// a reason, instead of a transfer that sits open for ever pretending goods are still moving.
    /// </summary>
    [Fact]
    public void Closing_short_writes_off_what_is_still_on_the_van_and_says_what_it_cost()
    {
        StockTransfer transfer = Dispatched(10, Eur(40.00m));
        StockTransferLineId lineId = transfer.Lines.Single().Id;
        transfer.Receive(lineId, Quantity.Each(6), Now);
        transfer.ClearDomainEvents();

        transfer.CloseShort("Pallet damaged in transit; carrier notified", Now.AddDays(1))
            .IsSuccess.Should().BeTrue();

        transfer.Status.Should().Be(StockTransferStatus.ClosedShort);
        transfer.HasStockInTransit.Should().BeFalse();

        StockTransferLine line = transfer.Lines.Single();
        line.LostQuantity.Should().Be(Quantity.Each(4));
        line.ValueInTransit!.Amount.Should().Be(0m);

        StockTransferClosedShortDomainEvent lost = transfer.DomainEvents
            .OfType<StockTransferClosedShortDomainEvent>()
            .Single();

        lost.LostValue.Should().Be(16.00m);
        lost.Reason.Should().Contain("Pallet damaged");
    }

    [Fact]
    public void Accepting_a_shortfall_needs_a_reason()
    {
        StockTransfer transfer = Dispatched(10, Eur(40.00m));

        transfer.CloseShort("  ", Now)
            .Error.Code.Should().Be("inventory.transfer.close_reason_required");

        transfer.Status.Should().Be(StockTransferStatus.InTransit);
    }

    [Fact]
    public void There_is_no_shortfall_to_accept_once_everything_has_arrived()
    {
        StockTransfer transfer = Dispatched(10, Eur(40.00m));
        transfer.Receive(transfer.Lines.Single().Id, Quantity.Each(10), Now);

        transfer.CloseShort("Nothing missing", Now)
            .Error.Code.Should().Be("inventory.transfer.not_in_transit");
    }

    /// <summary>
    /// Stock that has never been through a priced receipt has no value to carry. Null rather
    /// than zero: "nobody ever knew what this cost" is a different fact from "this is worthless",
    /// and only the first one is true.
    /// </summary>
    [Fact]
    public void Moving_stock_that_was_never_priced_carries_no_value()
    {
        (StockTransfer transfer, StockTransferLineId lineId) = TransferWithLine(10);
        transfer.Dispatch(Values(lineId, null), "ana", Now);

        transfer.Lines.Single().ValueInTransit.Should().BeNull();

        transfer.Receive(lineId, Quantity.Each(10), Now).Value.Should().BeNull();
    }

    [Fact]
    public void A_transfer_cannot_leave_empty()
    {
        NewTransfer()
            .Dispatch(new Dictionary<StockTransferLineId, Money?>(), "ana", Now)
            .Error.Code.Should().Be("inventory.transfer.no_lines");
    }

    /// <summary>
    /// A transfer leaves whole or not at all, so a line the sending warehouse could not fill has
    /// to come off the note first. Silently dispatching it as zero would put a line on the
    /// receiving end that nobody is ever going to be able to book in.
    /// </summary>
    [Fact]
    public void Every_line_has_to_be_accounted_for_at_dispatch()
    {
        StockTransfer transfer = NewTransfer();
        StockTransferLineId first = transfer
            .AddLine(Part(), "BP-1188", "Brake pads", Quantity.Each(10)).Value;
        transfer.AddLine(Part(), "OF-4471", "Oil filter", Quantity.Each(4));

        transfer.Dispatch(Values(first, Eur(40.00m)), "ana", Now)
            .Error.Code.Should().Be("inventory.transfer.line_not_dispatched");

        transfer.Status.Should().Be(StockTransferStatus.Draft);
    }

    [Fact]
    public void A_draft_can_be_cancelled_with_a_reason_and_lines_can_be_removed()
    {
        StockTransfer transfer = NewTransfer();
        StockTransferLineId first = transfer
            .AddLine(Part(), "BP-1188", "Brake pads", Quantity.Each(10)).Value;
        transfer.AddLine(Part(), "OF-4471", "Oil filter", Quantity.Each(4));

        transfer.RemoveLine(first).IsSuccess.Should().BeTrue();
        transfer.Lines.Should().HaveCount(1);
        transfer.Lines.Single().LineNumber.Should().Be(1);

        transfer.Cancel("  ").Error.Code.Should().Be("inventory.transfer.close_reason_required");
        transfer.Cancel("Depot has enough after all").IsSuccess.Should().BeTrue();
        transfer.Status.Should().Be(StockTransferStatus.Cancelled);
    }

    private static PartRef Part() => new(Guid.NewGuid());

    private static StockTransfer NewTransfer() =>
        StockTransfer.Draft("TR-2026-00031", From, To).Value;

    private static (StockTransfer Transfer, StockTransferLineId LineId) TransferWithLine(int quantity)
    {
        StockTransfer transfer = NewTransfer();
        StockTransferLineId lineId = transfer
            .AddLine(Part(), "BP-1188", "Brake pad set", Quantity.Each(quantity))
            .Value;

        return (transfer, lineId);
    }

    private static StockTransfer Dispatched(int quantity, Money? value)
    {
        (StockTransfer transfer, StockTransferLineId lineId) = TransferWithLine(quantity);
        transfer.Dispatch(Values(lineId, value), "ana", Now);
        transfer.ClearDomainEvents();

        return transfer;
    }

    private static Dictionary<StockTransferLineId, Money?> Values(
        StockTransferLineId lineId,
        Money? value) =>
        new() { [lineId] = value };
}

/// <summary>
/// The two ends of a transfer, at the balances.
/// <para>
/// Separate from the document tests because this is the part that must not lose money. Stock
/// moving between two of the company's own shelves cannot change what the company owns, and
/// nothing in the aggregate can enforce that on its own — it takes both balances to see it.
/// </para>
/// </summary>
public sealed class StockTransferBalanceTests
{
    private static Money Eur(decimal amount) => Money.Of(amount, Currency.Eur);

    [Fact]
    public void What_leaves_one_shelf_is_exactly_what_lands_on_the_other()
    {
        StockItem branch = Fixture.NewStock();
        branch.Receive(10m, Fixture.Receipt("GRN-1"), Fixture.Now, Eur(4.00m));
        branch.Receive(30m, Fixture.Receipt("GRN-2"), Fixture.Now, Eur(6.00m));

        StockItem depot = Fixture.NewStock();

        TransferredStock sent = branch
            .Dispatch(10m, Fixture.Transfer(), Fixture.Now)
            .Value;

        depot.ReceiveValued(10m, Fixture.Transfer(), Fixture.Now, sent.Value);

        // 220 across 40 units; ten of them are worth 55.
        sent.Value!.Amount.Should().Be(55.00m);
        branch.StockValue.Amount.Should().Be(165.00m);
        depot.StockValue.Amount.Should().Be(55.00m);

        // The company owns exactly what it owned before the van left.
        (branch.StockValue.Amount + depot.StockValue.Amount).Should().Be(220.00m);
    }

    [Fact]
    public void The_ledger_records_a_transfer_out_and_a_transfer_in()
    {
        StockItem branch = Fixture.WithStock(10m);
        StockItem depot = Fixture.NewStock();

        TransferredStock sent = branch.Dispatch(4m, Fixture.Transfer(), Fixture.Now).Value;
        StockMovement arrival = depot
            .ReceiveValued(4m, Fixture.Transfer(), Fixture.Now, sent.Value)
            .Value;

        sent.Movement.Type.Should().Be(MovementType.TransferOut);
        sent.Movement.Quantity.Value.Should().Be(-4m);
        arrival.Type.Should().Be(MovementType.TransferIn);
        arrival.Quantity.Value.Should().Be(4m);
    }

    [Fact]
    public void A_warehouse_cannot_send_what_it_does_not_have()
    {
        StockItem branch = Fixture.WithStock(3m);

        branch.Dispatch(4m, Fixture.Transfer(), Fixture.Now)
            .Error.Code.Should().Be("inventory.stock.insufficient_on_hand");

        branch.OnHand.Value.Should().Be(3m);
    }

    /// <summary>
    /// Sending stock is what makes a branch run low, and it is the moment nothing else would
    /// notice — no sale happened.
    /// </summary>
    [Fact]
    public void Sending_stock_can_put_the_sending_warehouse_below_its_reorder_point()
    {
        StockItem branch = Fixture.WithStock(10m);
        branch.SetReplenishmentPolicy(5m, 20m);
        branch.ClearDomainEvents();

        branch.Dispatch(6m, Fixture.Transfer(), Fixture.Now);

        branch.NeedsReplenishment.Should().BeTrue();
        branch.DomainEvents.Should()
            .ContainItemsAssignableTo<Domain.Stock.Events.StockFellBelowReorderPointDomainEvent>();
    }
}
