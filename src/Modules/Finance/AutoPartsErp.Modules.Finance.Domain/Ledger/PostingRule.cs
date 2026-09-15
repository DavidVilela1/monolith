using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Finance.Domain.Ledger;

/// <summary>
/// One line of a mapping: an amount a fact carries, a side, and the account it lands on.
/// </summary>
public sealed class PostingRuleLine : Entity<PostingRuleLineId>, ITenantScoped
{
    internal PostingRuleLine(
        PostingRuleLineId id,
        string amountKey,
        EntrySide side,
        string accountCode,
        string? narrative)
        : base(id)
    {
        AmountKey = amountKey;
        Side = side;
        AccountCode = accountCode;
        Narrative = narrative;
    }

    /// <summary>Required by EF Core materialization.</summary>
#pragma warning disable CS8618
    private PostingRuleLine()
    {
    }
#pragma warning restore CS8618

    /// <summary>Which of the fact's amounts this line posts.</summary>
    public string AmountKey { get; private set; } = string.Empty;

    /// <summary>Which side it lands on when the amount is positive.</summary>
    public EntrySide Side { get; private set; }

    /// <summary>The account it lands on.</summary>
    public string AccountCode { get; private set; } = string.Empty;

    /// <summary>What to write on the entry line, when the fact's own description is not enough.</summary>
    public string? Narrative { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; set; }
}

/// <summary>
/// What one financial fact turns into, in the accountant's own account codes.
/// <para>
/// The seam between a system that knows what happened and a chart of accounts that says where it
/// belongs. Everything upstream already produces the facts — a sale was invoiced, stock left at a
/// cost, a supplier charged more than the receipt was booked at — and each of them has been
/// landing nowhere, because the mapping is not a thing a programmer can guess. Portugal's SNC has
/// a standard chart and every accountant has opinions about the sub-accounts inside it; a rule is
/// how those opinions get into the system without anybody editing C#.
/// </para>
/// <para>
/// <b>A rule is per fact type, and there is at most one active one.</b> Two rules for the same
/// fact would each post their own version of it and the ledger would carry a sale twice. The
/// second one is refused when it is written, not when it fires.
/// </para>
/// <para>
/// <b>Account codes are text here, not references.</b> The rule is configuration and the chart is
/// data; checking that the code exists is the posting command's job, because a rule written on
/// Monday for an account opening on Tuesday is a normal way to work. What the rule will not do is
/// post to a code that turns out not to exist — it fails, loudly, at that point.
/// </para>
/// </summary>
public sealed class PostingRule : AggregateRoot<PostingRuleId>, IAuditable, ITenantScoped
{
    /// <summary>Longest permitted description.</summary>
    public const int MaxDescriptionLength = 200;

    /// <summary>Longest permitted line narrative.</summary>
    public const int MaxNarrativeLength = 200;

    /// <summary>Most lines one rule may have.</summary>
    public const int MaxLines = 20;

    private readonly List<PostingRuleLine> _lines = [];

    private PostingRule(
        PostingRuleId id,
        string factType,
        string description,
        JournalSource source)
        : base(id)
    {
        FactType = factType;
        Description = description;
        Source = source;
        IsActive = true;
        CreatedBy = string.Empty;
    }

    /// <summary>Required by EF Core materialization.</summary>
#pragma warning disable CS8618
    private PostingRule()
    {
    }
#pragma warning restore CS8618

    /// <summary>The fact this maps, as <see cref="PostingFacts"/> names it.</summary>
    public string FactType { get; private set; } = string.Empty;

    /// <summary>What the entry is for, written onto every entry this rule produces.</summary>
    public string Description { get; private set; } = string.Empty;

    /// <summary>Which journal the entries belong to.</summary>
    public JournalSource Source { get; private set; }

    /// <summary>
    /// False once the rule is no longer used.
    /// <para>
    /// Deactivated rather than deleted, because a rule is the explanation of every entry it ever
    /// produced. "Why did March's stock go up by 4.412,18?" is answered by the rule that was
    /// active in March, and deleting it deletes the answer.
    /// </para>
    /// </summary>
    public bool IsActive { get; private set; }

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

    /// <summary>The lines, in the order they were written.</summary>
    public IReadOnlyCollection<PostingRuleLine> Lines => _lines.AsReadOnly();

    /// <summary>Defines a mapping for a fact.</summary>
    /// <param name="factType">The fact, as <see cref="PostingFacts"/> names it.</param>
    /// <param name="description">What the entry is for.</param>
    /// <param name="source">Which journal the entries belong to.</param>
    public static Result<PostingRule> Define(
        string? factType,
        string? description,
        JournalSource source)
    {
        if (!PostingFacts.IsKnown(factType))
        {
            return FinanceErrors.Posting.UnknownFact(factType ?? "?");
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            return FinanceErrors.Posting.DescriptionRequired;
        }

        if (description.Trim().Length > MaxDescriptionLength)
        {
            return FinanceErrors.Posting.DescriptionTooLong;
        }

        if (source == JournalSource.Unknown)
        {
            return FinanceErrors.Journal.SourceRequired;
        }

        return new PostingRule(
            PostingRuleId.New(), factType!, description.Trim(), source);
    }

