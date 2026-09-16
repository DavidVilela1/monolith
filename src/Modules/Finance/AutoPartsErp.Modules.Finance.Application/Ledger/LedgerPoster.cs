using AutoPartsErp.Modules.Finance.Application.Ledger.Commands;
using AutoPartsErp.Modules.Finance.Domain;
using AutoPartsErp.Modules.Finance.Domain.Ledger;
using AutoPartsErp.SharedKernel.Abstractions;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Finance.Application.Ledger;

/// <summary>
/// Hands a financial fact to the ledger.
/// <para>
/// The one road every automatic posting takes. Each handler knows how to read its own event and
/// nothing else; what a fact turns into, whether its month is still open, and what to do when
/// none of that works out are decided here, once.
/// </para>
/// </summary>
public interface ILedgerPoster
{
    /// <summary>
    /// Records a fact and posts it if everything needed is in place.
    /// <para>
    /// Succeeds whether or not an entry was written. A fact nobody has mapped yet is not an error
    /// — it is the normal state of a system being installed — so it is kept and it waits. What
    /// the result carries is the entry, when there is one.
    /// </para>
    /// </summary>
    /// <param name="factType">The fact, as <see cref="PostingFacts"/> names it.</param>
    /// <param name="reference">The document behind it, as its own module numbers it.</param>
    /// <param name="occurredOn">The day it belongs to, which decides its period.</param>
    /// <param name="description">What it is, for somebody reading the waiting list.</param>
    /// <param name="amounts">What it carries, by key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<JournalEntryId?>> PostFactAsync(
        string factType,
        string reference,
        DateOnly occurredOn,
        string description,
        IReadOnlyDictionary<string, Money> amounts,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The same thing without committing, for a caller that owns the transaction.
    /// <para>
    /// What a domain event handler inside this module has to use. Domain events are dispatched
    /// before the save rather than after it, so a handler that saved would re-enter the save it
    /// is running inside — and the point of dispatching early is that everything the handlers do
    /// commits with the thing that caused them. A receipt and the entry for it land together or
    /// neither does.
    /// </para>
    /// </summary>
    /// <param name="factType">The fact, as <see cref="PostingFacts"/> names it.</param>
    /// <param name="reference">The document behind it.</param>
    /// <param name="occurredOn">The day it belongs to.</param>
    /// <param name="description">What it is.</param>
    /// <param name="amounts">What it carries, by key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<JournalEntryId?>> RecordFactAsync(
        string factType,
        string reference,
        DateOnly occurredOn,
        string description,
        IReadOnlyDictionary<string, Money> amounts,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tries again to post a fact that is already on the waiting list.
    /// <para>
    /// What a person presses once the rule they were missing exists. Does not save; the caller
    /// owns the transaction.
    /// </para>
    /// </summary>
    /// <param name="posting">The recorded fact.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result> TryPostAsync(FactPosting posting, CancellationToken cancellationToken = default);
}

