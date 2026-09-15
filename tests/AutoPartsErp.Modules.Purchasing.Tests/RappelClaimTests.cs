using AutoPartsErp.Modules.Purchasing.Domain;
using AutoPartsErp.Modules.Purchasing.Domain.Agreements;
using AutoPartsErp.Modules.Purchasing.Domain.Agreements.Events;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Purchasing.Tests;

/// <summary>
/// What a supplier owes for a rebate period, as something somebody chases.
/// <para>
/// This is where the two rebate gaps close. On an invoice, crossing a step in October leaves every
/// document settled since January short, and a supplier's issued invoice cannot be re-rated by
/// anybody — so the difference can only ever be asked for. By credit note, the whole year is
/// outstanding until the note arrives, and it was never going to arrive unasked. Both end up
/// here: a number, a state, and a supplier.
/// </para>
/// </summary>
public sealed class RappelClaimTests
{
    private static readonly RappelAccrualId Period = RappelAccrualId.New();
    private static readonly SupplierRef Supplier = new(Guid.NewGuid());
    private static readonly DateOnly From = new(2026, 1, 1);
    private static readonly DateOnly To = new(2026, 12, 31);
    private static readonly DateOnly InJanuary = new(2027, 1, 14);

    private static Money Eur(decimal amount) => Money.Of(amount, Currency.Eur);

    /// <summary>A raised claim knows what it is for and announces itself.</summary>
    [Fact]
    public void A_raised_claim_asks_for_what_the_period_was_owed()
    {
        RappelClaim claim = Raised(1388.00m);

        claim.Number.Should().Be("RC-2026-00007");
        claim.SupplierCode.Should().Be("BP");
        claim.AccrualId.Should().Be(Period);
        claim.Status.Should().Be(RappelClaimStatus.Raised);
        claim.IsOpen.Should().BeTrue();
        claim.Claimed.Amount.Should().Be(1388.00m);
        claim.Credited.Amount.Should().Be(0m);
        claim.Outstanding.Amount.Should().Be(1388.00m);
        claim.SentOn.Should().BeNull();

        RappelClaimRaisedDomainEvent raised = claim.DomainEvents
            .OfType<RappelClaimRaisedDomainEvent>()
            .Single();

        raised.ClaimedAmount.Should().Be(1388.00m);
        raised.PeriodFrom.Should().Be(From);
        raised.PeriodTo.Should().Be(To);
    }

    /// <summary>
    /// A claim for nothing is a line that wastes somebody's attention every time they look at the
    /// screen, and a supplier meeting is short.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-12.50)]
    public void A_claim_for_nothing_is_refused(decimal amount)
    {
        Result<RappelClaim> claim = RappelClaim.Raise(
            "RC-2026-00007", Period, Supplier, "BP", From, To, Eur(amount));

        claim.IsFailure.Should().BeTrue();
        claim.Error.Code.Should().Be("purchasing.rappel_claim.nothing_to_claim");
    }

    /// <summary>A claim comes from a period; what it is owed for is the whole of it.</summary>
    [Fact]
    public void A_claim_without_a_period_is_refused()
    {
        Result<RappelClaim> claim = RappelClaim.Raise(
            "RC-2026-00007", RappelAccrualId.Empty, Supplier, "BP", From, To, Eur(100m));

        claim.IsFailure.Should().BeTrue();
        claim.Error.Code.Should().Be("purchasing.rappel_claim.period_required");
    }

    /// <summary>Two people have to be able to talk about the same claim.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_claim_without_a_number_is_refused(string? number)
    {
        Result<RappelClaim> claim = RappelClaim.Raise(
            number, Period, Supplier, "BP", From, To, Eur(100m));

        claim.IsFailure.Should().BeTrue();
        claim.Error.Code.Should().Be("purchasing.rappel_claim.number_required");
    }

    /// <summary>Sending records the day and what was said.</summary>
    [Fact]
    public void Sending_records_when_and_what_was_said()
    {
        RappelClaim claim = Raised(1388.00m);
        claim.ClearDomainEvents();

        Result sent = claim.Send(InJanuary, "  Emailed Ana, with the purchase summary.  ");

        sent.IsSuccess.Should().BeTrue();
        claim.Status.Should().Be(RappelClaimStatus.Sent);
        claim.IsOpen.Should().BeTrue();
        claim.SentOn.Should().Be(InJanuary);
        claim.Note.Should().Be("Emailed Ana, with the purchase summary.");

        claim.DomainEvents.OfType<RappelClaimSentDomainEvent>().Single()
            .SentOn.Should().Be(InJanuary);
    }

    /// <summary>Asking twice is not asking again; it is losing track of whether anyone asked.</summary>
    [Fact]
    public void Sending_twice_is_refused()
    {
        RappelClaim claim = Raised(1388.00m);
        claim.Send(InJanuary, null);

        Result again = claim.Send(InJanuary.AddDays(7), null);

        again.IsFailure.Should().BeTrue();
        again.Error.Code.Should().Be("purchasing.rappel_claim.already_sent");
    }

    /// <summary>A credit note that covers the whole thing finishes it.</summary>
    [Fact]
    public void A_credit_note_for_the_whole_claim_closes_it()
    {
        RappelClaim claim = Raised(1388.00m);
        claim.Send(InJanuary, null);

        Result credited = claim.Credit(Eur(1388.00m), "NC 442/2027");

        credited.IsSuccess.Should().BeTrue();
        claim.Status.Should().Be(RappelClaimStatus.Credited);
        claim.IsOpen.Should().BeFalse();
        claim.Credited.Amount.Should().Be(1388.00m);
        claim.Outstanding.Amount.Should().Be(0m);

        claim.DomainEvents.OfType<RappelClaimCreditedDomainEvent>().Single()
            .CreditNoteNumber.Should().Be("NC 442/2027");
    }

