using AutoPartsErp.Modules.Purchasing.Domain.Agreements;
using AutoPartsErp.Modules.Purchasing.Domain.Agreements.Events;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Purchasing.Tests;

/// <summary>
/// What the rebate is worth so far, and how much of it the company has actually had.
/// <para>
/// One object for what looked like two problems. A rebate taken on the invoice is taken at the
/// rate the period had reached that day, so crossing a step in October leaves March's documents
/// two per cent short. A rebate taken by credit note is not taken at all until the end, so the
/// whole of it is outstanding and the stock is carried too high meanwhile. The same subtraction
/// answers both: what the scale earned, less what has already come off.
/// </para>
/// </summary>
public sealed class RappelAccrualTests
{
    private static readonly DateOnly Start = new(2026, 1, 1);
    private static readonly DateOnly End = new(2026, 12, 31);

    private static Money Eur(decimal amount) => Money.Of(amount, Currency.Eur);

    private static RappelScale Scale() =>
        RappelScale.Of(
        [
            new RappelStep(Eur(25_000m), 2m),
            new RappelStep(Eur(50_000m), 3m),
        ]).Value;

    /// <summary>
    /// The gap that was invisible before this existed. Three documents of twenty thousand: the
    /// first two go out with no rebate because the year has not reached the step, the third takes
    /// two per cent — and by then the year has earned two per cent on <em>everything</em>, of
    /// which 1.388,00 has never come off anything.
    /// </summary>
    [Fact]
    public void Crossing_a_step_leaves_the_earlier_documents_short_and_says_by_how_much()
    {
        RappelAccrual accrual = Open(RappelBasis.OnInvoice);
        RappelScale scale = Scale();

        // Each document is drafted at the rate the period had reached before it — which is how
        // the drafting handler stamps it, and the whole source of the shortfall.
        Settle(accrual, scale, 20_000m);
        accrual.Outstanding.Amount.Should().Be(0m);

        Settle(accrual, scale, 20_000m);
        accrual.Earned.Amount.Should().Be(800.00m);
        accrual.Outstanding.Amount.Should().Be(800.00m);

        Settle(accrual, scale, 20_000m);
        accrual.TakenOnInvoices.Amount.Should().Be(400.00m);
        accrual.Earned.Amount.Should().Be(1_788.00m);
        accrual.Outstanding.Amount.Should().Be(1_388.00m);
    }

    /// <summary>
    /// The whole accrual is outstanding all year, which is the figure the shelf is carrying too
    /// high until the note arrives. Nothing ever comes off a document.
    /// </summary>
    [Fact]
    public void A_credit_note_rebate_is_outstanding_from_the_first_euro_it_earns()
    {
        RappelAccrual accrual = Open(RappelBasis.PeriodCreditNote);
        RappelScale scale = Scale();

        accrual.Record(Eur(30_000m), Eur(0m), scale).IsSuccess.Should().BeTrue();

        accrual.TakenOnInvoices.Amount.Should().Be(0m);
        accrual.Earned.Amount.Should().Be(600.00m);
        accrual.Outstanding.Amount.Should().Be(600.00m);
    }

    /// <summary>
    /// The scale is not marginal, so the earned figure is recomputed on the whole period rather
    /// than added to. A running sum of per-document rebates would keep the earlier ones at the
    /// rate they were bought at, which is the error this object exists to find.
    /// </summary>
    [Fact]
    public void Reaching_a_step_re_rates_everything_bought_so_far()
    {
        RappelAccrual accrual = Open(RappelBasis.PeriodCreditNote);
        RappelScale scale = Scale();

        accrual.Record(Eur(49_000m), Eur(0m), scale);
        accrual.Earned.Amount.Should().Be(980.00m);

        // One thousand euros more, and the year gains 530: three per cent of the whole 50.000
        // instead of two. The goods that crossed the step are worth a fraction of what crossing
        // it was worth.
        accrual.Record(Eur(1_000m), Eur(0m), scale);
        accrual.Earned.Amount.Should().Be(1_500.00m);
    }

