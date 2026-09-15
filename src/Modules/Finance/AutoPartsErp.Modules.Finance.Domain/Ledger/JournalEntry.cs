using AutoPartsErp.Modules.Finance.Domain.Ledger.Events;
using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Finance.Domain.Ledger;

/// <summary>Which side of an account a line lands on.</summary>
public enum EntrySide
{
    /// <summary>Not set.</summary>
    Unknown = 0,

    /// <summary>The left.</summary>
    Debit = 1,

    /// <summary>The right.</summary>
    Credit = 2,
}

/// <summary>Where a journal entry came from.</summary>
public enum JournalSource
{
    /// <summary>Not set.</summary>
    Unknown = 0,

    /// <summary>Somebody typed it.</summary>
    Manual = 1,

    /// <summary>A customer document.</summary>
    Sales = 2,

    /// <summary>A supplier document.</summary>
    Purchases = 3,

    /// <summary>Money in or out.</summary>
    Cash = 4,

    /// <summary>Something that happened to stock.</summary>
    Inventory = 5,
}

/// <summary>Where an entry is in its life.</summary>
public enum JournalEntryStatus
{
    /// <summary>Not set.</summary>
    Unknown = 0,

    /// <summary>Being built. Nothing has reached a balance yet.</summary>
    Draft = 1,

    /// <summary>Posted. The balances moved and nothing here changes again.</summary>
    Posted = 2,
}

/// <summary>
/// One entry in the general ledger: a date, a reason, and lines that add up to nothing.
/// <para>
/// This is the piece everything else in this system has been waiting for. The cost of a sale is
/// stamped on a stock movement and posted nowhere; a shortfall written off in transit carries its
/// value in an event nobody consumes; a supplier's price variance corrects the shelf and leaves
/// the part already sold with nowhere to go. All three are the same shape of gap — a real
/// financial fact with no account to land on — and this is the account.
/// </para>
/// <para>
/// <b>An entry is posted once and never edited.</b> A correction is a second entry that reverses
/// the first, which is not bureaucracy: the trial balance for a month somebody has already
/// reported has to keep saying what it said, and an edit would silently restate it. That is also
/// why <see cref="Post"/> is the only thing that moves it out of draft and there is no way back.
/// </para>
/// <para>
/// <b>It has to balance before it can post.</b> Debits equal credits, to the cent, or nothing
/// happens. An unbalanced entry in a ledger is not a small error to fix later — it is a ledger
/// that no longer proves anything, and every report built on it inherits the doubt.
/// </para>
/// </summary>
public sealed class JournalEntry : AggregateRoot<JournalEntryId>, IAuditable, ITenantScoped
{
    /// <summary>Longest permitted entry number.</summary>
    public const int MaxNumberLength = 30;

    /// <summary>Longest permitted description.</summary>
    public const int MaxDescriptionLength = 300;

    /// <summary>Longest permitted source reference.</summary>
    public const int MaxReferenceLength = 60;

    private readonly List<JournalLine> _lines = [];

    private JournalEntry(
        JournalEntryId id,
        string number,
        DateOnly entryDate,
        JournalSource source,
        string description,
        string? reference,
        Currency currency)
        : base(id)
    {
        Number = number;
        EntryDate = entryDate;
        Source = source;
        Description = description;
        Reference = reference;
        CurrencyCode = currency.Code;
        Status = JournalEntryStatus.Draft;
        CreatedBy = string.Empty;
    }

    /// <summary>Required by EF Core materialization.</summary>
#pragma warning disable CS8618
    private JournalEntry()
    {
    }
#pragma warning restore CS8618

    /// <summary>Our number for the entry, from the module's counter.</summary>
    public string Number { get; private set; } = string.Empty;

    /// <summary>
    /// The day the entry belongs to, which is what decides the period it falls in.
    /// <para>
    /// The date of the fact, not the date somebody typed it. A delivery on the thirty-first of
    /// March entered on the second of April belongs to March, and a ledger that used the typing
    /// date would move it into a quarter it has nothing to do with.
    /// </para>
    /// </summary>
    public DateOnly EntryDate { get; private set; }

    /// <summary>Where it came from.</summary>
    public JournalSource Source { get; private set; }

