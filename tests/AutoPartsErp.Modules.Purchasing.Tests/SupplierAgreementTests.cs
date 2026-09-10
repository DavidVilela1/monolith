using AutoPartsErp.Modules.Purchasing.Domain.Agreements;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Purchasing.Tests;

/// <summary>
/// What was agreed with a supplier, so a delivery can price itself.
/// <para>
/// The whole of automatic entry rests on this being right. A warehouse counting a pallet is a
/// person's job and stays one; retyping prices settled in a negotiation nine months ago is not,
/// and every figure typed twice is a figure that will eventually disagree with itself.
/// </para>
/// </summary>
public sealed class SupplierAgreementTests
{
    private static Money Eur(decimal amount) => Money.Of(amount, Currency.Eur);

    private static RappelScale Scale(params (decimal From, decimal Percent)[] steps) =>
        RappelScale.Of(steps.Select(step => new RappelStep(Eur(step.From), step.Percent))).Value;

    /// <summary>
    /// The rate reached applies to the whole of the year, not only to what is above the step. That
    /// is how a rappel is written in this market, and it is why the last two thousand euros of a
    /// year are worth more than the goods they buy.
    /// </summary>
    [Fact]
    public void The_step_reached_pays_on_everything_not_only_on_the_excess()
    {
        RappelScale scale = Scale((25_000m, 2m), (50_000m, 3m));

        scale.RateFor(Eur(48_000m)).Should().Be(2m);
        scale.EarnedOn(Eur(48_000m)).Amount.Should().Be(960.00m);

        // Two thousand euros more, and the rebate nearly doubles: three per cent of the whole
        // 50.000, including the first 25.000 that was only ever worth two.
        scale.RateFor(Eur(50_000m)).Should().Be(3m);
        scale.EarnedOn(Eur(50_000m)).Amount.Should().Be(1_500.00m);
    }

    /// <summary>
    /// A supplier whose scale starts at 25.000 pays nothing to somebody who bought 20.000.
    /// Reporting the first step's rate anyway puts money on a screen that is never arriving.
    /// </summary>
    [Fact]
    public void Below_the_first_step_there_is_no_rebate()
    {
        RappelScale scale = Scale((25_000m, 2m), (50_000m, 3m));

        scale.RateFor(Eur(24_999.99m)).Should().Be(0m);
        scale.EarnedOn(Eur(24_999.99m)).Amount.Should().Be(0m);

        // Inclusive at the threshold, because that is what "a partir de" means on the contract.
        scale.RateFor(Eur(25_000m)).Should().Be(2m);
    }

    /// <summary>The number a buyer wants in December, and the reason to model a scale at all.</summary>
    [Fact]
    public void The_scale_says_how_far_it_is_to_the_next_step()
    {
        RappelScale scale = Scale((25_000m, 2m), (50_000m, 3m));

        scale.ToNextStep(Eur(48_150m))!.Amount.Should().Be(1_850.00m);

        // Below the first step it is the distance to earning anything at all.
        scale.ToNextStep(Eur(0m))!.Amount.Should().Be(25_000.00m);

        // And at the top there is nowhere left to climb. Null, not zero: zero would read as
        // "one more euro and the rate goes up", which is the opposite of what is true.
        scale.ToNextStep(Eur(51_000m)).Should().BeNull();
    }

    /// <summary>A flat agreed percentage is one step starting at nothing, not a second shape.</summary>
    [Fact]
    public void A_flat_rebate_is_a_scale_with_one_step()
    {
        RappelScale flat = RappelScale.Flat(4m, Currency.Eur).Value;

        flat.Steps.Should().ContainSingle();
        flat.RateFor(Eur(0m)).Should().Be(4m);
        flat.RateFor(Eur(120_000m)).Should().Be(4m);
    }

    /// <summary>
    /// Whoever types a supplier's contract into a screen is reading it off a fax, and should not
    /// have to sort it first.
    /// </summary>
    [Fact]
    public void Steps_are_sorted_rather_than_demanded_in_order()
    {
        RappelScale scale = Scale((50_000m, 3m), (10_000m, 1m), (25_000m, 2m));

        scale.Steps.Select(step => step.From.Amount).Should().ContainInOrder(10_000m, 25_000m, 50_000m);
        scale.BestRatePercent.Should().Be(3m);
    }

    /// <summary>
    /// A scale that pays less for buying more is percentages typed into the wrong rows. Letting it
    /// through stops a buyer ordering at exactly the wrong moment, for a reason nobody can see.
    /// </summary>
    [Fact]
    public void A_scale_that_pays_less_higher_up_is_refused()
    {
        Result<RappelScale> scale = RappelScale.Of(
        [
            new RappelStep(Eur(25_000m), 3m),
            new RappelStep(Eur(50_000m), 2m),
        ]);

        scale.Error.Code.Should().Be("purchasing.rappel.steps_not_increasing");
    }

