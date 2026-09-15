using AutoPartsErp.Modules.Finance.Domain;
using AutoPartsErp.Modules.Finance.Domain.Ledger;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Finance.Application.Ledger.Commands;

/// <summary>One line of a mapping, as a request carries it.</summary>
/// <param name="AmountKey">Which of the fact's amounts to post.</param>
/// <param name="Side">Debit or Credit, for when the amount is positive.</param>
/// <param name="AccountCode">The account it lands on.</param>
/// <param name="Narrative">What to write on the entry line.</param>
public sealed record PostingRuleLineInput(
    string AmountKey,
    string Side,
    string AccountCode,
    string? Narrative = null);

/// <summary>
/// Maps a fact to the account codes it lands on.
/// <para>
/// The whole rule arrives at once, lines included, for the same reason a journal entry does: a
/// mapping with half its lines written posts an entry that does not balance, and a half-written
/// one left lying about is how a ledger acquires a fact that reaches one account and not the
/// other.
/// </para>
/// </summary>
/// <param name="FactType">The fact, as the catalogue names it.</param>
/// <param name="Description">What the entries are for.</param>
/// <param name="Source">Sales, Purchases, Cash, Inventory or Manual.</param>
/// <param name="Lines">Where each amount lands.</param>
public sealed record DefinePostingRuleCommand(
    string FactType,
    string Description,
    string Source,
    IReadOnlyList<PostingRuleLineInput> Lines) : ICommand<Guid>;

