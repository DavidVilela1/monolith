using AutoPartsErp.Modules.Inventory.Domain.Stock;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Inventory.Tests;

/// <summary>
/// What stock is worth, and what it costs when it leaves.
/// <para>
/// Moving weighted average, and the thing to hold on to while reading these is that the
/// <em>value</em> is stored and the average is derived. Storing the average instead is the
/// obvious design and it is wrong: <see cref="Money"/> rounds to the currency's decimal places,
/// so a per-unit figure would round on every receipt and the error would compound for the life of
/// the part, until the ledger no longer explained the balance sheet.
/// </para>
/// <para>
/// The tests below are also the specification of the seam. Everything that adds stock goes
/// through the receipt, everything that removes it goes through one private method, and outside
/// this module a cost only ever appears as a value stamped on a ledger row — which is what makes
/// FIFO later a change to two methods rather than a change to four modules.
/// </para>
/// </summary>
public sealed class StockCostingTests
{
    private static Money Eur(decimal amount) => Money.Of(amount, Currency.Eur);

    [Fact]
    public void A_new_record_is_worth_nothing_and_has_no_average()
    {
        StockItem stock = Fixture.NewStock();

        stock.StockValue.Amount.Should().Be(0m);
        stock.AverageCost.Should().BeNull();
    }

    [Fact]
    public void Receiving_at_a_price_values_the_shelf_and_stamps_the_movement()
    {
        StockItem stock = Fixture.NewStock();

        StockMovement movement = stock
            .Receive(10m, Fixture.Receipt(), Fixture.Now, Eur(4.00m))
            .Value;

        stock.StockValue.Amount.Should().Be(40.00m);
        stock.AverageCost!.Amount.Should().Be(4.00m);
        movement.CostValue!.Amount.Should().Be(40.00m);
        movement.UnitCost!.Amount.Should().Be(4.00m);
    }

    /// <summary>The whole point of a weighted average: the second price does not replace the first.</summary>
    [Fact]
    public void A_second_receipt_at_a_different_price_moves_the_average_by_weight()
    {
        StockItem stock = Fixture.NewStock();

        stock.Receive(10m, Fixture.Receipt("GRN-1"), Fixture.Now, Eur(4.00m));
        stock.Receive(30m, Fixture.Receipt("GRN-2"), Fixture.Now, Eur(6.00m));

        // 40 + 180 over 40 units. Not 5.00, which is what averaging the two prices would give
        // and what everybody guesses.
        stock.StockValue.Amount.Should().Be(220.00m);
        stock.AverageCost!.Amount.Should().Be(5.50m);
    }

    [Fact]
    public void Issuing_takes_value_off_at_the_average_and_stamps_what_it_took()
    {
        StockItem stock = Fixture.NewStock();
        stock.Receive(10m, Fixture.Receipt("GRN-1"), Fixture.Now, Eur(4.00m));
        stock.Receive(30m, Fixture.Receipt("GRN-2"), Fixture.Now, Eur(6.00m));

        StockMovement movement = stock.Issue(10m, Fixture.SalesOrder(), Fixture.Now).Value;

        movement.CostValue!.Amount.Should().Be(55.00m);
        movement.UnitCost!.Amount.Should().Be(5.50m);
        stock.StockValue.Amount.Should().Be(165.00m);

        // And the average is unchanged by a sale, which is the property that makes it an average.
        stock.AverageCost!.Amount.Should().Be(5.50m);
    }

