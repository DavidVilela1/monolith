using AutoPartsErp.Modules.Catalog.Domain;
using AutoPartsErp.Modules.Catalog.Domain.Parts;
using AutoPartsErp.Modules.Catalog.Domain.Parts.Events;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Catalog.Tests;

/// <summary>
/// The rules that protect the part aggregate. These run without a database, a web server or a
/// container, which is the whole point of keeping the rules in the domain.
/// </summary>
public sealed class PartTests
{
    [Fact]
    public void A_new_part_starts_as_a_draft_and_is_not_sellable()
    {
        Part part = PartTestData.NewPart();

        part.Status.Should().Be(PartStatus.Draft);
        part.IsSellable.Should().BeFalse();
        part.IsPurchasable.Should().BeFalse();
    }

    [Fact]
    public void Creating_a_part_raises_a_domain_event()
    {
        Part part = PartTestData.NewPart();

        part.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<PartCreatedDomainEvent>();
    }

    [Fact]
    public void Activation_makes_a_part_purchasable_and_sellable()
    {
        Part part = PartTestData.NewPart();

        part.Activate().IsSuccess.Should().BeTrue();

        part.Status.Should().Be(PartStatus.Active);
        part.IsPurchasable.Should().BeTrue();
        part.IsSellable.Should().BeTrue();
        part.DomainEvents.Should().ContainItemsAssignableTo<PartActivatedDomainEvent>();
    }

    [Fact]
    public void Flagging_a_core_return_records_the_deposit_and_survives_activation()
    {
        Part part = PartTestData.NewPart();

        part.RequireCoreReturn(Money.Of(85m, Currency.Eur)).IsSuccess.Should().BeTrue();
        part.Activate().IsSuccess.Should().BeTrue();

        part.RequiresCoreReturn.Should().BeTrue();
        part.CoreCharge.Should().Be(Money.Of(85m, Currency.Eur));
    }

    [Fact]
    public void Clearing_a_core_return_removes_the_deposit_with_it()
    {
        Part part = PartTestData.NewPart();
        part.RequireCoreReturn(Money.Of(85m, Currency.Eur));

        part.ClearCoreReturn().IsSuccess.Should().BeTrue();

        part.RequiresCoreReturn.Should().BeFalse();
        part.CoreCharge.Should().BeNull();
    }

    [Fact]
    public void A_core_charge_must_be_positive()
    {
        Part part = PartTestData.NewPart();

        Result applied = part.RequireCoreReturn(Money.Of(0m, Currency.Eur));

        applied.Error.Should().Be(CatalogErrors.Part.CoreChargeMustBePositive);
    }

    [Fact]
    public void The_stocking_unit_is_frozen_once_a_part_goes_live()
    {
        Part part = PartTestData.NewPart();
        part.Activate();

        Result changed = part.ChangeStockUnit(UnitOfMeasure.Litre);

        changed.IsFailure.Should().BeTrue();
        changed.Error.Should().Be(CatalogErrors.Part.StockUnitLocked);
        part.StockUnit.Should().Be(UnitOfMeasure.Each);
    }

    [Fact]
    public void The_stocking_unit_can_still_be_corrected_while_a_part_is_a_draft()
    {
        Part part = PartTestData.NewPart();

        part.ChangeStockUnit(UnitOfMeasure.Litre).IsSuccess.Should().BeTrue();

        part.StockUnit.Should().Be(UnitOfMeasure.Litre);
        part.DomainEvents.Should().ContainItemsAssignableTo<PartStockUnitChangedDomainEvent>();
    }

    [Fact]
    public void A_discontinued_part_cannot_be_brought_back_to_life()
    {
        Part part = PartTestData.NewPart();
        part.Activate();
        part.Discontinue();

        Result reactivation = part.Activate();

        reactivation.IsFailure.Should().BeTrue();
        reactivation.Error.Should().Be(CatalogErrors.Part.CannotReactivate);
    }

    [Fact]
    public void A_discontinued_part_is_still_sold_down_but_no_longer_bought()
    {
        Part part = PartTestData.NewPart();
        part.Activate();
        part.Discontinue();

        part.IsSellable.Should().BeTrue();
        part.IsPurchasable.Should().BeFalse();
    }

    [Fact]
    public void A_draft_part_cannot_be_discontinued()
    {
        Part part = PartTestData.NewPart();

        part.Discontinue().Error.Should().Be(CatalogErrors.Part.CannotDiscontinueDraft);
    }

    [Fact]
    public void A_part_cannot_be_its_own_replacement()
    {
        Part part = PartTestData.NewPart();
        part.Activate();

        Result discontinued = part.Discontinue(part.Id);

        discontinued.Error.Should().Be(CatalogErrors.Part.CannotSupersedeItself);
    }