    /// <summary>What it is for, in words somebody will read on a trial balance.</summary>
    public string Description { get; private set; } = string.Empty;

    /// <summary>The document behind it, as its own module numbers it.</summary>
    public string? Reference { get; private set; }

    /// <summary>The currency every line is in.</summary>
    public string CurrencyCode { get; private set; } = Currency.Default.Code;

    /// <summary>Where it is in its life.</summary>
    public JournalEntryStatus Status { get; private set; }

    /// <summary>When it was posted.</summary>
    public DateTimeOffset? PostedAtUtc { get; private set; }

    /// <summary>The entry this one reverses, when it is a correction.</summary>
    public JournalEntryId? ReversesId { get; private set; }

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

    /// <summary>Its lines.</summary>
    public IReadOnlyList<JournalLine> Lines => _lines;

    /// <summary>The currency every line is in.</summary>
    public Currency Currency => Currency.FromCode(CurrencyCode);

    /// <summary>True while lines can still be added.</summary>
    public bool IsDraft => Status == JournalEntryStatus.Draft;

    /// <summary>Everything on the left.</summary>
    public Money TotalDebits => Sum(EntrySide.Debit);

    /// <summary>Everything on the right.</summary>
    public Money TotalCredits => Sum(EntrySide.Credit);

    /// <summary>
    /// True when the two sides agree to the cent.
    /// <para>
    /// The one thing that has to be true before anything moves. Compared as money rather than
    /// through a tolerance: the lines were each rounded when they were written, and two sides that
    /// do not meet exactly mean whoever built the entry divided something and lost a cent.
    /// </para>
    /// </summary>
    public bool IsBalanced => _lines.Count > 0 && TotalDebits.Amount == TotalCredits.Amount;

    /// <summary>Opens a draft entry.</summary>
    /// <param name="number">The entry number, from the module's counter.</param>
    /// <param name="entryDate">The day the entry belongs to.</param>
    /// <param name="source">Where it came from.</param>
    /// <param name="description">What it is for.</param>
    /// <param name="currency">The currency every line is in.</param>
    /// <param name="reference">The document behind it, as its own module numbers it.</param>
    public static Result<JournalEntry> Draft(
        string? number,
        DateOnly entryDate,
        JournalSource source,
        string? description,
        Currency currency,
        string? reference = null)
    {
        ArgumentNullException.ThrowIfNull(currency);

        if (string.IsNullOrWhiteSpace(number))
        {
            return FinanceErrors.Journal.NumberRequired;
        }

        if (source == JournalSource.Unknown)
        {
            return FinanceErrors.Journal.SourceRequired;
        }

        // A trial balance of forty lines reading "Adjustment" forty times is a trial balance
        // nobody can audit. The description is what makes a number explainable a year later.
        if (string.IsNullOrWhiteSpace(description))
        {
            return FinanceErrors.Journal.DescriptionRequired;
        }

        if (description.Trim().Length > MaxDescriptionLength)
        {
            return FinanceErrors.Journal.DescriptionTooLong;
        }

        return new JournalEntry(
            JournalEntryId.New(),
            number.Trim().ToUpperInvariant(),
            entryDate,
            source,
            description.Trim(),
            Clip(reference, MaxReferenceLength),
            currency);
    }

    /// <summary>
    /// Adds a line to a draft entry.
    /// </summary>
    /// <param name="account">The account it lands on. Has to allow postings.</param>
    /// <param name="side">Debit or credit.</param>
    /// <param name="amount">How much. Positive — the side carries the direction.</param>
    /// <param name="narrative">What this line in particular is for, when the entry's own is not enough.</param>
    public Result<JournalLineId> AddLine(
        Account account,
        EntrySide side,
        Money amount,
        string? narrative = null)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(amount);

        if (!IsDraft)
        {
            return FinanceErrors.Journal.AlreadyPosted;
        }

        if (!account.CanTakePostings)
        {
            return account.IsActive
                ? FinanceErrors.Journal.AccountIsAGroup(account.Code)
                : FinanceErrors.Journal.AccountInactive(account.Code);
        }

        if (side == EntrySide.Unknown)
        {
            return FinanceErrors.Journal.SideRequired;
        }

