using AutoPartsErp.Modules.Finance.Domain.Ledger;
using AutoPartsErp.Modules.Finance.Domain.Ledger.Events;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Finance.Tests;

/// <summary>
/// The accounting calendar: which months still take entries.
/// <para>
/// Everything else in this module is about getting a figure right. This is about it staying right
/// — a March that has been declared to the tax authority and shown to somebody should still say
/// the same thing in July, and the only way it can is if nothing new lands in it.
/// </para>
/// </summary>
public sealed class AccountingPeriodTests
{
    private static readonly DateTimeOffset Now = new(2026, 4, 3, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly InApril = new(2026, 4, 3);

    /// <summary>A month opens ready to take entries.</summary>
    [Fact]
    public void A_period_opens_open()
    {
        AccountingPeriod period = March();

        period.Year.Should().Be(2026);
        period.Month.Should().Be(3);
        period.Status.Should().Be(PeriodStatus.Open);
        period.IsOpen.Should().BeTrue();
        period.ClosedAtUtc.Should().BeNull();
        period.ReopenedReason.Should().BeNull();
    }

    /// <summary>The bounds are the calendar's, including the short months.</summary>
    [Theory]
    [InlineData(2026, 1, 31)]
    [InlineData(2026, 2, 28)]
    [InlineData(2024, 2, 29)]
    [InlineData(2026, 4, 30)]
    [InlineData(2026, 12, 31)]
    public void A_period_runs_from_the_first_to_the_last_day_of_its_month(
        int year, int month, int lastDay)
    {
        AccountingPeriod period = AccountingPeriod.Open(year, month).Value;

        period.From.Should().Be(new DateOnly(year, month, 1));
        period.To.Should().Be(new DateOnly(year, month, lastDay));
    }

    /// <summary>A year this system will not keep books for is refused.</summary>
    [Fact]
    public void A_year_outside_the_range_is_refused()
    {
        Result<AccountingPeriod> period = AccountingPeriod.Open(1999, 3);

        period.IsFailure.Should().BeTrue();
        period.Error.Code.Should().Be("finance.period.year_out_of_range");
    }

    /// <summary>There is no month thirteen.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void A_month_outside_one_to_twelve_is_refused(int month)
    {
        Result<AccountingPeriod> period = AccountingPeriod.Open(2026, month);

        period.IsFailure.Should().BeTrue();
        period.Error.Code.Should().Be("finance.period.month_out_of_range");
    }

    /// <summary>Closing a finished month whose predecessors are closed is the ordinary case.</summary>
    [Fact]
    public void A_finished_month_closes_and_says_so()
    {
        AccountingPeriod period = March();

        Result closed = period.Close(Now, InApril, previousIsClosed: true);

        closed.IsSuccess.Should().BeTrue();
        period.Status.Should().Be(PeriodStatus.Closed);
        period.IsOpen.Should().BeFalse();
        period.ClosedAtUtc.Should().Be(Now);

        AccountingPeriodClosedDomainEvent raised = period.DomainEvents
            .OfType<AccountingPeriodClosedDomainEvent>()
            .Single();

        raised.Year.Should().Be(2026);
        raised.Month.Should().Be(3);
        raised.From.Should().Be(new DateOnly(2026, 3, 1));
        raised.To.Should().Be(new DateOnly(2026, 3, 31));
    }

    /// <summary>
    /// A month still running cannot be closed — the entries belonging to its last days would have
    /// nowhere to go, and somebody would post them into the next month to get the work done.
    /// </summary>
    [Fact]
    public void A_month_that_is_still_running_cannot_be_closed()
    {
        AccountingPeriod period = March();

        Result closed = period.Close(
            Now, new DateOnly(2026, 3, 31), previousIsClosed: true);

        closed.IsFailure.Should().BeTrue();
        closed.Error.Code.Should().Be("finance.period.not_over");
        period.IsOpen.Should().BeTrue();
    }

    /// <summary>The last day of the month is still inside it.</summary>
    [Fact]
    public void The_last_day_of_the_month_is_too_early_to_close_it()
    {
        AccountingPeriod period = March();

        period.Close(Now, period.To, previousIsClosed: true).IsFailure.Should().BeTrue();
        period.Close(Now, period.To.AddDays(1), previousIsClosed: true)
            .IsSuccess.Should().BeTrue();
    }

    /// <summary>
    /// Closing out of order leaves a month somebody can post into after the ones after it have
    /// been reported, and the year-to-date figures on those reports would change afterwards.
    /// </summary>
    [Fact]
    public void A_month_whose_predecessor_is_open_cannot_be_closed()
    {
        AccountingPeriod period = March();

        Result closed = period.Close(Now, InApril, previousIsClosed: false);

        closed.IsFailure.Should().BeTrue();
        closed.Error.Code.Should().Be("finance.period.earlier_open");
        period.IsOpen.Should().BeTrue();
    }

    /// <summary>Closing twice is not a second close.</summary>
    [Fact]
    public void A_closed_month_cannot_be_closed_again()
    {
        AccountingPeriod period = Closed();

        Result again = period.Close(Now, InApril, previousIsClosed: true);

        again.IsFailure.Should().BeTrue();
        again.Error.Code.Should().Be("finance.period.already_closed");
    }

    /// <summary>Reopening takes it back to open, and records why.</summary>
    [Fact]
    public void A_closed_month_reopens_with_a_reason()
    {
        AccountingPeriod period = Closed();
        period.ClearDomainEvents();

        Result reopened = period.Reopen(
            "  Supplier invoice 4471 arrived dated 28 March.  ", laterPeriodIsClosed: false);

        reopened.IsSuccess.Should().BeTrue();
        period.Status.Should().Be(PeriodStatus.Open);
        period.ClosedAtUtc.Should().BeNull();
        period.ReopenedReason.Should().Be("Supplier invoice 4471 arrived dated 28 March.");

        AccountingPeriodReopenedDomainEvent raised = period.DomainEvents
            .OfType<AccountingPeriodReopenedDomainEvent>()
            .Single();

        raised.Year.Should().Be(2026);
        raised.Month.Should().Be(3);
        raised.Reason.Should().Be("Supplier invoice 4471 arrived dated 28 March.");
    }

    /// <summary>
    /// Reopening is somebody deciding a reported figure was wrong. Without a sentence, nobody
    /// reading the ledger next year can tell what happened.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Reopening_without_a_reason_is_refused(string? reason)
    {
        AccountingPeriod period = Closed();

        Result reopened = period.Reopen(reason, laterPeriodIsClosed: false);

        reopened.IsFailure.Should().BeTrue();
        reopened.Error.Code.Should().Be("finance.period.reopen_reason_required");
        period.IsOpen.Should().BeFalse();
    }

    /// <summary>A reason longer than the column is refused rather than truncated.</summary>
    [Fact]
    public void A_reason_longer_than_the_limit_is_refused()
    {
        AccountingPeriod period = Closed();

        Result reopened = period.Reopen(
            new string('x', AccountingPeriod.MaxReasonLength + 1), laterPeriodIsClosed: false);

        reopened.IsFailure.Should().BeTrue();
        reopened.Error.Code.Should().Be("finance.period.reason_too_long");
    }

    /// <summary>
    /// A later month already closed computed its year-to-date figures over this one. Reopening
    /// underneath it would change a report that has already been read.
    /// </summary>
    [Fact]
    public void A_month_underneath_a_closed_one_cannot_be_reopened()
    {
        AccountingPeriod period = Closed();

        Result reopened = period.Reopen("April was wrong too.", laterPeriodIsClosed: true);

        reopened.IsFailure.Should().BeTrue();
        reopened.Error.Code.Should().Be("finance.period.later_closed");
        period.IsOpen.Should().BeFalse();
    }

    /// <summary>An open month is already open.</summary>
    [Fact]
    public void An_open_month_cannot_be_reopened()
    {
        AccountingPeriod period = March();

        Result reopened = period.Reopen("Anything.", laterPeriodIsClosed: false);

        reopened.IsFailure.Should().BeTrue();
        reopened.Error.Code.Should().Be("finance.period.already_open");
    }

    /// <summary>
    /// The reason survives the second close, deliberately. "This month was reopened in April and
    /// this is what somebody said about it" is the sentence an auditor is looking for, and
    /// clearing it would erase the only trace.
    /// </summary>
    [Fact]
    public void Closing_again_keeps_the_reason_it_was_reopened_for()
    {
        AccountingPeriod period = Closed();
        period.Reopen("Invoice 4471 arrived late.", laterPeriodIsClosed: false)
            .IsSuccess.Should().BeTrue();

        period.Close(Now, InApril, previousIsClosed: true).IsSuccess.Should().BeTrue();

        period.Status.Should().Be(PeriodStatus.Closed);
        period.ReopenedReason.Should().Be("Invoice 4471 arrived late.");
    }

    /// <summary>A period covers the days of its own month and no others.</summary>
    [Fact]
    public void A_period_covers_only_its_own_month()
    {
        AccountingPeriod period = March();

        period.Covers(new DateOnly(2026, 3, 1)).Should().BeTrue();
        period.Covers(new DateOnly(2026, 3, 31)).Should().BeTrue();
        period.Covers(new DateOnly(2026, 2, 28)).Should().BeFalse();
        period.Covers(new DateOnly(2026, 4, 1)).Should().BeFalse();
        period.Covers(new DateOnly(2025, 3, 15)).Should().BeFalse();
    }

    /// <summary>
    /// The ordinal sorts months across a year boundary, which is the whole reason it exists:
    /// December 2025 comes before January 2026, and comparing year and month separately does not
    /// say so.
    /// </summary>
    [Fact]
    public void The_ordinal_orders_months_across_the_turn_of_the_year()
    {
        int december = AccountingPeriod.Key(2025, 12);
        int january = AccountingPeriod.Key(2026, 1);

        january.Should().Be(december + 1);
        AccountingPeriod.Open(2026, 1).Value.Ordinal.Should().Be(january);
    }

    private static AccountingPeriod March() => AccountingPeriod.Open(2026, 3).Value;

    private static AccountingPeriod Closed()
    {
        AccountingPeriod period = March();
        period.Close(Now, InApril, previousIsClosed: true).IsSuccess.Should().BeTrue();

        return period;
    }
}
