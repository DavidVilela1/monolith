using AutoPartsErp.Modules.Inventory.Domain;
using AutoPartsErp.Modules.Inventory.Domain.Stock;
using AutoPartsErp.Modules.Inventory.Domain.Stock.Events;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Inventory.Tests;

/// <summary>
/// The rules that keep on-hand, reserved and available in step. These are the ones that cost
/// real money when they are wrong: overselling, phantom stock, and stock figures nobody believes.
/// </summary>
public sealed class StockItemTests
{
    [Fact]
    public void A_new_record_starts_empty()
    {
        StockItem stock = Fixture.NewStock();

        stock.OnHand.Value.Should().Be(0m);
        stock.Reserved.Value.Should().Be(0m);
        stock.Available.Value.Should().Be(0m);
        stock.DomainEvents.Should().ContainItemsAssignableTo<StockRecordOpenedDomainEvent>();
    }

    [Fact]
    public void Receiving_increases_what_is_on_hand_and_available()
    {
        StockItem stock = Fixture.NewStock();

        stock.Receive(10m, Fixture.Receipt(), Fixture.Now).IsSuccess.Should().BeTrue();

        stock.OnHand.Value.Should().Be(10m);
        stock.Available.Value.Should().Be(10m);
    }

    [Fact]
    public void A_movement_records_the_balance_that_followed_it()
    {
        StockItem stock = Fixture.NewStock();
        stock.Receive(10m, Fixture.Receipt(), Fixture.Now);

        Result<StockMovement> second = stock.Receive(5m, Fixture.Receipt("GRN-2"), Fixture.Now);

        second.Value.Quantity.Value.Should().Be(5m);
        second.Value.BalanceAfter.Value.Should().Be(15m);
        second.Value.IsInbound.Should().BeTrue();
    }

    [Fact]
    public void An_issue_is_recorded_as_a_negative_movement()
    {
        StockItem stock = Fixture.WithStock(10m);

        Result<StockMovement> movement = stock.Issue(4m, Fixture.SalesOrder(), Fixture.Now);

        movement.Value.Quantity.Value.Should().Be(-4m);
        movement.Value.BalanceAfter.Value.Should().Be(6m);
        stock.OnHand.Value.Should().Be(6m);
    }

    [Fact]
    public void Issuing_more_than_is_there_is_refused_by_default()
    {
        StockItem stock = Fixture.WithStock(3m);

        Result<StockMovement> movement = stock.Issue(5m, Fixture.SalesOrder(), Fixture.Now);

        movement.IsFailure.Should().BeTrue();
        movement.Error.Code.Should().Be("inventory.stock.insufficient_on_hand");
        stock.OnHand.Value.Should().Be(3m);
    }

    [Fact]
    public void A_warehouse_that_permits_it_may_go_negative()
    {
        StockItem stock = Fixture.WithStock(3m);

        Result<StockMovement> movement =
            stock.Issue(5m, Fixture.SalesOrder(), Fixture.Now, allowNegative: true);

        movement.IsSuccess.Should().BeTrue();
        stock.OnHand.Value.Should().Be(-2m);
    }

    [Fact]
    public void Movement_quantities_must_be_positive()
    {
        StockItem stock = Fixture.WithStock(10m);

        stock.Receive(0m, Fixture.Receipt(), Fixture.Now).Error.Code
            .Should().Be("inventory.stock.quantity_not_positive");
        stock.Issue(-1m, Fixture.SalesOrder(), Fixture.Now).Error.Code
            .Should().Be("inventory.stock.quantity_not_positive");
    }

    [Fact]
    public void Quantities_must_fit_the_unit_the_part_is_counted_in()
    {
        StockItem stock = Fixture.NewStock();

        Result<StockMovement> movement = stock.Receive(2.5m, Fixture.Receipt(), Fixture.Now);

        movement.IsFailure.Should().BeTrue();
        movement.Error.Code.Should().Be("quantity.whole_units_only");
    }
}