    /// <summary>
    /// A supplier who agrees with four fifths sends four fifths. Forcing all-or-nothing would mean
    /// a screen reading as though the company got everything it asked for.
    /// </summary>
    [Fact]
    public void Part_of_a_claim_can_be_credited_and_the_rest_stays_outstanding()
    {
        RappelClaim claim = Raised(1388.00m);

        claim.Credit(Eur(1100.00m), "NC 442/2027").IsSuccess.Should().BeTrue();

        claim.Status.Should().Be(RappelClaimStatus.Raised);
        claim.IsOpen.Should().BeTrue();
        claim.Credited.Amount.Should().Be(1100.00m);
        claim.Outstanding.Amount.Should().Be(288.00m);

        claim.Credit(Eur(288.00m), "NC 501/2027").IsSuccess.Should().BeTrue();

        claim.Status.Should().Be(RappelClaimStatus.Credited);
        claim.Outstanding.Amount.Should().Be(0m);
    }

    /// <summary>
    /// More than was asked for is refused rather than absorbed. Quietly taking it is how a company
    /// finds out eighteen months later, when the supplier asks for it back.
    /// </summary>
    [Fact]
    public void A_credit_note_larger_than_the_claim_is_refused()
    {
        RappelClaim claim = Raised(1388.00m);
        claim.Credit(Eur(1100.00m), null);

        Result tooMuch = claim.Credit(Eur(400.00m), "NC 501/2027");

        tooMuch.IsFailure.Should().BeTrue();
        tooMuch.Error.Code.Should().Be("purchasing.rappel_claim.more_than_claimed");
        tooMuch.Error.Description.Should().Contain("288.00");

        // And nothing moved.
        claim.Credited.Amount.Should().Be(1100.00m);
    }

    /// <summary>A rebate is in one currency, and converting is a decision with a rate behind it.</summary>
    [Fact]
    public void A_credit_note_in_another_currency_is_refused()
    {
        RappelClaim claim = Raised(1388.00m);

        Result wrong = claim.Credit(Money.Of(100m, Currency.Usd), null);

        wrong.IsFailure.Should().BeTrue();
        wrong.Error.Code.Should().Be("purchasing.agreement.rappel_currency_mismatch");
    }

    /// <summary>Giving up is a real outcome, and the reason is the point of recording it.</summary>
    [Fact]
    public void Writing_off_records_what_was_given_up_and_why()
    {
        RappelClaim claim = Raised(1388.00m);
        claim.Credit(Eur(1100.00m), null);
        claim.ClearDomainEvents();

        Result written = claim.WriteOff("They dispute the September step. Not worth the argument.");

        written.IsSuccess.Should().BeTrue();
        claim.Status.Should().Be(RappelClaimStatus.WrittenOff);
        claim.IsOpen.Should().BeFalse();
        claim.Note.Should().Contain("dispute");

        RappelClaimWrittenOffDomainEvent off = claim.DomainEvents
            .OfType<RappelClaimWrittenOffDomainEvent>()
            .Single();

        // What was given up is what was left, not what was originally asked for.
        off.Amount.Should().Be(288.00m);
    }

    /// <summary>Writing off money the company was owed needs a sentence behind it.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Writing_off_without_a_reason_is_refused(string? reason)
    {
        RappelClaim claim = Raised(1388.00m);

        Result written = claim.WriteOff(reason);

        written.IsFailure.Should().BeTrue();
        written.Error.Code.Should().Be("purchasing.rappel_claim.write_off_reason_required");
        claim.IsOpen.Should().BeTrue();
    }

    /// <summary>A finished claim is finished, both ways round.</summary>
    [Fact]
    public void A_finished_claim_takes_nothing_more()
    {
        RappelClaim credited = Raised(100m);
        credited.Credit(Eur(100m), null);

        credited.Credit(Eur(10m), null).Error.Code
            .Should().Be("purchasing.rappel_claim.already_credited");
        credited.WriteOff("Anything.").Error.Code
            .Should().Be("purchasing.rappel_claim.already_credited");
        credited.Send(InJanuary, null).Error.Code
            .Should().Be("purchasing.rappel_claim.closed");

        RappelClaim written = Raised(100m);
        written.WriteOff("Gone.");

        written.Credit(Eur(10m), null).Error.Code.Should().Be("purchasing.rappel_claim.closed");
        written.WriteOff("Again.").Error.Code.Should().Be("purchasing.rappel_claim.closed");
    }

    /// <summary>
    /// A supplier who credited more than was asked for is a different conversation, and one
    /// Outstanding would hide by reporting a debt the other way.
    /// </summary>
    [Fact]
    public void Outstanding_never_goes_below_zero()
    {
        RappelClaim claim = Raised(100m);
        claim.Credit(Eur(100m), null);

        claim.Outstanding.Amount.Should().Be(0m);
        claim.Outstanding.IsNegative.Should().BeFalse();
    }

    private static RappelClaim Raised(decimal amount) =>
        RappelClaim.Raise(
            "rc-2026-00007", Period, Supplier, " bp ", From, To, Eur(amount)).Value;
}