/// <summary>Posts facts to the ledger through the configured rules.</summary>
public sealed class LedgerPoster : ILedgerPoster
{
    private readonly IFactPostingRepository _facts;
    private readonly IPostingRuleRepository _rules;
    private readonly IAccountRepository _accounts;
    private readonly IJournalEntryRepository _entries;
    private readonly IAccountingPeriodRepository _periods;
    private readonly IFinanceUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    /// <summary>Initializes the poster.</summary>
    public LedgerPoster(
        IFactPostingRepository facts,
        IPostingRuleRepository rules,
        IAccountRepository accounts,
        IJournalEntryRepository entries,
        IAccountingPeriodRepository periods,
        IFinanceUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _facts = facts;
        _rules = rules;
        _accounts = accounts;
        _entries = entries;
        _periods = periods;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result<JournalEntryId?>> PostFactAsync(
        string factType,
        string reference,
        DateOnly occurredOn,
        string description,
        IReadOnlyDictionary<string, Money> amounts,
        CancellationToken cancellationToken = default)
    {
        Result<JournalEntryId?> recorded = await RecordFactAsync(
                factType, reference, occurredOn, description, amounts, cancellationToken)
            .ConfigureAwait(false);

        if (recorded.IsFailure)
        {
            return recorded;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return recorded;
    }

    /// <inheritdoc />
    public async Task<Result<JournalEntryId?>> RecordFactAsync(
        string factType,
        string reference,
        DateOnly occurredOn,
        string description,
        IReadOnlyDictionary<string, Money> amounts,
        CancellationToken cancellationToken = default)
    {
        // The outbox delivers at least once, and a row already here for this fact and this
        // document means the message has been seen before. Posting a sale twice is an error
        // nobody finds until the year-end.
        FactPosting? posting = await _facts
            .GetForAsync(factType, reference, cancellationToken)
            .ConfigureAwait(false);

        if (posting is not null && !posting.IsWaiting)
        {
            return posting.JournalEntryId;
        }

        if (posting is null)
        {
            Result<FactPosting> recorded = FactPosting.Record(
                factType, reference, occurredOn, description, amounts);

            if (recorded.IsFailure)
            {
                return Result.Failure<JournalEntryId?>(recorded.Error);
            }

            posting = recorded.Value;
            _facts.Add(posting);
        }

        await TryPostAsync(posting, cancellationToken).ConfigureAwait(false);

        return posting.JournalEntryId;
    }

    /// <inheritdoc />
    public async Task<Result> TryPostAsync(
        FactPosting posting,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(posting);

        if (!posting.IsWaiting)
        {
            return posting.Status == PostingStatus.Posted
                ? FinanceErrors.Posting.AlreadyPosted
                : FinanceErrors.Posting.WasDismissed;
        }

        PostingRule? rule = await _rules
            .GetForFactAsync(posting.FactType, cancellationToken)
            .ConfigureAwait(false);

        if (rule is null)
        {
            return Refuse(posting, FinanceErrors.Posting.NotFound(posting.FactType));
        }

        Result<IReadOnlyList<PostingInstruction>> instructions =
            rule.Apply(posting.AmountsByKey());

        if (instructions.IsFailure)
        {
            return Refuse(posting, instructions.Error);
        }

        // The same guard the hand-written entry passes through. A fact dated inside a month
        // somebody has already reported waits rather than changing what that month said.
        Result period = await LedgerPeriodGuard
            .EnsureOpenAsync(_periods, posting.OccurredOn, cancellationToken)
            .ConfigureAwait(false);

        if (period.IsFailure)
        {
            return Refuse(posting, period.Error);
        }

        IReadOnlyDictionary<string, Account> accounts = await _accounts
            .GetByCodesAsync(
                [.. instructions.Value.Select(instruction => instruction.AccountCode)],
                cancellationToken)
            .ConfigureAwait(false);

        foreach (PostingInstruction instruction in instructions.Value)
        {
            if (!accounts.ContainsKey(instruction.AccountCode))
            {
                return Refuse(
                    posting, FinanceErrors.Account.NotFound(instruction.AccountCode));
            }
        }

        // Checked before a number is taken, not after. The entry checks it again and would refuse
        // either way, but a rule that does not balance would otherwise burn a journal number on
        // every document that met it, and a ledger with gaps in its numbering is one an auditor
        // asks about.
        Result balanced = CheckBalance(instructions.Value, posting.Currency);

        if (balanced.IsFailure)
        {
            return Refuse(posting, balanced.Error);
        }

        string number = await _entries
            .NextEntryNumberAsync(posting.OccurredOn.Year, cancellationToken)
            .ConfigureAwait(false);

        Result<JournalEntry> entry = JournalEntry.Draft(
            number,
            posting.OccurredOn,
            rule.Source,
            rule.Description,
            posting.Currency,
            posting.Reference);

        if (entry.IsFailure)
        {
            return Refuse(posting, entry.Error);
        }

        foreach (PostingInstruction instruction in instructions.Value)
        {
            Result<JournalLineId> added = entry.Value.AddLine(
                accounts[instruction.AccountCode],
                instruction.Side,
                instruction.Amount,
                instruction.Narrative ?? posting.Description);

            if (added.IsFailure)
            {
                return Refuse(posting, added.Error);
            }
        }

        Result posted = entry.Value.Post(_clock.UtcNow);

        if (posted.IsFailure)
        {
            return Refuse(posting, posted.Error);
        }

        _entries.Add(entry.Value);

        return posting.Posted(entry.Value.Id, _clock.UtcNow);
    }

    private static Result CheckBalance(
        IReadOnlyList<PostingInstruction> instructions,
        Currency currency)
    {
        Money debits = Money.Zero(currency);
        Money credits = Money.Zero(currency);

        foreach (PostingInstruction instruction in instructions)
        {
            if (instruction.Side == EntrySide.Debit)
            {
                debits += instruction.Amount;
            }
            else
            {
                credits += instruction.Amount;
            }
        }

        if (debits.Amount != credits.Amount)
        {
            return FinanceErrors.Journal.OutOfBalance(debits.Amount, credits.Amount);
        }

        return debits.IsZero
            ? FinanceErrors.Journal.OneSidedEntry
            : Result.Success();
    }

    private static Result Refuse(FactPosting posting, Error error)
    {
        posting.CouldNotPost(error.Description);

        return Result.Failure(error);
    }
}