    /// <summary>
    /// A buyer wants to be told the year has moved to three per cent. Being told another eleven
    /// euros were bought is noise, and noise is what makes people stop reading.
    /// </summary>
    [Fact]
    public void Only_actually_crossing_a_step_is_announced()
    {
        RappelAccrual accrual = Open(RappelBasis.PeriodCreditNote);
        RappelScale scale = Scale();

        accrual.Record(Eur(10_000m), Eur(0m), scale);
        accrual.DomainEvents.Should().BeEmpty();

        accrual.Record(Eur(20_000m), Eur(0m), scale);

        RappelStepReachedDomainEvent reached =
            accrual.DomainEvents.OfType<RappelStepReachedDomainEvent>().Should().ContainSingle().Subject;

        reached.RatePercent.Should().Be(2m);
        reached.PurchasedAmount.Should().Be(30_000m);
        reached.GainedAmount.Should().Be(600.00m);

        accrual.ClearDomainEvents();

        // Still at two per cent. Nothing to say.
        accrual.Record(Eur(5_000m), Eur(0m), scale);
        accrual.DomainEvents.Should().BeEmpty();
    }

    /// <summary>
    /// Without this, a cancelled order leaves the year permanently claiming a rate it never
    /// reached — and the supplier finds that before the company does.
    /// </summary>
    [Fact]
    public void Crediting_a_document_can_take_the_period_back_down_through_a_step()
    {
        RappelAccrual accrual = Open(RappelBasis.PeriodCreditNote);
        RappelScale scale = Scale();

        accrual.Record(Eur(26_000m), Eur(0m), scale);
        accrual.Earned.Amount.Should().Be(520.00m);

        accrual.Reverse(Eur(2_000m), Eur(0m), scale).IsSuccess.Should().BeTrue();

        accrual.Purchased.Amount.Should().Be(24_000m);
        accrual.Earned.Amount.Should().Be(0m);
        accrual.Outstanding.Amount.Should().Be(0m);
    }

    /// <summary>
    /// Real, and worth surfacing rather than clamping away silently. The outstanding figure stays
    /// at nothing — the company is not owed money — but the flag says the year owes some back.
    /// </summary>
    [Fact]
    public void Taking_more_than_the_year_earned_is_reported_rather_than_hidden()
    {
        RappelAccrual accrual = Open(RappelBasis.OnInvoice);
        RappelScale scale = Scale();

        accrual.Record(Eur(49_000m), Eur(1_000m), scale);

        accrual.Earned.Amount.Should().Be(980.00m);
        accrual.IsOverclaimed.Should().BeTrue();
        accrual.Outstanding.Amount.Should().Be(0m);
    }

    /// <summary>
    /// A credit note does not have to match what the system worked out. Refusing one that did not
    /// would be the system arguing with a document that already exists.
    /// </summary>
    [Fact]
    public void A_credit_note_comes_off_what_is_outstanding()
    {
        RappelAccrual accrual = Open(RappelBasis.PeriodCreditNote);
        accrual.Record(Eur(30_000m), Eur(0m), Scale());
        accrual.ClearDomainEvents();

        accrual.RecordCreditNote(Eur(550.00m)).IsSuccess.Should().BeTrue();

        accrual.Credited.Amount.Should().Be(550.00m);
        accrual.Outstanding.Amount.Should().Be(50.00m);

        accrual.DomainEvents.OfType<RappelCreditedDomainEvent>()
            .Single().OutstandingAmount.Should().Be(50.00m);

        accrual.RecordCreditNote(Eur(0m))
            .Error.Code.Should().Be("purchasing.rappel_accrual.credit_not_positive");
    }

    /// <summary>
    /// Closing early stops counting purchases that still belong to the period, and the year
    /// settles at a step it had not finished climbing.
    /// </summary>
    [Fact]
    public void A_period_cannot_be_closed_before_it_is_over()
    {
        RappelAccrual accrual = Open(RappelBasis.PeriodCreditNote);

        accrual.Close(End).Error.Code.Should().Be("purchasing.rappel_accrual.period_not_over");

        accrual.Close(End.AddDays(1)).IsSuccess.Should().BeTrue();
        accrual.IsClosed.Should().BeTrue();

        accrual.Record(Eur(1_000m), Eur(0m), Scale())
            .Error.Code.Should().Be("purchasing.rappel_accrual.already_closed");
    }

