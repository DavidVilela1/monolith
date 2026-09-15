using AutoPartsErp.Modules.Finance.Domain;
using AutoPartsErp.Modules.Finance.Domain.Ledger;
using AutoPartsErp.SharedKernel.Abstractions;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Finance.Application.Ledger.Commands;

/// <summary>Opens an account in the chart.</summary>
/// <param name="Code">The code the accountant refers to it by.</param>
/// <param name="Name">What it is called on a trial balance.</param>
/// <param name="Type">Asset, Liability, Equity, Income or Expense.</param>
/// <param name="AllowsPosting">False for a group account that exists to be summed.</param>
/// <param name="ParentCode">The account above it in the chart, when there is one.</param>
public sealed record OpenAccountCommand(
    string Code,
    string Name,
    string Type,
    bool AllowsPosting = true,
    string? ParentCode = null) : ICommand<Guid>;

/// <summary>Checks the shape of an <see cref="OpenAccountCommand"/>.</summary>
public sealed class OpenAccountCommandValidator : IValidator<OpenAccountCommand>
{
    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(
        OpenAccountCommand instance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var failures = new List<ValidationFailure>();

        if (string.IsNullOrWhiteSpace(instance.Code))
        {
            failures.Add(new ValidationFailure(
                nameof(instance.Code), "required", "An account code is required."));
        }

        if (string.IsNullOrWhiteSpace(instance.Name))
        {
            failures.Add(new ValidationFailure(
                nameof(instance.Name), "required", "An account name is required."));
        }

        if (!Enum.TryParse(instance.Type, ignoreCase: true, out AccountType type)
            || type == AccountType.Unknown)
        {
            failures.Add(new ValidationFailure(
                nameof(instance.Type), "unknown_type",
                "An account is an Asset, a Liability, Equity, Income or an Expense."));
        }

        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}

/// <summary>Opens the account.</summary>
public sealed class OpenAccountCommandHandler : ICommandHandler<OpenAccountCommand, Guid>
{
    private readonly IAccountRepository _accounts;
    private readonly IFinanceUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public OpenAccountCommandHandler(IAccountRepository accounts, IFinanceUnitOfWork unitOfWork)
    {
        _accounts = accounts;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result<Guid>> HandleAsync(
        OpenAccountCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Checked here so a duplicate comes back as a 409 with something to read rather than a 500
        // with a constraint name in it. The unique index is still there and still authoritative.
        if (await _accounts.CodeExistsAsync(request.Code, null, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<Guid>(FinanceErrors.Account.CodeExists);
        }

        AccountId? parentId = null;

        if (!string.IsNullOrWhiteSpace(request.ParentCode))
        {
            Account? parent = await _accounts
                .GetByCodeAsync(request.ParentCode, cancellationToken)
                .ConfigureAwait(false);

            if (parent is null)
            {
                return Result.Failure<Guid>(FinanceErrors.Account.NotFound(request.ParentCode));
            }

            parentId = parent.Id;
        }

        Result<Account> account = Account.Open(
            request.Code,
            request.Name,
            Enum.Parse<AccountType>(request.Type, ignoreCase: true),
            request.AllowsPosting,
            parentId);

        if (account.IsFailure)
        {
            return Result.Failure<Guid>(account.Error);
        }

        _accounts.Add(account.Value);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return account.Value.Id.Value;
    }
}

/// <summary>Renames an account, or takes it out of use. The code and the type do not move.</summary>
/// <param name="AccountId">The account.</param>
/// <param name="Name">The new name.</param>
/// <param name="IsActive">False to stop it taking new entries.</param>
public sealed record AmendAccountCommand(
    Guid AccountId,
    string Name,
    bool IsActive = true) : ICommand;

/// <summary>Amends the account.</summary>
public sealed class AmendAccountCommandHandler : ICommandHandler<AmendAccountCommand>
{
    private readonly IAccountRepository _accounts;
    private readonly IFinanceUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public AmendAccountCommandHandler(IAccountRepository accounts, IFinanceUnitOfWork unitOfWork)
    {
        _accounts = accounts;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        AmendAccountCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Account? account = await _accounts
            .GetByIdAsync(new AccountId(request.AccountId), cancellationToken)
            .ConfigureAwait(false);

        if (account is null)
        {
            return FinanceErrors.Account.NotFound(request.AccountId.ToString());
        }

        Result renamed = account.Rename(request.Name);

        if (renamed.IsFailure)
        {
            return renamed;
        }

        if (request.IsActive)
        {
            account.Reactivate();
        }
        else
        {
            account.Deactivate();
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>
/// Writes an entry and posts it, in one act.
/// <para>
/// One command rather than a draft somebody adds lines to and posts later, because a journal entry
/// is only ever meaningful whole: a half-written one balances to nothing, and leaving drafts about
/// is how a ledger acquires rows that never became facts. The aggregate still has a draft state,
/// because building an entry is a sequence — but that sequence belongs inside one transaction.
/// </para>
/// </summary>
/// <param name="EntryDate">The day the entry belongs to, which decides its period.</param>
/// <param name="Source">Sales, Purchases, Cash, Inventory or Manual.</param>
/// <param name="Description">What it is for.</param>
/// <param name="Lines">Its lines. Debits have to equal credits.</param>
/// <param name="Reference">The document behind it, as its own module numbers it.</param>
public sealed record PostJournalEntryCommand(
    DateOnly EntryDate,
    string Source,
    string Description,
    IReadOnlyList<JournalLineInput> Lines,
    string? Reference = null) : ICommand<Guid>;

/// <summary>One line of an entry, as a request carries it.</summary>
/// <param name="AccountCode">The account it lands on.</param>
/// <param name="Side">Debit or Credit.</param>
/// <param name="Amount">How much. Positive — the side carries the direction.</param>
/// <param name="Narrative">What this line in particular is for.</param>
public sealed record JournalLineInput(
    string AccountCode,
    string Side,
    decimal Amount,
    string? Narrative = null);

/// <summary>Checks the shape of a <see cref="PostJournalEntryCommand"/>.</summary>
public sealed class PostJournalEntryCommandValidator : IValidator<PostJournalEntryCommand>
{
    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(
        PostJournalEntryCommand instance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var failures = new List<ValidationFailure>();

        if (!Enum.TryParse(instance.Source, ignoreCase: true, out JournalSource source)
            || source == JournalSource.Unknown)
        {
            failures.Add(new ValidationFailure(
                nameof(instance.Source), "unknown_source",
                "An entry comes from Sales, Purchases, Cash, Inventory or a person (Manual)."));
        }

        if (string.IsNullOrWhiteSpace(instance.Description))
        {
            failures.Add(new ValidationFailure(
                nameof(instance.Description), "required",
                "Say what the entry is for. A trial balance of forty lines all reading "
                + "'Adjustment' is one nobody can audit."));
        }

        if (instance.Lines is null || instance.Lines.Count < 2)
        {
            failures.Add(new ValidationFailure(
                nameof(instance.Lines), "too_few",
                "An entry needs at least two lines: something has to balance against something."));

            return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
        }

        foreach (JournalLineInput line in instance.Lines)
        {
            if (!Enum.TryParse(line.Side, ignoreCase: true, out EntrySide side)
                || side == EntrySide.Unknown)
            {
                failures.Add(new ValidationFailure(
                    nameof(instance.Lines), "unknown_side", "A line is a Debit or a Credit."));

                break;
            }
        }

        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}

/// <summary>Writes and posts the entry.</summary>
public sealed class PostJournalEntryCommandHandler
    : ICommandHandler<PostJournalEntryCommand, Guid>
{
    private readonly IJournalEntryRepository _entries;
    private readonly IAccountRepository _accounts;
    private readonly IFinanceUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    /// <summary>Initializes the handler.</summary>
    public PostJournalEntryCommandHandler(
        IJournalEntryRepository entries,
        IAccountRepository accounts,
        IFinanceUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _entries = entries;
        _accounts = accounts;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result<Guid>> HandleAsync(
        PostJournalEntryCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Every account the entry names, in one round trip. Asking one at a time would be four
        // queries to answer one question, and the answer decides whether anything happens at all.
        IReadOnlyDictionary<string, Account> accounts = await _accounts
            .GetByCodesAsync(
                [.. request.Lines.Select(line => line.AccountCode)], cancellationToken)
            .ConfigureAwait(false);

        string number = await _entries
            .NextEntryNumberAsync(request.EntryDate.Year, cancellationToken)
            .ConfigureAwait(false);

        Result<JournalEntry> entry = JournalEntry.Draft(
            number,
            request.EntryDate,
            Enum.Parse<JournalSource>(request.Source, ignoreCase: true),
            request.Description,
            Currency.Default,
            request.Reference);

        if (entry.IsFailure)
        {
            return Result.Failure<Guid>(entry.Error);
        }

        foreach (JournalLineInput line in request.Lines)
        {
            string code = line.AccountCode?.Trim().ToUpperInvariant() ?? string.Empty;

            if (!accounts.TryGetValue(code, out Account? account))
            {
                return Result.Failure<Guid>(FinanceErrors.Account.NotFound(line.AccountCode ?? "?"));
            }

            Result<JournalLineId> added = entry.Value.AddLine(
                account,
                Enum.Parse<EntrySide>(line.Side, ignoreCase: true),
                Money.Of(line.Amount, Currency.Default),
                line.Narrative);

            if (added.IsFailure)
            {
                return Result.Failure<Guid>(added.Error);
            }
        }

        Result posted = entry.Value.Post(_clock.UtcNow);

        if (posted.IsFailure)
        {
            return Result.Failure<Guid>(posted.Error);
        }

        _entries.Add(entry.Value);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return entry.Value.Id.Value;
    }
}

/// <summary>Reverses a posted entry with a second one that mirrors it.</summary>
/// <param name="JournalEntryId">The entry to reverse.</param>
/// <param name="EntryDate">The day the reversal belongs to.</param>
/// <param name="Reason">Why.</param>
public sealed record ReverseJournalEntryCommand(
    Guid JournalEntryId,
    DateOnly EntryDate,
    string Reason) : ICommand<Guid>;

/// <summary>Posts the reversal.</summary>
public sealed class ReverseJournalEntryCommandHandler
    : ICommandHandler<ReverseJournalEntryCommand, Guid>
{
    private readonly IJournalEntryRepository _entries;
    private readonly IFinanceUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    /// <summary>Initializes the handler.</summary>
    public ReverseJournalEntryCommandHandler(
        IJournalEntryRepository entries,
        IFinanceUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _entries = entries;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result<Guid>> HandleAsync(
        ReverseJournalEntryCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        JournalEntry? original = await _entries
            .GetByIdAsync(new JournalEntryId(request.JournalEntryId), cancellationToken)
            .ConfigureAwait(false);

        if (original is null)
        {
            return Result.Failure<Guid>(
                FinanceErrors.Journal.NotFound(request.JournalEntryId.ToString()));
        }

        string number = await _entries
            .NextEntryNumberAsync(request.EntryDate.Year, cancellationToken)
            .ConfigureAwait(false);

        Result<JournalEntry> reversal = original.BuildReversal(
            number, request.EntryDate, request.Reason);

        if (reversal.IsFailure)
        {
            return Result.Failure<Guid>(reversal.Error);
        }

        Result posted = reversal.Value.Post(_clock.UtcNow);

        if (posted.IsFailure)
        {
            return Result.Failure<Guid>(posted.Error);
        }

        _entries.Add(reversal.Value);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return reversal.Value.Id.Value;
    }
}