    /// <summary>At that figure the rebate would be both rates, and nothing says which.</summary>
    [Fact]
    public void Two_steps_at_the_same_threshold_are_refused()
    {
        Result<RappelScale> scale = RappelScale.Of(
        [
            new RappelStep(Eur(25_000m), 2m),
            new RappelStep(Eur(25_000m), 3m),
        ]);

        scale.Error.Code.Should().Be("purchasing.rappel.duplicate_threshold");
    }

    [Fact]
    public void A_scale_has_to_have_a_step_and_a_real_rate()
    {
        RappelScale.Of([]).Error.Code.Should().Be("purchasing.rappel.no_steps");

        RappelScale.Of([new RappelStep(Eur(0m), 0m)])
            .Error.Code.Should().Be("purchasing.rappel.rate_out_of_range");

        RappelScale.Of([new RappelStep(Eur(0m), 100m)])
            .Error.Code.Should().Be("purchasing.rappel.rate_out_of_range");
    }

    /// <summary>
    /// The rebate comes off the supplier's own document, so the goods land on the shelf already at
    /// the price the company really paid, and the margin on the first sale is right.
    /// </summary>
    [Fact]
    public void A_rebate_settled_on_the_invoice_takes_money_off_the_document()
    {
        SupplierAgreement agreement = NewAgreement();

        agreement.RebateOnInvoice(Scale((25_000m, 2m), (50_000m, 3m))).IsSuccess.Should().BeTrue();

        agreement.RebatesOnInvoice.Should().BeTrue();
        agreement.RebatesByCreditNote.Should().BeFalse();
        agreement.InvoiceRateOn(Eur(30_000m)).Should().Be(2m);
    }

    /// <summary>
    /// The trap. A credit-note rebate takes nothing off the document — the company pays the full
    /// figure and is credited later — and applying its rate to the invoice as well is how the same
    /// discount gets taken twice.
    /// </summary>
    [Fact]
    public void A_rebate_settled_by_credit_note_takes_nothing_off_the_document()
    {
        SupplierAgreement agreement = NewAgreement();

        agreement.RebateByCreditNote(Scale((25_000m, 2m)), RappelPeriod.Annual)
            .IsSuccess.Should().BeTrue();

        agreement.RebatesByCreditNote.Should().BeTrue();
        agreement.InvoiceRateOn(Eur(80_000m)).Should().Be(0m);
    }

    /// <summary>A rebate that arrives later has to say later than what.</summary>
    [Fact]
    public void A_credit_note_rebate_has_to_name_its_period()
    {
        SupplierAgreement agreement = NewAgreement();

        agreement.RebateByCreditNote(Scale((25_000m, 2m)), RappelPeriod.None)
            .Error.Code.Should().Be("purchasing.agreement.rappel_period_required");
    }

    /// <summary>
    /// A threshold whose currency is assumed is a contract that changes meaning without anybody
    /// editing it.
    /// </summary>
    [Fact]
    public void A_scale_in_another_currency_is_refused()
    {
        SupplierAgreement agreement = NewAgreement();

        RappelScale dollars = RappelScale
            .Of([new RappelStep(Money.Of(25_000m, Currency.Usd), 2m)]).Value;

        agreement.RebateOnInvoice(dollars)
            .Error.Code.Should().Be("purchasing.agreement.rappel_currency_mismatch");
    }

    /// <summary>
    /// A supplier who withdraws a rebate in June has not thereby un-earned what came off in May.
    /// The agreement forgets; the documents already raised do not, because the rate is written on
    /// them.
    /// </summary>
    [Fact]
    public void Dropping_a_rebate_leaves_nothing_behind_to_apply()
    {
        SupplierAgreement agreement = NewAgreement();
        agreement.RebateOnInvoice(Scale((0m, 4m)));

        agreement.DropRebate();

        agreement.RappelBasis.Should().Be(RappelBasis.None);
        agreement.RappelScale.Should().BeNull();
        agreement.InvoiceRateOn(Eur(80_000m)).Should().Be(0m);
    }

    [Fact]
    public void An_agreement_applies_over_its_own_life_and_not_before_or_after()
    {
        SupplierAgreement agreement = NewAgreement();

        agreement.IsEffectiveOn(new DateOnly(2025, 12, 31)).Should().BeFalse();
        agreement.IsEffectiveOn(new DateOnly(2026, 1, 1)).Should().BeTrue();

        agreement.End(new DateOnly(2026, 6, 30)).IsSuccess.Should().BeTrue();

        agreement.IsEffectiveOn(new DateOnly(2026, 6, 30)).Should().BeTrue();
        agreement.IsEffectiveOn(new DateOnly(2026, 7, 1)).Should().BeFalse();

        agreement.End(new DateOnly(2025, 1, 1))
            .Error.Code.Should().Be("purchasing.agreement.period_inverted");
    }

    private static SupplierAgreement NewAgreement() =>
        SupplierAgreement.Open(
            Fixture.Supplier, "BOSCH", Currency.Eur, new DateOnly(2026, 1, 1)).Value;
}