    /// <summary>Adds a line to the mapping.</summary>
    /// <param name="amountKey">Which of the fact's amounts to post.</param>
    /// <param name="side">Which side it lands on when the amount is positive.</param>
    /// <param name="accountCode">The account it lands on.</param>
    /// <param name="narrative">What to write on the entry line.</param>
    public Result<PostingRuleLineId> AddLine(
        string? amountKey,
        EntrySide side,
        string? accountCode,
        string? narrative = null)
    {
        // Checked against the fact's own catalogue, which is the whole point of having one. A
        // line naming an amount the fact does not carry would be a rule that silently posts
        // nothing, discovered when somebody reconciles the month.
        if (!PostingFacts.Carries(FactType, amountKey))
        {
            return FinanceErrors.Posting.UnknownAmount(FactType, amountKey ?? "?");
        }

        if (side == EntrySide.Unknown)
        {
            return FinanceErrors.Journal.SideRequired;
        }

        if (string.IsNullOrWhiteSpace(accountCode))
        {
            return FinanceErrors.Posting.AccountCodeRequired;
        }

        if (accountCode.Trim().Length > Account.MaxCodeLength)
        {
            return FinanceErrors.Account.CodeTooLong;
        }

        if (narrative is not null && narrative.Trim().Length > MaxNarrativeLength)
        {
            return FinanceErrors.Posting.NarrativeTooLong;
        }

        if (_lines.Count >= MaxLines)
        {
            return FinanceErrors.Posting.TooManyLines;
        }

        string code = accountCode.Trim().ToUpperInvariant();

        // The same amount landing twice on the same account and side is a duplication, not a
        // split: it would post the figure twice and the entry would not balance.
        foreach (PostingRuleLine existing in _lines)
        {
            if (string.Equals(existing.AmountKey, amountKey, StringComparison.Ordinal)
                && existing.Side == side
                && string.Equals(existing.AccountCode, code, StringComparison.Ordinal))
            {
                return FinanceErrors.Posting.DuplicateLine;
            }
        }

        var line = new PostingRuleLine(
            PostingRuleLineId.New(),
            amountKey!,
            side,
            code,
            string.IsNullOrWhiteSpace(narrative) ? null : narrative.Trim());

        _lines.Add(line);

        return line.Id;
    }

    /// <summary>Removes a line.</summary>
    /// <param name="lineId">The line.</param>
    public Result RemoveLine(PostingRuleLineId lineId)
    {
        PostingRuleLine? line = _lines.Find(candidate => candidate.Id == lineId);

        if (line is null)
        {
            return FinanceErrors.Posting.LineNotFound;
        }

        _lines.Remove(line);

        return Result.Success();
    }

    /// <summary>Changes what the entries say they are for.</summary>
    /// <param name="description">The new description.</param>
    public Result Describe(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return FinanceErrors.Posting.DescriptionRequired;
        }

        if (description.Trim().Length > MaxDescriptionLength)
        {
            return FinanceErrors.Posting.DescriptionTooLong;
        }

        Description = description.Trim();

        return Result.Success();
    }

    /// <summary>Takes the rule out of use. The fact stops being posted.</summary>
    public void Deactivate() => IsActive = false;

    /// <summary>Puts the rule back into use.</summary>
    public void Activate() => IsActive = true;

    /// <summary>
    /// Turns the amounts a fact carries into the lines of an entry.
    /// <para>
    /// <b>A negative amount flips its side and posts the absolute value.</b> The rest of the
    /// ledger insists a line is positive and the side carries the direction, and a price variance
    /// or an adjustment can honestly go either way — so a rule is written for one direction and
    /// the arithmetic takes care of the other. A credit written as a negative debit is the same
    /// fact in a form the other half of the ledger cannot see.
    /// </para>
    /// <para>
    /// <b>A zero amount posts nothing.</b> An exempt sale carries no VAT, and a line reading
    /// "0,00 to VAT payable" is noise on every exempt document the company ever issues.
    /// </para>
    /// <para>
    /// This does not check that the result balances. It cannot: whether net plus VAT equals gross
    /// is a fact about the amounts, not about the mapping. The entry checks it at posting, and a
    /// rule written wrongly fails there — visibly, on the first fact it meets, rather than
    /// quietly.
    /// </para>
    /// </summary>
    /// <param name="amounts">What the fact carries, by key.</param>
    public Result<IReadOnlyList<PostingInstruction>> Apply(
        IReadOnlyDictionary<string, Money> amounts)
    {
        ArgumentNullException.ThrowIfNull(amounts);

        if (!IsActive)
        {
            return FinanceErrors.Posting.RuleInactive(FactType);
        }

        if (_lines.Count == 0)
        {
            return FinanceErrors.Posting.RuleHasNoLines(FactType);
        }

        var instructions = new List<PostingInstruction>(_lines.Count);
        Currency? currency = null;

        foreach (PostingRuleLine line in _lines)
        {
            if (!amounts.TryGetValue(line.AmountKey, out Money? amount))
            {
                return FinanceErrors.Posting.AmountMissing(FactType, line.AmountKey);
            }

            currency ??= amount.Currency;

            if (amount.Currency != currency)
            {
                return FinanceErrors.Posting.MixedCurrencies;
            }

            if (amount.IsZero)
            {
                continue;
            }

            EntrySide side = amount.IsNegative ? Opposite(line.Side) : line.Side;
            Money posted = amount.IsNegative ? amount.Negate() : amount;

            instructions.Add(new PostingInstruction(
                line.AccountCode, side, posted, line.Narrative));
        }

        if (instructions.Count == 0)
        {
            return FinanceErrors.Posting.NothingToPost(FactType);
        }

        return instructions;
    }

    private static EntrySide Opposite(EntrySide side) =>
        side == EntrySide.Debit ? EntrySide.Credit : EntrySide.Debit;
}

/// <summary>
/// One line of an entry, as a rule works it out: where it lands, which way, and how much.
/// </summary>
/// <param name="AccountCode">The account.</param>
/// <param name="Side">Debit or credit, after any flip for a negative amount.</param>
/// <param name="Amount">How much, always positive.</param>
/// <param name="Narrative">What the line is for, when the rule gave one.</param>
public sealed record PostingInstruction(
    string AccountCode,
    EntrySide Side,
    Money Amount,
    string? Narrative);
