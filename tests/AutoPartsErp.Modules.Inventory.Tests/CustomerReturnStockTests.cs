using AutoPartsErp.Modules.Inventory.Domain.Stock;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Inventory.Tests;

/// <summary>
/// Goods coming back from a customer, and what they are worth when they land.
/// <para>
/// The whole of this file is one argument: a return is worth what it cost when it left, not what
/// the shelf averages the day it comes back. The difference is a silent profit or loss booked
/// against a transaction where the company made nothing, and enough of them make the stock value
/// a number nobody can explain.
/// </para>
/// </summary>
public sealed class CustomerReturnStockTests
{
    private static Money Eur(decimal amount) => Money.Of(amount, Currency.Eur);

    private static MovementReference Return(string number = "RET-1", string? note = "Line A") =>
        MovementReference.Create(ReferenceType.CustomerReturn, number, note).Value;

    private static MovementReference OrderLine(string number = "SO-1", string? note = "Line A") =>
        MovementReference.Create(ReferenceType.SalesOrder, number, note).Value;

    /// <summary>
    /// The headline. Sold off a €4.00 shelf, then the shelf averages down to €3.00 — and the
    /// return still lands at what it left at. Booking it at today's average would put €3.00 back
    /// where €4.00 came out and quietly make a euro of profit on a sale that was reversed.
    /// </summary>
    [Fact]
    public void A_return_lands_at_what_it_cost_when_it_left_not_at_todays_average()
    {
        StockItem stock = Fixture.NewStock();
        stock.Receive(10m, Fixture.Receipt("GRN-1"), Fixture.Now, Eur(4.00m));

        StockMovement issue = stock.Issue(4m, OrderLine(), Fixture.Now).Value;
        issue.CostValue!.Amount.Should().Be(16.00m);

        // The shelf moves on: 6 left at 4.00 plus 10 in at 2.40 averages to 3.00.
        stock.Receive(10m, Fixture.Receipt("GRN-2"), Fixture.Now, Eur(2.40m));
        stock.AverageCost!.Amount.Should().Be(3.00m);

        Money? value = StockMovement.ValueOfReturn([issue], returnedQuantity: 4m);
        value!.Amount.Should().Be(16.00m);

        StockMovement back = stock.ReceiveReturn(4m, Return(), Fixture.Now, value).Value;

        back.Type.Should().Be(MovementType.CustomerReturn);
        back.CostValue!.Amount.Should().Be(16.00m);
        stock.OnHand.Value.Should().Be(20m);
        stock.StockValue.Amount.Should().Be(64.00m);
    }

    /// <summary>
    /// Part of a line comes back and it is worth its share. Computed as a fraction of what left
    /// rather than as a unit cost multiplied up, because rounding twice on three of seven is how
    /// a ledger stops adding up to the balance it explains.
    /// </summary>
    [Fact]
    public void Part_of_a_line_comes_back_worth_its_share_of_what_left()
    {
        StockItem stock = Fixture.NewStock();
        stock.Receive(7m, Fixture.Receipt(), Fixture.Now, Eur(1.00m));

        StockMovement issue = stock.Issue(7m, OrderLine(), Fixture.Now).Value;
        issue.CostValue!.Amount.Should().Be(7.00m);

        StockMovement.ValueOfReturn([issue], 3m)!.Amount.Should().Be(3.00m);
    }

    /// <summary>
    /// A line shipped twice at two costs has no single unit cost, and the customer bringing some
    /// of it back does not say which van it came on. The average of what left is the only honest
    /// answer available.
    /// </summary>
    [Fact]
    public void A_line_shipped_twice_at_two_costs_comes_back_at_the_average_of_what_left()
    {
        StockItem stock = Fixture.NewStock();

        stock.Receive(10m, Fixture.Receipt("GRN-1"), Fixture.Now, Eur(2.00m));
        StockMovement first = stock.Issue(10m, OrderLine(), Fixture.Now).Value;

        stock.Receive(10m, Fixture.Receipt("GRN-2"), Fixture.Now, Eur(6.00m));
        StockMovement second = stock.Issue(10m, OrderLine(), Fixture.Now).Value;

        // 20.00 and 60.00 over twenty units. Half of it back is half of eighty.
        StockMovement.ValueOfReturn([first, second], 20m)!.Amount.Should().Be(80.00m);
        StockMovement.ValueOfReturn([first, second], 10m)!.Amount.Should().Be(40.00m);
    }