/// <summary>Reservations hold stock back without moving it.</summary>
public sealed class ReservationTests
{
    [Fact]
    public void Reserving_reduces_available_but_not_on_hand()
    {
        StockItem stock = Fixture.WithStock(10m);

        stock.Reserve(4m, Fixture.Quote(), Fixture.Now).IsSuccess.Should().BeTrue();

        stock.OnHand.Value.Should().Be(10m);
        stock.Reserved.Value.Should().Be(4m);
        stock.Available.Value.Should().Be(6m);
    }

    [Fact]
    public void Stock_that_is_present_but_promised_cannot_be_reserved_again()
    {
        StockItem stock = Fixture.WithStock(10m);
        stock.Reserve(8m, Fixture.Quote(), Fixture.Now);

        Result<StockReservation> second = stock.Reserve(5m, Fixture.Quote("Q-2"), Fixture.Now);

        second.IsFailure.Should().BeTrue();
        second.Error.Code.Should().Be("inventory.stock.insufficient_available");
    }

    [Fact]
    public void Releasing_a_claim_returns_the_stock_to_available()
    {
        StockItem stock = Fixture.WithStock(10m);
        StockReservation reservation = stock.Reserve(4m, Fixture.Quote(), Fixture.Now).Value;

        stock.Release(reservation.Id).IsSuccess.Should().BeTrue();

        stock.Available.Value.Should().Be(10m);
        stock.Reserved.Value.Should().Be(0m);
        reservation.Status.Should().Be(ReservationStatus.Released);
    }

    [Fact]
    public void A_claim_cannot_be_released_twice()
    {
        StockItem stock = Fixture.WithStock(10m);
        StockReservation reservation = stock.Reserve(4m, Fixture.Quote(), Fixture.Now).Value;
        stock.Release(reservation.Id);

        stock.Release(reservation.Id).Error.Code.Should().Be("inventory.stock.reservation_not_active");
    }

    [Fact]
    public void Fulfilling_a_claim_takes_the_stock_and_clears_the_hold_together()
    {
        StockItem stock = Fixture.WithStock(10m);
        StockReservation reservation = stock.Reserve(4m, Fixture.SalesOrder(), Fixture.Now).Value;

        Result<StockMovement> movement = stock.Fulfil(reservation.Id, Fixture.Now);

        movement.IsSuccess.Should().BeTrue();
        stock.OnHand.Value.Should().Be(6m);
        stock.Reserved.Value.Should().Be(0m);
        stock.Available.Value.Should().Be(6m);
        reservation.Status.Should().Be(ReservationStatus.Fulfilled);
    }

    [Fact]
    public void An_expiry_in_the_past_is_refused()
    {
        StockItem stock = Fixture.WithStock(10m);

        Result<StockReservation> reservation = stock.Reserve(
            1m, Fixture.Quote(), Fixture.Now, Fixture.Now.AddMinutes(-1));

        reservation.Error.Code.Should().Be("inventory.stock.reservation_expiry_past");
    }

    [Fact]
    public void An_abandoned_quote_gives_its_stock_back_when_it_lapses()
    {
        StockItem stock = Fixture.WithStock(10m);
        stock.Reserve(4m, Fixture.Quote(), Fixture.Now, Fixture.Now.AddMinutes(30));

        stock.Available.Value.Should().Be(6m);

        int expired = stock.ExpireLapsedReservations(Fixture.Now.AddHours(1));

        expired.Should().Be(1);
        stock.Available.Value.Should().Be(10m);
        stock.Reserved.Value.Should().Be(0m);
    }

    [Fact]
    public void A_claim_that_has_not_lapsed_is_left_alone()
    {
        StockItem stock = Fixture.WithStock(10m);
        stock.Reserve(4m, Fixture.Quote(), Fixture.Now, Fixture.Now.AddHours(2));

        stock.ExpireLapsedReservations(Fixture.Now.AddMinutes(30)).Should().Be(0);
        stock.Reserved.Value.Should().Be(4m);
    }
}

