using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Finance.Domain.Ledger;

/// <summary>What became of a fact that was handed to the ledger.</summary>
public enum PostingStatus
{
    /// <summary>Not set.</summary>
    Unknown = 0,

    /// <summary>It could not be posted and is waiting for somebody.</summary>
    Waiting = 1,

    /// <summary>It reached the ledger.</summary>
    Posted = 2,

    /// <summary>Somebody decided it does not belong in the ledger.</summary>
    Dismissed = 3,
}

/// <summary>One of the amounts a fact carried.</summary>
public sealed class FactAmount : Entity<FactAmountId>, ITenantScoped
{
    internal FactAmount(FactAmountId id, string key, Money amount)
        : base(id)
    {
        Key = key;
        Amount = amount;
    }

    /// <summary>Required by EF Core materialization.</summary>
#pragma warning disable CS8618
    private FactAmount()
    {
    }
#pragma warning restore CS8618

    /// <summary>Which amount, as the catalogue names it.</summary>
    public string Key { get; private set; } = string.Empty;

    /// <summary>How much.</summary>
    public Money Amount { get; private set; } = null!;

    /// <inheritdoc />
    public Guid TenantId { get; set; }
}

/// <summary>
/// The record of every fact the ledger was asked to post, and what became of it.
/// <para>
/// This is the piece that stops the ledger being quietly incomplete. A fact arriving with no rule
/// to map it has to go somewhere: dropping it leaves a general ledger that is missing a month of
/// sales and says so nowhere, which is the worst failure this module has, because every report
/// built on it still looks right. So the fact is kept — what it was, when, what it was worth —
/// and it waits.
/// </para>
/// <para>
/// <b>It is also what makes the handlers idempotent.</b> The outbox delivers at least once, and a
/// row already here for this fact and this document means the message has been seen before.
/// Posting a sale twice is an error nobody finds until the year-end, so the same unique key that
/// answers "has this been dealt with?" is the one that prevents it.
/// </para>
/// <para>
/// <b>Waiting is not failing.</b> A fact that arrives before the accountant has written the rule
/// is the normal way this system is installed: everything waits, the rules go in, and the backlog
/// is posted. What would be failing is losing them meanwhile.
/// </para>
/// </summary>
public sealed class FactPosting : AggregateRoot<FactPostingId>, IAuditable, ITenantScoped
{
    /// <summary>Longest permitted reference.</summary>
    public const int MaxReferenceLength = 60;

    /// <summary>Longest permitted reason.</summary>
    public const int MaxReasonLength = 500;

    private readonly List<FactAmount> _amounts = [];

    private FactPosting(
        FactPostingId id,
        string factType,
        string reference,
        DateOnly occurredOn,
        string description,
        Currency currency)
        : base(id)
    {
        FactType = factType;
        Reference = reference;
        OccurredOn = occurredOn;
        Description = description;
        CurrencyCode = currency.Code;
        Status = PostingStatus.Waiting;
        CreatedBy = string.Empty;
    }

    /// <summary>Required by EF Core materialization.</summary>
#pragma warning disable CS8618
    private FactPosting()
    {
    }
#pragma warning restore CS8618

    /// <summary>The fact, as <see cref="PostingFacts"/> names it.</summary>
    public string FactType { get; private set; } = string.Empty;

    /// <summary>
    /// The document behind it, as its own module numbers it.
    /// <para>
    /// With the fact type it is the natural key: one document produces one sale posting, and the
    /// same document arriving twice is the outbox doing its job.
    /// </para>
    /// </summary>
    public string Reference { get; private set; } = string.Empty;

    /// <summary>The day it belongs to, which decides its period.</summary>
    public DateOnly OccurredOn { get; private set; }

    /// <summary>What it is, for somebody reading the waiting list.</summary>
    public string Description { get; private set; } = string.Empty;

    /// <summary>The currency the amounts are in.</summary>
    public string CurrencyCode { get; private set; } = Currency.Default.Code;

    /// <summary>What became of it.</summary>
    public PostingStatus Status { get; private set; }

    /// <summary>The entry it produced, once it produced one.</summary>
    public JournalEntryId? JournalEntryId { get; private set; }

    /// <summary>
    /// Why it has not been posted, in the words of whatever refused it.
    /// <para>
    /// Kept after a successful posting too. "This waited three weeks because nothing mapped it"
    /// is the sentence that explains a late entry, and clearing it on success would erase it.
    /// </para>
    /// </summary>
    public string? Reason { get; private set; }

    /// <summary>When it reached the ledger.</summary>
    public DateTimeOffset? PostedAtUtc { get; private set; }

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

    /// <summary>What the fact carried.</summary>
    public IReadOnlyCollection<FactAmount> Amounts => _amounts.AsReadOnly();