    /// <summary>
    /// The reason the value is stored and the unit cost derived. Three thousand at 35 cents plus
    /// one at a euro averages €0.3502, which a stored per-unit figure would round to €0.35 — and
    /// the issue of three thousand would then report €1,050.00 for a movement that actually took
    /// €1,050.65 off the balance sheet. Sixty-five cents on one line, every line, for ever.
    /// </summary>
    [Fact]
    public void A_price_that_does_not_divide_evenly_does_not_leak_cents()
    {
        StockItem stock = Fixture.NewStock();
        stock.Receive(3000m, Fixture.Receipt("GRN-1"), Fixture.Now, Eur(0.35m));
        stock.Receive(1m, Fixture.Receipt("GRN-2"), Fixture.Now, Eur(1.00m));

        stock.StockValue.Amount.Should().Be(1051.00m);

        StockMovement movement = stock.Issue(3000m, Fixture.SalesOrder(), Fixture.Now).Value;

        // Value out plus value left is exactly value in. No version of this that multiplies a
        // rounded per-unit figure can promise that.
        (movement.CostValue!.Amount + stock.StockValue.Amount).Should().Be(1051.00m);
    }

    /// <summary>
    /// Emptying the shelf empties the value, to the cent. Proportions round, and rounding leaves
    /// a few cents of value against a shelf with nothing on it — a balance sheet claiming the
    /// company owns €0.03 of a part it has none of. Somebody eventually writes a routine to clean
    /// that up; not creating it is cheaper.
    /// </summary>
    [Fact]
    public void Selling_the_last_of_it_leaves_no_value_behind()
    {
        StockItem stock = Fixture.NewStock();
        stock.Receive(3m, Fixture.Receipt(), Fixture.Now, Eur(10.00m));

        stock.Issue(1m, Fixture.SalesOrder("SO-1"), Fixture.Now);
        stock.Issue(1m, Fixture.SalesOrder("SO-2"), Fixture.Now);
        StockMovement last = stock.Issue(1m, Fixture.SalesOrder("SO-3"), Fixture.Now).Value;

        stock.OnHand.Value.Should().Be(0m);
        stock.StockValue.Amount.Should().Be(0m);
        stock.AverageCost.Should().BeNull();
        last.CostValue!.Amount.Should().Be(10.00m);
    }

    /// <summary>
    /// A transfer in or a customer return knows no purchase price, so it comes in at what the
    /// shelf is already worth and the average does not move. Adding the quantity with no value
    /// would be calling the goods free — the average would be diluted towards nothing, and the
    /// first return would wreck the valuation of a part sold for years.
    /// </summary>
    [Fact]
    public void A_receipt_with_no_price_comes_in_at_the_average_and_does_not_move_it()
    {
        StockItem stock = Fixture.NewStock();
        stock.Receive(10m, Fixture.Receipt(), Fixture.Now, Eur(4.00m));

        StockMovement movement = stock
            .Receive(10m, Fixture.Receipt("TRF-1"), Fixture.Now)
            .Value;

        stock.OnHand.Value.Should().Be(20m);
        stock.StockValue.Amount.Should().Be(80.00m);
        stock.AverageCost!.Amount.Should().Be(4.00m);
        movement.CostValue!.Amount.Should().Be(40.00m);
    }

    /// <summary>
    /// The same receipt into a shelf that has never been priced. There is no average to apply, so
    /// the quantity joins the balance uncosted — reported as no cost rather than a cost of zero,
    /// because "we do not know what this cost" and "this cost nothing" are different facts and
    /// only one of them is true.
    /// </summary>
    [Fact]
    public void A_receipt_with_no_price_into_a_never_priced_shelf_stays_uncosted()
    {
        StockItem stock = Fixture.WithStock(10m);

        StockMovement movement = stock
            .Receive(10m, Fixture.Receipt("TRF-1"), Fixture.Now)
            .Value;

        stock.OnHand.Value.Should().Be(20m);
        stock.StockValue.Amount.Should().Be(0m);
        movement.CostValue.Should().BeNull();
    }