    /// <summary>What the period earned leaves with it, so somebody can go and ask for it.</summary>
    [Fact]
    public void Closing_a_period_says_what_is_still_owed()
    {
        RappelAccrual accrual = Open(RappelBasis.PeriodCreditNote);
        accrual.Record(Eur(60_000m), Eur(0m), Scale());
        accrual.ClearDomainEvents();

        accrual.Close(End.AddDays(1)).IsSuccess.Should().BeTrue();

        RappelPeriodClosedDomainEvent closed =
            accrual.DomainEvents.OfType<RappelPeriodClosedDomainEvent>().Should().ContainSingle().Subject;

        closed.PurchasedAmount.Should().Be(60_000m);
        closed.EarnedAmount.Should().Be(1_800.00m);
        closed.OutstandingAmount.Should().Be(1_800.00m);
    }

    /// <summary>
    /// The basis is snapshotted when the period opens. A renegotiation in August must not restate
    /// the months before it.
    /// </summary>
    [Fact]
    public void A_period_keeps_the_basis_it_opened_under()
    {
        Open(RappelBasis.OnInvoice).Basis.Should().Be(RappelBasis.OnInvoice);

        RappelAccrual.Open(
            Fixture.Supplier, "BOSCH", RappelBasis.None, Start, End, Currency.Eur)
            .Error.Code.Should().Be("purchasing.rappel_accrual.no_rebate");
    }

    /// <summary>Calendar periods, because a supplier's own accounts run on the calendar.</summary>
    [Fact]
    public void The_agreement_says_what_stretch_of_days_a_rebate_is_measured_over()
    {
        SupplierAgreement agreement = SupplierAgreement
            .Open(Fixture.Supplier, "BOSCH", Currency.Eur, Start).Value;

        agreement.PeriodFor(new DateOnly(2026, 8, 12)).Should().BeNull();

        agreement.RebateByCreditNote(Scale(), RappelPeriod.Quarterly);

        (DateOnly From, DateOnly To) quarter = agreement.PeriodFor(new DateOnly(2026, 8, 12))!.Value;
        quarter.From.Should().Be(new DateOnly(2026, 7, 1));
        quarter.To.Should().Be(new DateOnly(2026, 9, 30));

        agreement.RebateByCreditNote(Scale(), RappelPeriod.Monthly);

        (DateOnly From, DateOnly To) month = agreement.PeriodFor(new DateOnly(2026, 2, 9))!.Value;
        month.From.Should().Be(new DateOnly(2026, 2, 1));
        month.To.Should().Be(new DateOnly(2026, 2, 28));

        agreement.RebateOnInvoice(Scale());

        (DateOnly From, DateOnly To) year = agreement.PeriodFor(new DateOnly(2026, 8, 12))!.Value;
        year.From.Should().Be(Start);
        year.To.Should().Be(End);
    }

    /// <summary>A period cannot buy less than nothing, and a reversal that big hides a real error.</summary>
    [Fact]
    public void A_reversal_larger_than_the_period_bought_is_refused()
    {
        RappelAccrual accrual = Open(RappelBasis.PeriodCreditNote);
        accrual.Record(Eur(1_000m), Eur(0m), Scale());

        Result reversed = accrual.Reverse(Eur(1_001m), Eur(0m), Scale());

        reversed.Error.Code.Should().Be("purchasing.rappel_accrual.reversal_too_large");
    }

    /// <summary>Settles one document the way the drafting handler stamps it: at the rate reached so far.</summary>
    private static void Settle(RappelAccrual accrual, RappelScale scale, decimal gross)
    {
        Money rebate = Eur(gross).Percentage(scale.RateFor(accrual.Purchased));

        accrual.Record(Eur(gross) - rebate, rebate, scale).IsSuccess.Should().BeTrue();
    }

    private static RappelAccrual Open(RappelBasis basis) =>
        RappelAccrual.Open(Fixture.Supplier, "BOSCH", basis, Start, End, Currency.Eur).Value;
}