    [Fact]
    public void An_obsolete_part_is_frozen()
    {
        Part part = PartTestData.NewPart();
        part.Activate();
        part.Discontinue();
        part.MakeObsolete();

        part.Describe("New name", null).Error.Should().Be(CatalogErrors.Part.ObsoleteIsReadOnly);
        part.Reclassify(CategoryId.New()).Error.Should().Be(CatalogErrors.Part.ObsoleteIsReadOnly);
    }
}

/// <summary>Cross-references are how a customer's number reaches the right part.</summary>
public sealed class PartCrossReferenceTests
{
    [Fact]
    public void The_same_reference_cannot_be_recorded_twice()
    {
        Part part = PartTestData.NewPart();
        CrossReference reference = Reference("5Q0 698 151 A");

        part.AddCrossReference(reference).IsSuccess.Should().BeTrue();
        part.AddCrossReference(Reference("5Q0 698 151 A")).Error
            .Should().Be(CatalogErrors.CrossReference.Duplicate);

        part.CrossReferences.Should().ContainSingle();
    }

    [Fact]
    public void Spacing_differences_do_not_create_a_second_reference()
    {
        Part part = PartTestData.NewPart();

        part.AddCrossReference(Reference("5Q0 698 151 A"));
        Result second = part.AddCrossReference(Reference("5Q0698151A"));

        second.Error.Should().Be(CatalogErrors.CrossReference.Duplicate);
    }

    [Fact]
    public void A_part_cannot_cross_reference_its_own_number()
    {
        Part part = PartTestData.NewPart();

        Result added = part.AddCrossReference(Reference(PartTestData.ManufacturerNumber));

        added.Error.Should().Be(CatalogErrors.CrossReference.SameAsOwnNumber);
    }

    [Fact]
    public void Any_recorded_number_finds_the_part_however_it_is_typed()
    {
        Part part = PartTestData.NewPart();
        part.AddCrossReference(Reference("5Q0 698 151 A"));

        part.MatchesNumber("5q0-698-151-a").Should().BeTrue();
        part.MatchesNumber("5Q0698151A").Should().BeTrue();
        part.MatchesNumber(PartTestData.ManufacturerNumber).Should().BeTrue();
        part.MatchesNumber("something else").Should().BeFalse();
    }

    private static CrossReference Reference(string number) =>
        CrossReference.Create(CrossReferenceKind.Oem, number, "VW").Value;
}

/// <summary>Fitment is the relationship the whole catalogue exists to answer questions about.</summary>
public sealed class FitmentTests
{
    [Fact]
    public void A_year_range_cannot_run_backwards()
    {
        Result<Fitment> fitment = Fitment.Create("VOLKSWAGEN", "GOLF VII", 2020, 2012);

        fitment.Error.Should().Be(CatalogErrors.Fitment.YearRangeInverted);
    }

    [Fact]
    public void Make_and_model_are_normalized_so_lookups_match()
    {
        Fitment fitment = Fitment.Create(" volkswagen ", "golf vii", 2012, 2020).Value;

        fitment.Make.Should().Be("VOLKSWAGEN");
        fitment.Model.Should().Be("GOLF VII");
    }

    [Fact]
    public void A_fitment_covers_every_year_in_its_range_inclusive()
    {
        Fitment fitment = Fitment.Create("VOLKSWAGEN", "GOLF VII", 2012, 2020).Value;

        fitment.CoversYear(2012).Should().BeTrue();
        fitment.CoversYear(2016).Should().BeTrue();
        fitment.CoversYear(2020).Should().BeTrue();
        fitment.CoversYear(2011).Should().BeFalse();
        fitment.CoversYear(2021).Should().BeFalse();
    }

    [Fact]
    public void The_same_vehicle_application_cannot_be_recorded_twice()
    {
        Part part = PartTestData.NewPart();
        Fitment golf = Fitment.Create("VOLKSWAGEN", "GOLF VII", 2012, 2020, "CJZA", "FRONT").Value;

        part.AddFitment(golf).IsSuccess.Should().BeTrue();
        part.AddFitment(Fitment.Create("volkswagen", "golf vii", 2012, 2020, "cjza", "front").Value)
            .Error.Should().Be(CatalogErrors.Fitment.Duplicate);
    }

    [Fact]
    public void The_same_vehicle_at_a_different_position_is_a_separate_application()
    {
        Part part = PartTestData.NewPart();

        part.AddFitment(Fitment.Create("VOLKSWAGEN", "GOLF VII", 2012, 2020, null, "FRONT").Value);
        Result rear = part.AddFitment(
            Fitment.Create("VOLKSWAGEN", "GOLF VII", 2012, 2020, null, "REAR").Value);

        rear.IsSuccess.Should().BeTrue();
        part.Fitments.Should().HaveCount(2);
    }