    /// <summary>
    /// Buying in dollars against stock valued in euros. Refused rather than converted: there is
    /// no exchange rate anywhere in this system, and converting with an invented one puts a
    /// number on the balance sheet nobody can trace back to a decision.
    /// </summary>
    [Fact]
    public void A_receipt_priced_in_another_currency_is_refused_and_changes_nothing()
    {
        StockItem stock = Fixture.NewStock();
        stock.Receive(10m, Fixture.Receipt(), Fixture.Now, Eur(4.00m));

        stock.Receive(5m, Fixture.Receipt("GRN-2"), Fixture.Now, Money.Of(6.00m, Currency.Usd))
            .Error.Code.Should().Be("inventory.stock.cost_currency_mismatch");

        stock.OnHand.Value.Should().Be(10m);
        stock.StockValue.Amount.Should().Be(40.00m);
    }

    /// <summary>
    /// A count that found less takes value out at the average, like any other departure; a count
    /// that found more takes the average with it. The extra units are the same part off the same
    /// shelf, and the only defensible price for them is what the rest cost — zero would say the
    /// company found something worthless, and a purchase price would have to be invented.
    /// </summary>
    [Fact]
    public void A_count_moves_value_in_the_direction_it_moves_stock()
    {
        StockItem undercount = Fixture.NewStock();
        undercount.Receive(10m, Fixture.Receipt(), Fixture.Now, Eur(4.00m));
        undercount.AdjustTo(8m, Fixture.Count(), Fixture.Now);

        undercount.StockValue.Amount.Should().Be(32.00m);
        undercount.AverageCost!.Amount.Should().Be(4.00m);

        StockItem overcount = Fixture.NewStock();
        overcount.Receive(10m, Fixture.Receipt(), Fixture.Now, Eur(4.00m));
        overcount.AdjustTo(12m, Fixture.Count(), Fixture.Now);

        overcount.StockValue.Amount.Should().Be(48.00m);
        overcount.AverageCost!.Amount.Should().Be(4.00m);
    }

    [Fact]
    public void Fulfilling_a_reservation_costs_the_same_as_issuing_it()
    {
        StockItem stock = Fixture.NewStock();
        stock.Receive(10m, Fixture.Receipt(), Fixture.Now, Eur(4.00m));

        StockReservation reservation = stock
            .Reserve(4m, Fixture.SalesOrder(), Fixture.Now)
            .Value;

        StockMovement movement = stock.Fulfil(reservation.Id, Fixture.Now).Value;

        movement.CostValue!.Amount.Should().Be(16.00m);
        stock.StockValue.Amount.Should().Be(24.00m);
    }

    /// <summary>
    /// A part received before costing existed, or only ever transferred in. It has quantity and
    /// no value, and an issue against it must not invent one — a null cost is the honest answer
    /// and is what tells anybody reading the ledger that this stock was never priced.
    /// </summary>
    [Fact]
    public void Issuing_stock_that_was_never_priced_stamps_no_cost()
    {
        StockItem stock = Fixture.WithStock(10m);

        StockMovement movement = stock.Issue(4m, Fixture.SalesOrder(), Fixture.Now).Value;

        movement.CostValue.Should().BeNull();
        movement.UnitCost.Should().BeNull();
        stock.StockValue.Amount.Should().Be(0m);
    }

    /// <summary>
    /// Buying back into an empty shelf. There is no old average to weight against, so the
    /// receipt price is simply the new one — the formula would otherwise be dividing by the
    /// quantity that just arrived and weighting it against nothing.
    /// </summary>
    [Fact]
    public void Restocking_an_empty_shelf_takes_the_new_price_outright()
    {
        StockItem stock = Fixture.NewStock();
        stock.Receive(10m, Fixture.Receipt("GRN-1"), Fixture.Now, Eur(4.00m));
        stock.Issue(10m, Fixture.SalesOrder(), Fixture.Now);

        stock.Receive(5m, Fixture.Receipt("GRN-2"), Fixture.Now, Eur(9.00m));

        stock.StockValue.Amount.Should().Be(45.00m);
        stock.AverageCost!.Amount.Should().Be(9.00m);
    }
}