/// <summary>Counting corrects the balance, and refuses corrections that create impossible states.</summary>
public sealed class StockAdjustmentTests
{
    [Fact]
    public void A_count_records_the_difference_as_a_movement()
    {
        StockItem stock = Fixture.WithStock(10m);

        Result<StockMovement> movement = stock.AdjustTo(7m, Fixture.Count(), Fixture.Now);

        movement.Value.Quantity.Value.Should().Be(-3m);
        movement.Value.BalanceAfter.Value.Should().Be(7m);
        stock.OnHand.Value.Should().Be(7m);
        stock.LastCountedAtUtc.Should().Be(Fixture.Now);
    }

    [Fact]
    public void A_count_that_matches_the_system_is_not_an_adjustment()
    {
        StockItem stock = Fixture.WithStock(10m);

        stock.AdjustTo(10m, Fixture.Count(), Fixture.Now).Error.Code
            .Should().Be("inventory.stock.adjustment_no_change");
    }

    [Fact]
    public void A_count_below_what_is_already_promised_is_refused()
    {
        StockItem stock = Fixture.WithStock(10m);
        stock.Reserve(8m, Fixture.SalesOrder(), Fixture.Now);

        Result<StockMovement> movement = stock.AdjustTo(5m, Fixture.Count(), Fixture.Now);

        movement.IsFailure.Should().BeTrue();
        movement.Error.Code.Should().Be("inventory.stock.count_below_reserved");
        stock.OnHand.Value.Should().Be(10m);
    }

    [Fact]
    public void A_count_cannot_be_negative()
    {
        StockItem stock = Fixture.WithStock(10m);

        stock.AdjustTo(-1m, Fixture.Count(), Fixture.Now).Error.Code
            .Should().Be("inventory.stock.count_negative");
    }
}

/// <summary>Replenishment fires off available stock, not on-hand.</summary>
public sealed class ReplenishmentTests
{
    [Fact]
    public void Both_halves_of_the_policy_are_required()
    {
        StockItem stock = Fixture.WithStock(10m);

        stock.SetReplenishmentPolicy(5m, null).Error.Code
            .Should().Be("inventory.stock.replenishment_incomplete");
    }

    [Fact]
    public void Falling_to_the_reorder_point_raises_an_event()
    {
        StockItem stock = Fixture.WithStock(10m);
        stock.SetReplenishmentPolicy(5m, 20m);

        stock.Issue(5m, Fixture.SalesOrder(), Fixture.Now);

        stock.NeedsReplenishment.Should().BeTrue();
        stock.DomainEvents.Should().ContainItemsAssignableTo<StockFellBelowReorderPointDomainEvent>();
    }

    [Fact]
    public void Reserved_stock_counts_against_the_reorder_point()
    {
        StockItem stock = Fixture.WithStock(10m);
        stock.SetReplenishmentPolicy(5m, 20m);

        // Nothing has physically left, but 6 of the 10 are already promised, so only 4 remain
        // sellable. Replenishment driven off on-hand would miss this entirely and the next
        // customer would be told "in stock" about goods already going out the door.
        stock.Reserve(6m, Fixture.SalesOrder(), Fixture.Now);

        stock.OnHand.Value.Should().Be(10m);
        stock.NeedsReplenishment.Should().BeTrue();
    }
}

internal static class Fixture
{
    public static readonly DateTimeOffset Now = new(2026, 8, 25, 9, 0, 0, TimeSpan.Zero);

    public static StockItem NewStock(UnitOfMeasure? unit = null) =>
        StockItem.Open(new PartRef(Guid.NewGuid()), WarehouseId.New(), unit ?? UnitOfMeasure.Each).Value;

    public static StockItem WithStock(decimal quantity, UnitOfMeasure? unit = null)
    {
        StockItem stock = NewStock(unit);
        stock.Receive(quantity, Receipt(), Now);
        return stock;
    }

    public static MovementReference Receipt(string number = "GRN-1") =>
        MovementReference.Create(ReferenceType.GoodsReceipt, number).Value;

    public static MovementReference SalesOrder(string number = "SO-1") =>
        MovementReference.Create(ReferenceType.SalesOrder, number).Value;

    public static MovementReference Quote(string number = "Q-1") =>
        MovementReference.Create(ReferenceType.Quote, number).Value;

    public static MovementReference Count(string number = "SC-1") =>
        MovementReference.Create(ReferenceType.StockCount, number, "Annual count").Value;
}
