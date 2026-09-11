using AutoPartsErp.Modules.Inventory.Domain.Stock;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Inventory.Tests;

/// <summary>
/// What happens when the supplier's invoice says the delivery cost something else.
/// <para>
/// A receipt is booked in at the purchase order's price, because that is the only figure anybody
/// has while a van is being unloaded. The invoice arrives days later with a different one, and
/// until the difference reaches the shelf the balance sheet disagrees with the money about to
/// leave the bank.
/// </para>
/// <para>
/// The movement this writes is the only kind in the ledger with no quantity on it. Nothing
/// arrived and nothing left; only the value moved, and that is the whole shape of the fact.
/// </para>
/// </summary>
public sealed class PriceVarianceTests
{
    private static Money Eur(decimal amount) => Money.Of(amount, Currency.Eur);

    private static MovementReference Invoice(string number = "FT 2026/14872") =>
        MovementReference.Create(
            ReferenceType.SupplierInvoice, number, "Purchase order line 1").Value;

    /// <summary>
    /// A hundred booked in at 4,50 and invoiced at 4,61 is eleven euros the shelf was not carrying.
    /// </summary>
    [Fact]
    public void A_supplier_charging_more_makes_the_shelf_worth_more()
    {
        StockItem stock = Fixture.NewStock();
        stock.Receive(100m, Fixture.Receipt(), Fixture.Now, Eur(4.50m));

        StockMovement movement = stock.Revalue(Eur(11.00m), Invoice(), Fixture.Now).Value;

        stock.StockValue.Amount.Should().Be(461.00m);
        stock.AverageCost!.Amount.Should().Be(4.61m);

        movement.Type.Should().Be(MovementType.PriceVariance);
        movement.CostValue!.Amount.Should().Be(11.00m);
    }

    /// <summary>
    /// Nothing arrived and nothing left. A ledger row with a quantity on it would put a receipt
    /// of a hundred-and-something on a shelf nobody delivered to.
    /// </summary>
    [Fact]
    public void The_balance_does_not_move_only_the_value()
    {
        StockItem stock = Fixture.NewStock();
        stock.Receive(100m, Fixture.Receipt(), Fixture.Now, Eur(4.50m));

        StockMovement movement = stock.Revalue(Eur(11.00m), Invoice(), Fixture.Now).Value;

        movement.Quantity.Value.Should().Be(0m);
        movement.BalanceAfter.Value.Should().Be(100m);
        stock.OnHand.Value.Should().Be(100m);

        // No unit cost on the row: dividing a value by nothing is not a per-unit figure.
        movement.UnitCost.Should().BeNull();
    }

    /// <summary>
    /// A supplier who charged less than the order said makes the shelf worth less. A correction
    /// that only ever went upwards would quietly keep every favourable invoice off the accounts.
    /// </summary>
    [Fact]
    public void A_supplier_charging_less_makes_the_shelf_worth_less()
    {
        StockItem stock = Fixture.NewStock();
        stock.Receive(100m, Fixture.Receipt(), Fixture.Now, Eur(4.50m));

        stock.Revalue(Eur(-30.00m), Invoice(), Fixture.Now).IsSuccess.Should().BeTrue();

        stock.StockValue.Amount.Should().Be(420.00m);
        stock.AverageCost!.Amount.Should().Be(4.20m);
    }

    /// <summary>
    /// Most deliveries are invoiced at the price they were ordered at. A ledger row for every one
    /// of those would bury the rows that matter under thousands that do not.
    /// </summary>
    [Fact]
    public void A_difference_of_nothing_writes_nothing()
    {
        StockItem stock = Fixture.NewStock();
        stock.Receive(100m, Fixture.Receipt(), Fixture.Now, Eur(4.50m));

        Result<StockMovement> movement = stock.Revalue(Eur(0m), Invoice(), Fixture.Now);

        movement.Error.Code.Should().Be("inventory.stock.no_variance");
        stock.StockValue.Amount.Should().Be(450.00m);
    }

    /// <summary>
    /// A negative stock value is not a fact about any warehouse. Something earlier is wrong, and
    /// letting this through would hide it under a number nobody can interpret.
    /// </summary>
    [Fact]
    public void A_correction_cannot_take_the_shelf_below_nothing()
    {
        StockItem stock = Fixture.NewStock();
        stock.Receive(100m, Fixture.Receipt(), Fixture.Now, Eur(4.50m));

        Result<StockMovement> movement = stock.Revalue(Eur(-460.00m), Invoice(), Fixture.Now);

        movement.Error.Code.Should().Be("inventory.stock.variance_below_zero");
        stock.StockValue.Amount.Should().Be(450.00m);
    }

    /// <summary>
    /// A correction in another currency is refused rather than converted, like every other figure
    /// that reaches this module from outside it.
    /// </summary>
    [Fact]
    public void A_correction_in_another_currency_is_refused()
    {
        StockItem stock = Fixture.NewStock();
        stock.Receive(100m, Fixture.Receipt(), Fixture.Now, Eur(4.50m));

        stock.Revalue(Money.Of(11.00m, Currency.Usd), Invoice(), Fixture.Now)
            .Error.Code.Should().Be("inventory.stock.cost_currency_mismatch");
    }

    /// <summary>
    /// The reference points at the supplier's own document. "Why did this shelf change value on a
    /// day nothing moved?" is answered by the piece of paper that caused it.
    /// </summary>
    [Fact]
    public void The_row_points_at_the_suppliers_own_document()
    {
        StockItem stock = Fixture.NewStock();
        stock.Receive(100m, Fixture.Receipt(), Fixture.Now, Eur(4.50m));

        StockMovement movement = stock.Revalue(Eur(11.00m), Invoice(), Fixture.Now).Value;

        movement.Reference.Type.Should().Be(ReferenceType.SupplierInvoice);
        movement.Reference.Number.Should().Be("FT 2026/14872");
    }
}
