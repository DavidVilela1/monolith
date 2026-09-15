using AutoPartsErp.Modules.Finance.Domain.Ledger.Events;
using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Finance.Domain.Ledger;

/// <summary>Whether a period still takes entries.</summary>
public enum PeriodStatus
{
    /// <summary>Not set.</summary>
    Unknown = 0,

    /// <summary>Entries may still be posted into it.</summary>
    Open = 1,

    /// <summary>It has been reported and nothing more lands in it.</summary>
    Closed = 2,
}

/// <summary>
/// One accounting month, and whether anything may still be posted into it.
/// <para>
/// The guard that makes the rest of the ledger worth trusting. Without it, an entry dated the
/// thirty-first of March can be posted in July — after the VAT for March has been declared, after
/// the accounts have been shown to somebody, after the figure stopped being provisional. The
/// ledger would still balance and every report of March would quietly have changed.
/// </para>
/// <para>
/// <b>Months, not quarters or years.</b> Portugal's VAT is declared monthly or quarterly and a
/// quarter is three months; a month is the smallest unit any of it is reported in, so it is the
/// unit that can be closed. Anything larger is closed by closing the months inside it.
/// </para>
/// <para>
/// <b>Closing is in order and leaves no holes.</b> A March closed while February is still open is
/// a February somebody can still post into after March has been reported — and the year-to-date
/// figures on that March report would change afterwards. The sequence is the whole point.
/// </para>
/// </summary>
public sealed class AccountingPeriod : AggregateRoot<AccountingPeriodId>, IAuditable, ITenantScoped
{
    /// <summary>Longest permitted reason for reopening.</summary>
    public const int MaxReasonLength = 500;

    private AccountingPeriod(AccountingPeriodId id, int year, int month)
        : base(id)
    {
        Year = year;
        Month = month;
        Status = PeriodStatus.Open;
        CreatedBy = string.Empty;
    }

    /// <summary>Required by EF Core materialization.</summary>
#pragma warning disable CS8618
    private AccountingPeriod()
    {
    }
#pragma warning restore CS8618

    /// <summary>The calendar year.</summary>
    public int Year { get; private set; }

    /// <summary>The calendar month, 1 to 12.</summary>
    public int Month { get; private set; }

    /// <summary>Whether anything may still be posted into it.</summary>
    public PeriodStatus Status { get; private set; }

    /// <summary>When it was closed.</summary>
    public DateTimeOffset? ClosedAtUtc { get; private set; }

    /// <summary>
    /// Why it was last reopened, when it has been.
    /// <para>
    /// Kept after the period is closed again, deliberately. "This month was reopened in August
    /// and this is what somebody said about it" is exactly the sentence an auditor is looking for,
    /// and clearing it on the second close would erase the only trace.
    /// </para>
    /// </summary>
    public string? ReopenedReason { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <inheritdoc />
    public string CreatedBy { get; set; } = string.Empty;

    /// <inheritdoc />
    public DateTimeOffset? ModifiedAtUtc { get; set; }

    /// <inheritdoc />
    public string? ModifiedBy { get; set; }

    /// <summary>The first day of the month.</summary>
    public DateOnly From => new(Year, Month, 1);

    /// <summary>The last day of the month.</summary>
    public DateOnly To => new(Year, Month, DateTime.DaysInMonth(Year, Month));

    /// <summary>True while entries may still be posted into it.</summary>
    public bool IsOpen => Status == PeriodStatus.Open;

    /// <summary>
    /// How this period sorts against another, as one number.
    /// <para>
    /// Year times twelve plus month, so "is this one before that one?" is an integer comparison
    /// rather than two. It is also the column an index can order by.
    /// </para>
    /// </summary>
    public int Ordinal => Key(Year, Month);

    /// <summary>Opens a period.</summary>
    /// <param name="year">The calendar year.</param>
    /// <param name="month">The calendar month, 1 to 12.</param>
    public static Result<AccountingPeriod> Open(int year, int month)
    {
        if (year is < 2000 or > 2999)
        {
            return FinanceErrors.Period.YearOutOfRange;
        }

        if (month is < 1 or > 12)
        {
            return FinanceErrors.Period.MonthOutOfRange;
        }

        return new AccountingPeriod(AccountingPeriodId.New(), year, month);
    }

    /// <summary>Sorts a year and month as one number, the way <see cref="Ordinal"/> does.</summary>
    /// <param name="year">The year.</param>
    /// <param name="month">The month.</param>
    public static int Key(int year, int month) => (year * 12) + month;

    /// <summary>
    /// Closes the period. Nothing more is posted into it.
    /// </summary>
    /// <param name="now">The current instant.</param>
    /// <param name="today">Today. A period still running cannot be closed.</param>
    /// <param name="previousIsClosed">
    /// Whether the month before this one is closed, or there is none. Passed in rather than read,
    /// because "every earlier period" is a statement about other rows and an aggregate can only
    /// ever see itself.
    /// </param>
    public Result Close(DateTimeOffset now, DateOnly today, bool previousIsClosed)
    {
        if (!IsOpen)
        {
            return FinanceErrors.Period.AlreadyClosed;
        }

        // A month still running would stop taking the entries that belong to it — the last three
        // days of it would have nowhere to go, and somebody would post them into the next month
        // to get the work done.
        if (today <= To)
        {
            return FinanceErrors.Period.NotOverYet;
        }

        if (!previousIsClosed)
        {
            return FinanceErrors.Period.EarlierPeriodOpen;
        }

        Status = PeriodStatus.Closed;
        ClosedAtUtc = now;

        Raise(new AccountingPeriodClosedDomainEvent(Id, Year, Month, From, To));

        return Result.Success();
    }

    /// <summary>
    /// Reopens the period, with a reason somebody wrote.
    /// </summary>
    /// <param name="reason">Why it is being reopened.</param>
    /// <param name="laterPeriodIsClosed">
    /// Whether any month after this one is already closed. Passed in for the same reason the close
    /// check is.
    /// </param>
    public Result Reopen(string? reason, bool laterPeriodIsClosed)
    {
        if (IsOpen)
        {
            return FinanceErrors.Period.AlreadyOpen;
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return FinanceErrors.Period.ReopenReasonRequired;
        }

        if (reason.Trim().Length > MaxReasonLength)
        {
            return FinanceErrors.Period.ReasonTooLong;
        }

        // A later month already closed means its year-to-date figures were computed over this one.
        // Reopening underneath them would change a report that has already been read, which is
        // exactly what closing exists to prevent — so the later months come back first, in order.
        if (laterPeriodIsClosed)
        {
            return FinanceErrors.Period.LaterPeriodClosed;
        }

        Status = PeriodStatus.Open;
        ClosedAtUtc = null;
        ReopenedReason = reason.Trim();

        Raise(new AccountingPeriodReopenedDomainEvent(Id, Year, Month, ReopenedReason));

        return Result.Success();
    }

    /// <summary>True when a given day falls inside this period.</summary>
    /// <param name="on">The day.</param>
    public bool Covers(DateOnly on) => on.Year == Year && on.Month == Month;
}
