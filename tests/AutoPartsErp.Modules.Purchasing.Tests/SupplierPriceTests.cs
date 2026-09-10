using AutoPartsErp.Modules.Purchasing.Domain;
using AutoPartsErp.Modules.Purchasing.Domain.Agreements;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Purchasing.Tests;

/// <summary>
/// What a supplier charges for a part, from a given day.
/// <para>
/// The point of the day is that a price is never edited when it changes. The supplier announces a
/// rise from the first of October, the buyer records it, and both rows stay — the old one explains
/// the invoices already received and the new one prices everything after. Overwrite it and the
/// company can no longer say why a delivery in August cost what it did.
/// </para>
/// </summary>
public sealed class SupplierPriceTests
{
    private static Money Eur(decimal amount) => Money.Of(amount, Currency.Eur);

    [Fact]
    public void A_price_applies_from_its_day_onwards_and_not_before()
    {
        SupplierPrice price = Agree(4.50m, new DateOnly(2026, 10, 1));

        price.AppliesOn(new DateOnly(2026, 9, 30)).Should().BeFalse();
        price.AppliesOn(new DateOnly(2026, 10, 1)).Should().BeTrue();
        price.AppliesOn(new DateOnly(2027, 3, 14)).Should().BeTrue();
    }

    /// <summary>
    /// A rise is a second row, not an edit. Both survive, and a delivery is priced by whichever
    /// applied on the day it arrived.
    /// </summary>
    [Fact]
    public void A_rise_is_recorded_beside_the_old_price_rather_than_over_it()
    {
        SupplierPrice was = Agree(4.50m, new DateOnly(2026, 1, 1));
        SupplierPrice now = Agree(4.95m, new DateOnly(2026, 10, 1));

        var arrivedInAugust = new DateOnly(2026, 8, 12);

        was.AppliesOn(arrivedInAugust).Should().BeTrue();
        now.AppliesOn(arrivedInAugust).Should().BeFalse();

        was.UnitPrice.Amount.Should().Be(4.50m);
    }

    /// <summary>
    /// For a mistake, not for a change. This is the afternoon somebody notices the 4,50 should
    /// have read 45,00 and nothing has come in against it yet.
    /// </summary>
    [Fact]
    public void A_price_typed_wrong_is_corrected_in_place_with_a_reason()
    {
        SupplierPrice price = Agree(4.50m, new DateOnly(2026, 1, 1));

        price.Correct(Eur(45.00m), "Typed a decimal place out; confirmed against their fax.")
            .IsSuccess.Should().BeTrue();

        price.UnitPrice.Amount.Should().Be(45.00m);
        price.Note.Should().Contain("fax");
    }

    /// <summary>A price in another currency is another price, not a correction of this one.</summary>
    [Fact]
    public void A_correction_cannot_change_the_currency()
    {
        SupplierPrice price = Agree(4.50m, new DateOnly(2026, 1, 1));

        price.Correct(Money.Of(4.50m, Currency.Usd))
            .Error.Code.Should().Be("purchasing.agreement.price_currency_mismatch");
    }

    /// <summary>
    /// A line at nothing is almost always a price nobody filled in, and a shelf costed at zero
    /// reports every sale off it as pure margin — the exact figure a branch would act on, and the
    /// exact one that is wrong.
    /// </summary>
    [Fact]
    public void A_price_of_nothing_is_refused_rather_than_read_as_free()
    {
        SupplierPrice.Agree(Fixture.Supplier, Fixture.NewPart(), Eur(0m), new DateOnly(2026, 1, 1))
            .Error.Code.Should().Be("purchasing.agreement.price_not_positive");
    }

    /// <summary>
    /// Their invoice does not carry the company's SKU. Matching a line by description is how a
    /// delivery of brake pads gets booked against brake discs.
    /// </summary>
    [Fact]
    public void The_suppliers_own_reference_is_kept_so_their_lines_can_be_matched()
    {
        SupplierPrice price = SupplierPrice.Agree(
            Fixture.Supplier,
            Fixture.NewPart(),
            Eur(4.50m),
            new DateOnly(2026, 1, 1),
            supplierPartNumber: " 0986452041 ").Value;

        price.SupplierPartNumber.Should().Be("0986452041");

        price.SetSupplierPartNumber(null);
        price.SupplierPartNumber.Should().BeNull();
    }

    [Fact]
    public void A_price_needs_a_supplier_and_a_part()
    {
        Result<SupplierPrice> noSupplier = SupplierPrice.Agree(
            SupplierRef.Empty, Fixture.NewPart(), Eur(4.50m), new DateOnly(2026, 1, 1));

        noSupplier.Error.Code.Should().Be("purchasing.agreement.supplier_required");

        Result<SupplierPrice> noPart = SupplierPrice.Agree(
            Fixture.Supplier, PartRef.Empty, Eur(4.50m), new DateOnly(2026, 1, 1));

        noPart.Error.Code.Should().Be("purchasing.line.part_required");
    }

    private static SupplierPrice Agree(decimal amount, DateOnly from) =>
        SupplierPrice.Agree(Fixture.Supplier, Fixture.NewPart(), Eur(amount), from).Value;
}