    [Fact]
    public void A_part_reports_whether_it_fits_a_vehicle()
    {
        Part part = PartTestData.NewPart();
        part.AddFitment(Fitment.Create("VOLKSWAGEN", "GOLF VII", 2012, 2020).Value);

        part.FitsVehicle("volkswagen", "golf vii", 2015).Should().BeTrue();
        part.FitsVehicle("VOLKSWAGEN", "GOLF VII", 2022).Should().BeFalse();
        part.FitsVehicle("BMW", "3 SERIES F30", 2015).Should().BeFalse();
    }
}

/// <summary>SKUs and part numbers are normalized so the same part cannot be created twice.</summary>
public sealed class PartIdentifierTests
{
    [Theory]
    [InlineData("bp-1188", "BP-1188")]
    [InlineData("  bp-1188  ", "BP-1188")]
    [InlineData("OF/4501.A", "OF/4501.A")]
    public void A_sku_is_uppercased_and_trimmed(string input, string expected)
    {
        Sku.Create(input).Value.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-LEADING")]
    [InlineData("HAS SPACE")]
    [InlineData("HAS#HASH")]
    public void An_invalid_sku_is_rejected(string input)
    {
        Sku.Create(input).IsFailure.Should().BeTrue();
    }

    [Theory]
    [InlineData("0 986 424 815", "0986424815")]
    [InlineData("0986-424-815", "0986424815")]
    [InlineData("f 026 407 006", "F026407006")]
    public void A_part_number_keeps_its_printed_form_and_a_searchable_form(string input, string normalized)
    {
        PartNumber number = PartNumber.Create(input).Value;

        number.Display.Should().Be(input.Trim());
        number.Normalized.Should().Be(normalized);
    }

    [Fact]
    public void Part_numbers_that_differ_only_in_punctuation_are_the_same_number()
    {
        PartNumber printed = PartNumber.Create("0 986 424 815").Value;
        PartNumber typed = PartNumber.Create("0986424815").Value;

        printed.Should().Be(typed);
    }
}

/// <summary>Shared fixtures for the part tests.</summary>
internal static class PartTestData
{
    public const string ManufacturerNumber = "16 232 70";

    public static Part NewPart() =>
        Part.Create(
            Sku.Create("BP-1188").Value,
            PartNumber.Create(ManufacturerNumber).Value,
            BrandId.New(),
            CategoryId.New(),
            "Brake pad set, front axle",
            UnitOfMeasure.Each).Value;
}

/// <summary>
/// The sellable-status list is what the counter-facing queries filter on, so it has to stay
/// in step with the IsSellable rule. If someone adds a status and updates only one of them,
/// this test fails rather than a draft part quietly reaching a customer.
/// </summary>
public sealed class SellableStatusTests
{
    [Theory]
    [InlineData(PartStatus.Draft, false)]
    [InlineData(PartStatus.Active, true)]
    [InlineData(PartStatus.Discontinued, true)]
    [InlineData(PartStatus.Obsolete, false)]
    public void The_sellable_list_agrees_with_the_IsSellable_rule(PartStatus status, bool expected)
    {
        Part.SellableStatuses.Contains(status).Should().Be(expected);
    }

    [Fact]
    public void A_draft_part_is_not_sellable()
    {
        Part part = PartTestData.NewPart();

        part.IsSellable.Should().BeFalse();
        Part.SellableStatuses.Should().NotContain(part.Status);
    }

    [Fact]
    public void An_active_part_is_sellable_by_both_measures()
    {
        Part part = PartTestData.NewPart();
        part.Activate();

        part.IsSellable.Should().BeTrue();
        Part.SellableStatuses.Should().Contain(part.Status);
    }
}

/// <summary>
/// The purchasable-status list is the other half of the same guard, and it is stricter: a
/// discontinued part may still be sold down off the shelf, but ordering more of it is how dead
/// stock gets bought on purpose. Catalog now answers this question for Purchasing across a
/// module boundary, so the list and the rule disagreeing would not fail loudly here — it would
/// quietly let somebody restock a part the company decided to stop carrying.
/// </summary>
public sealed class PurchasableStatusTests
{
    [Theory]
    [InlineData(PartStatus.Draft, false)]
    [InlineData(PartStatus.Active, true)]
    [InlineData(PartStatus.Discontinued, false)]
    [InlineData(PartStatus.Obsolete, false)]
    public void The_purchasable_list_agrees_with_the_IsPurchasable_rule(PartStatus status, bool expected)
    {
        Part.PurchasableStatuses.Contains(status).Should().Be(expected);
    }

    [Fact]
    public void A_discontinued_part_can_still_be_sold_but_not_ordered()
    {
        Part part = PartTestData.NewPart();
        part.Activate();
        part.Discontinue();

        part.IsSellable.Should().BeTrue();
        part.IsPurchasable.Should().BeFalse();
    }
}