    /// <summary>The currency the amounts are in.</summary>
    public Currency Currency => Currency.FromCode(CurrencyCode);

    /// <summary>True while it is still waiting for somebody.</summary>
    public bool IsWaiting => Status == PostingStatus.Waiting;

    /// <summary>Records a fact that was handed to the ledger.</summary>
    /// <param name="factType">The fact, as the catalogue names it.</param>
    /// <param name="reference">The document behind it.</param>
    /// <param name="occurredOn">The day it belongs to.</param>
    /// <param name="description">What it is.</param>
    /// <param name="amounts">What it carried, by key.</param>
    public static Result<FactPosting> Record(
        string? factType,
        string? reference,
        DateOnly occurredOn,
        string? description,
        IReadOnlyDictionary<string, Money> amounts)
    {
        ArgumentNullException.ThrowIfNull(amounts);

        if (!PostingFacts.IsKnown(factType))
        {
            return FinanceErrors.Posting.UnknownFact(factType ?? "?");
        }

        if (string.IsNullOrWhiteSpace(reference))
        {
            return FinanceErrors.Posting.ReferenceRequired;
        }

        if (reference.Trim().Length > MaxReferenceLength)
        {
            return FinanceErrors.Posting.ReferenceTooLong;
        }

        if (amounts.Count == 0)
        {
            return FinanceErrors.Posting.NoAmounts;
        }

        Currency? currency = null;

        foreach (Money amount in amounts.Values)
        {
            currency ??= amount.Currency;

            if (amount.Currency != currency)
            {
                return FinanceErrors.Posting.MixedCurrencies;
            }
        }

        var posting = new FactPosting(
            FactPostingId.New(),
            factType!,
            reference.Trim(),
            occurredOn,
            string.IsNullOrWhiteSpace(description) ? factType! : description.Trim(),
            currency!);

        foreach (KeyValuePair<string, Money> amount in amounts)
        {
            posting._amounts.Add(
                new FactAmount(FactAmountId.New(), amount.Key, amount.Value));
        }

        return posting;
    }

    /// <summary>What the fact carried, in the shape a rule wants it.</summary>
    public IReadOnlyDictionary<string, Money> AmountsByKey()
    {
        var amounts = new Dictionary<string, Money>(StringComparer.Ordinal);

        foreach (FactAmount amount in _amounts)
        {
            amounts[amount.Key] = amount.Amount;
        }

        return amounts;
    }

    /// <summary>Records that it reached the ledger.</summary>
    /// <param name="journalEntryId">The entry it produced.</param>
    /// <param name="now">The current instant.</param>
    public Result Posted(JournalEntryId journalEntryId, DateTimeOffset now)
    {
        if (Status == PostingStatus.Posted)
        {
            return FinanceErrors.Posting.AlreadyPosted;
        }

        if (Status == PostingStatus.Dismissed)
        {
            return FinanceErrors.Posting.WasDismissed;
        }

        Status = PostingStatus.Posted;
        JournalEntryId = journalEntryId;
        PostedAtUtc = now;

        return Result.Success();
    }

    /// <summary>
    /// Records why it could not be posted, so the waiting list says something useful.
    /// </summary>
    /// <param name="reason">Why.</param>
    public void CouldNotPost(string? reason) =>
        Reason = string.IsNullOrWhiteSpace(reason)
            ? null
            : reason.Trim()[..Math.Min(reason.Trim().Length, MaxReasonLength)];

    /// <summary>
    /// Takes the fact off the waiting list without posting it.
    /// <para>
    /// For the honest case where something reached the ledger by another route, or belongs to a
    /// period that has been closed and reconciled by hand. It is not a delete: the row stays, with
    /// the sentence explaining it, because "why is there no entry for invoice 4471?" has to have
    /// an answer.
    /// </para>
    /// </summary>
    /// <param name="reason">Why it does not belong in the ledger.</param>
    public Result Dismiss(string? reason)
    {
        if (Status == PostingStatus.Posted)
        {
            return FinanceErrors.Posting.AlreadyPosted;
        }

        if (Status == PostingStatus.Dismissed)
        {
            return FinanceErrors.Posting.AlreadyDismissed;
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return FinanceErrors.Posting.DismissReasonRequired;
        }

        if (reason.Trim().Length > MaxReasonLength)
        {
            return FinanceErrors.Posting.ReasonTooLong;
        }

        Status = PostingStatus.Dismissed;
        Reason = reason.Trim();

        return Result.Success();
    }

    /// <summary>Puts a dismissed fact back on the waiting list.</summary>
    public Result Reinstate()
    {
        if (Status != PostingStatus.Dismissed)
        {
            return FinanceErrors.Posting.NotDismissed;
        }

        Status = PostingStatus.Waiting;

        return Result.Success();
    }
}