/// <summary>Checks the shape of a <see cref="DefinePostingRuleCommand"/>.</summary>
public sealed class DefinePostingRuleCommandValidator : IValidator<DefinePostingRuleCommand>
{
    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(
        DefinePostingRuleCommand instance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var failures = new List<ValidationFailure>();

        if (!PostingFacts.IsKnown(instance.FactType))
        {
            failures.Add(new ValidationFailure(
                nameof(instance.FactType), "unknown_fact",
                "That is not a fact this system raises. The list is in PostingFacts."));
        }

        if (!Enum.TryParse(instance.Source, ignoreCase: true, out JournalSource source)
            || source == JournalSource.Unknown)
        {
            failures.Add(new ValidationFailure(
                nameof(instance.Source), "unknown_source",
                "An entry comes from Sales, Purchases, Cash, Inventory or a person (Manual)."));
        }

        if (instance.Lines is null || instance.Lines.Count < 2)
        {
            failures.Add(new ValidationFailure(
                nameof(instance.Lines), "too_few",
                "A mapping needs at least two lines: something has to balance against something."));

            return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
        }

        foreach (PostingRuleLineInput line in instance.Lines)
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

/// <summary>Writes the mapping.</summary>
public sealed class DefinePostingRuleCommandHandler
    : ICommandHandler<DefinePostingRuleCommand, Guid>
{
    private readonly IPostingRuleRepository _rules;
    private readonly IFinanceUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public DefinePostingRuleCommandHandler(
        IPostingRuleRepository rules,
        IFinanceUnitOfWork unitOfWork)
    {
        _rules = rules;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result<Guid>> HandleAsync(
        DefinePostingRuleCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await _rules.IsMappedAsync(request.FactType, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<Guid>(FinanceErrors.Posting.AlreadyMapped);
        }

        Result<PostingRule> rule = PostingRule.Define(
            request.FactType,
            request.Description,
            Enum.Parse<JournalSource>(request.Source, ignoreCase: true));

        if (rule.IsFailure)
        {
            return Result.Failure<Guid>(rule.Error);
        }

        foreach (PostingRuleLineInput line in request.Lines)
        {
            Result<PostingRuleLineId> added = rule.Value.AddLine(
                line.AmountKey,
                Enum.Parse<EntrySide>(line.Side, ignoreCase: true),
                line.AccountCode,
                line.Narrative);

            if (added.IsFailure)
            {
                return Result.Failure<Guid>(added.Error);
            }
        }

        _rules.Add(rule.Value);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return rule.Value.Id.Value;
    }
}

/// <summary>
/// Replaces a mapping's lines and description.
/// <para>
/// The lines are replaced wholesale rather than patched one at a time, because a mapping is only
/// ever meaningful whole. Removing the credit line and adding the new one as two requests leaves a
/// window in which the rule posts one side of every fact that arrives.
/// </para>
/// </summary>
/// <param name="PostingRuleId">The rule.</param>
/// <param name="Description">What the entries are for.</param>
/// <param name="Lines">Where each amount lands, from now on.</param>
/// <param name="IsActive">False to take the rule out of use.</param>
public sealed record AmendPostingRuleCommand(
    Guid PostingRuleId,
    string Description,
    IReadOnlyList<PostingRuleLineInput> Lines,
    bool IsActive = true) : ICommand;

/// <summary>Amends the mapping.</summary>
public sealed class AmendPostingRuleCommandHandler : ICommandHandler<AmendPostingRuleCommand>
{
    private readonly IPostingRuleRepository _rules;
    private readonly IFinanceUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public AmendPostingRuleCommandHandler(
        IPostingRuleRepository rules,
        IFinanceUnitOfWork unitOfWork)
    {
        _rules = rules;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        AmendPostingRuleCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        PostingRule? rule = await _rules
            .GetByIdAsync(new PostingRuleId(request.PostingRuleId), cancellationToken)
            .ConfigureAwait(false);

        if (rule is null)
        {
            return FinanceErrors.Posting.NotFound(request.PostingRuleId.ToString());
        }

        Result described = rule.Describe(request.Description);

        if (described.IsFailure)
        {
            return described;
        }

        // Taken out and put back in one transaction. The identifiers change, which is correct:
        // these are different lines, and the entries already posted carry their own copies of
        // what the old ones said.
        foreach (PostingRuleLineId lineId in rule.Lines.Select(line => line.Id).ToList())
        {
            Result removed = rule.RemoveLine(lineId);

            if (removed.IsFailure)
            {
                return removed;
            }
        }

        foreach (PostingRuleLineInput line in request.Lines)
        {
            if (!Enum.TryParse(line.Side, ignoreCase: true, out EntrySide side)
                || side == EntrySide.Unknown)
            {
                return FinanceErrors.Journal.SideRequired;
            }

            Result<PostingRuleLineId> added = rule.AddLine(
                line.AmountKey, side, line.AccountCode, line.Narrative);

            if (added.IsFailure)
            {
                return added.Error;
            }
        }

        if (request.IsActive)
        {
            rule.Activate();
        }
        else
        {
            rule.Deactivate();
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>Everything that is mapped, and what each one lands on.</summary>
public sealed record ListPostingRulesQuery : IQuery<IReadOnlyList<PostingRuleView>>;

/// <summary>A mapping, as a screen shows it.</summary>
/// <param name="PostingRuleId">The rule.</param>
/// <param name="FactType">The fact it maps.</param>
/// <param name="Description">What the entries are for.</param>
/// <param name="Source">Which journal they belong to.</param>
/// <param name="IsActive">Whether it is in use.</param>
/// <param name="Lines">Where each amount lands.</param>
public sealed record PostingRuleView(
    Guid PostingRuleId,
    string FactType,
    string Description,
    string Source,
    bool IsActive,
    IReadOnlyList<PostingRuleLineView> Lines);

/// <summary>One line of a mapping, as a screen shows it.</summary>
/// <param name="AmountKey">Which amount.</param>
/// <param name="Side">Which side.</param>
/// <param name="AccountCode">Which account.</param>
/// <param name="Narrative">What the line says.</param>
public sealed record PostingRuleLineView(
    string AmountKey,
    string Side,
    string AccountCode,
    string? Narrative);

/// <summary>Lists the mappings.</summary>
public sealed class ListPostingRulesQueryHandler
    : IQueryHandler<ListPostingRulesQuery, IReadOnlyList<PostingRuleView>>
{
    private readonly IPostingRuleRepository _rules;

    /// <summary>Initializes the handler.</summary>
    public ListPostingRulesQueryHandler(IPostingRuleRepository rules)
    {
        _rules = rules;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<PostingRuleView>>> HandleAsync(
        ListPostingRulesQuery request,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PostingRule> rules = await _rules
            .GetAllAsync(cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<PostingRuleView> views =
        [
            .. rules.Select(rule => new PostingRuleView(
                rule.Id.Value,
                rule.FactType,
                rule.Description,
                rule.Source.ToString(),
                rule.IsActive,
                [
                    .. rule.Lines.Select(line => new PostingRuleLineView(
                        line.AmountKey,
                        line.Side.ToString(),
                        line.AccountCode,
                        line.Narrative)),
                ])),
        ];

        return Result.Success(views);
    }
}

/// <summary>
/// Every fact that can be mapped, and what each one carries.
/// <para>
/// What the configuration screen is built from: the accountant picks a fact from this list and
/// says where its amounts land, rather than typing a key and finding out later that nothing
/// raises it.
/// </para>
/// </summary>
public sealed record ListPostableFactsQuery : IQuery<IReadOnlyList<PostableFactView>>;

/// <summary>A fact that can be mapped.</summary>
/// <param name="FactType">Its key.</param>
/// <param name="AmountKeys">The amounts it carries.</param>
/// <param name="IsMapped">Whether a rule already exists for it.</param>
public sealed record PostableFactView(
    string FactType,
    IReadOnlyList<string> AmountKeys,
    bool IsMapped);

/// <summary>Lists the facts.</summary>
public sealed class ListPostableFactsQueryHandler
    : IQueryHandler<ListPostableFactsQuery, IReadOnlyList<PostableFactView>>
{
    private readonly IPostingRuleRepository _rules;

    /// <summary>Initializes the handler.</summary>
    public ListPostableFactsQueryHandler(IPostingRuleRepository rules)
    {
        _rules = rules;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<PostableFactView>>> HandleAsync(
        ListPostableFactsQuery request,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PostingRule> rules = await _rules
            .GetAllAsync(cancellationToken)
            .ConfigureAwait(false);

        HashSet<string> mapped = [.. rules.Select(rule => rule.FactType)];

        IReadOnlyList<PostableFactView> views =
        [
            .. PostingFacts.All.Select(fact => new PostableFactView(
                fact, PostingFacts.AmountKeysFor(fact), mapped.Contains(fact))),
        ];

        return Result.Success(views);
    }
}