        if (amount.Currency != Currency)
        {
            return FinanceErrors.Journal.CurrencyMismatch;
        }

        // Zero is refused as well as negative. A line for nothing moves no balance and only makes
        // the entry longer, and a negative one is somebody expressing a credit as a negative debit
        // — which is the same fact written in a way the other side of the ledger cannot see.
        if (!amount.IsPositive)
        {
            return FinanceErrors.Journal.AmountNotPositive;
        }

        JournalLine line = JournalLine.Create(
            account.Id, account.Code, side, amount, Clip(narrative, MaxDescriptionLength));

        _lines.Add(line);

        return line.Id;
    }

    /// <summary>Takes a line off a draft entry.</summary>
    /// <param name="lineId">The line.</param>
    public Result RemoveLine(JournalLineId lineId)
    {
        if (!IsDraft)
        {
            return FinanceErrors.Journal.AlreadyPosted;
        }

        JournalLine? line = _lines.Find(candidate => candidate.Id == lineId);

        if (line is null)
        {
            return FinanceErrors.Journal.LineNotFound(lineId.ToString());
        }

        _lines.Remove(line);

        return Result.Success();
    }

    /// <summary>
    /// Posts the entry. The balances move, and nothing here changes again.
    /// </summary>
    /// <param name="now">The current instant.</param>
    public Result Post(DateTimeOffset now)
    {
        if (!IsDraft)
        {
            return FinanceErrors.Journal.AlreadyPosted;
        }

        if (_lines.Count == 0)
        {
            return FinanceErrors.Journal.NoLines;
        }

        // Both sides have to exist, not only agree. A single line of zero on each side would
        // balance arithmetically and post nothing, and an entry that is all debits balancing all
        // debits is caught here rather than by somebody reading a trial balance in April.
        if (TotalDebits.Amount == 0m || TotalCredits.Amount == 0m)
        {
            return FinanceErrors.Journal.OneSidedEntry;
        }

        if (!IsBalanced)
        {
            return FinanceErrors.Journal.OutOfBalance(
                TotalDebits.Amount, TotalCredits.Amount);
        }

        Status = JournalEntryStatus.Posted;
        PostedAtUtc = now;

        Raise(new JournalEntryPostedDomainEvent(
            Id, Number, EntryDate, Source, Description, TotalDebits.Amount, CurrencyCode));

        return Result.Success();
    }

    /// <summary>
    /// Builds the entry that reverses this one, line for line and side for side.
    /// <para>
    /// The only way to undo a posting. The original keeps saying what it said, which is what a
    /// trial balance somebody has already reported requires, and the two together net to nothing.
    /// </para>
    /// </summary>
    /// <param name="number">The reversing entry's own number.</param>
    /// <param name="entryDate">The day the reversal belongs to.</param>
    /// <param name="reason">Why it is being reversed.</param>
    public Result<JournalEntry> BuildReversal(string? number, DateOnly entryDate, string? reason)
    {
        if (Status != JournalEntryStatus.Posted)
        {
            return FinanceErrors.Journal.NotPosted;
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return FinanceErrors.Journal.ReversalReasonRequired;
        }

        // Dated on or after the original. A reversal posted into an earlier month would change a
        // period that was closed before the mistake was even made.
        if (entryDate < EntryDate)
        {
            return FinanceErrors.Journal.ReversalBeforeOriginal;
        }

        Result<JournalEntry> reversal = Draft(
            number,
            entryDate,
            Source,
            $"Reversal of {Number}: {reason.Trim()}",
            Currency,
            Reference);

        if (reversal.IsFailure)
        {
            return reversal;
        }

        foreach (JournalLine line in _lines)
        {
            reversal.Value._lines.Add(JournalLine.Create(
                line.AccountId,
                line.AccountCode,
                line.Side == EntrySide.Debit ? EntrySide.Credit : EntrySide.Debit,
                line.Amount,
                line.Narrative));
        }

        reversal.Value.ReversesId = Id;

        return reversal;
    }

    private Money Sum(EntrySide side) =>
        _lines
            .Where(line => line.Side == side)
            .Aggregate(Money.Zero(Currency), (running, line) => running + line.Amount);

    private static string? Clip(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string trimmed = value.Trim();

        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