    /// <summary>
    /// Only what went out counts. The ledger rows for a line include the return itself once one
    /// has been booked, and counting that would value the second return off the first.
    /// </summary>
    [Fact]
    public void What_came_back_is_not_counted_as_something_that_left()
    {
        StockItem stock = Fixture.NewStock();
        stock.Receive(10m, Fixture.Receipt(), Fixture.Now, Eur(5.00m));

        StockMovement issue = stock.Issue(10m, OrderLine(), Fixture.Now).Value;
        StockMovement back = stock.ReceiveReturn(2m, Return(), Fixture.Now, Eur(10.00m)).Value;

        // Still 50.00 over ten units, so two more back are worth ten.
        StockMovement.ValueOfReturn([issue, back], 2m)!.Amount.Should().Be(10.00m);
    }

    /// <summary>
    /// A costless issue is not an issue that cost nothing. Counting it on both sides of the
    /// fraction would halve the figure and write the difference off without saying so.
    /// </summary>
    [Fact]
    public void Stock_that_left_uncosted_does_not_drag_the_figure_down()
    {
        StockItem stock = Fixture.NewStock();

        // Received with no price at all, then issued: the movement carries no value.
        stock.Receive(5m, Fixture.Receipt("GRN-0"), Fixture.Now);
        StockMovement uncosted = stock.Issue(5m, OrderLine(), Fixture.Now).Value;
        uncosted.CostValue.Should().BeNull();

        stock.Receive(5m, Fixture.Receipt("GRN-1"), Fixture.Now, Eur(4.00m));
        StockMovement costed = stock.Issue(5m, OrderLine(), Fixture.Now).Value;
        costed.CostValue!.Amount.Should().Be(20.00m);

        StockMovement.ValueOfReturn([uncosted, costed], 1m)!.Amount.Should().Be(4.00m);
    }

    /// <summary>
    /// Nothing that left had a value, so nothing comes back with one. Null rather than zero: a
    /// shelf that has never been priced is a real state, and "free" is a different claim.
    /// </summary>
    [Fact]
    public void A_return_of_never_priced_stock_comes_back_uncosted()
    {
        StockItem stock = Fixture.NewStock();
        stock.Receive(5m, Fixture.Receipt(), Fixture.Now);

        StockMovement issue = stock.Issue(5m, OrderLine(), Fixture.Now).Value;

        StockMovement.ValueOfReturn([issue], 5m).Should().BeNull();
        StockMovement.ValueOfReturn([], 5m).Should().BeNull();

        StockMovement back = stock.ReceiveReturn(5m, Return(), Fixture.Now, null).Value;

        back.CostValue.Should().BeNull();
        stock.OnHand.Value.Should().Be(5m);
        stock.StockValue.Amount.Should().Be(0m);
    }

    [Fact]
    public void A_return_in_the_wrong_currency_is_refused_before_the_balance_moves()
    {
        StockItem stock = Fixture.NewStock();
        stock.Receive(10m, Fixture.Receipt(), Fixture.Now, Eur(4.00m));

        stock.ReceiveReturn(1m, Return(), Fixture.Now, Money.Of(4.00m, Currency.Usd))
            .Error.Code.Should().Be("inventory.stock.cost_currency_mismatch");

        stock.OnHand.Value.Should().Be(10m);
        stock.StockValue.Amount.Should().Be(40.00m);
    }

    [Fact]
    public void A_return_of_nothing_is_refused()
    {
        StockItem stock = Fixture.WithStock(10m);

        stock.ReceiveReturn(0m, Return(), Fixture.Now, null).IsFailure.Should().BeTrue();
        stock.ReceiveReturn(-1m, Return(), Fixture.Now, null).IsFailure.Should().BeTrue();
    }
}
