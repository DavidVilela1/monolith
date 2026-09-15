using AutoPartsErp.Modules.Finance.Domain;
using AutoPartsErp.Modules.Finance.Domain.Ledger;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Finance.Application.Ledger.Commands;

/// <summary>
/// Tries again to post every fact that is waiting.
/// <para>
/// What somebody presses after filling in the rules. Each fact is attempted on its own and a
/// failure stops nothing: a backlog of two hundred where three are still unmapped should post the
/// hundred and ninety-seven.
/// </para>
/// </summary>
public sealed record PostWaitingFactsCommand : ICommand<PostWaitingFactsResult>;

/// <summary>What a run of the waiting list did.</summary>
/// <param name="Attempted">How many were tried.</param>
/// <param name="Posted">How many reached the ledger.</param>
/// <param name="StillWaiting">How many are still waiting, and why is on each one.</param>
public sealed record PostWaitingFactsResult(int Attempted, int Posted, int StillWaiting);

/// <summary>Runs the waiting list.</summary>
public sealed class PostWaitingFactsCommandHandler
    : ICommandHandler<PostWaitingFactsCommand, PostWaitingFactsResult>
{
    private readonly IFactPostingRepository _facts;
    private readonly ILedgerPoster _poster;
    private readonly IFinanceUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public PostWaitingFactsCommandHandler(
        IFactPostingRepository facts,
        ILedgerPoster poster,
        IFinanceUnitOfWork unitOfWork)
    {
        _facts = facts;
        _poster = poster;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result<PostWaitingFactsResult>> HandleAsync(
        PostWaitingFactsCommand request,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<FactPosting> waiting = await _facts
            .GetWaitingAsync(cancellationToken)
            .ConfigureAwait(false);

        int posted = 0;

        foreach (FactPosting fact in waiting)
        {
            Result attempt = await _poster
                .TryPostAsync(fact, cancellationToken)
                .ConfigureAwait(false);

            if (attempt.IsSuccess)
            {
                posted++;
            }
        }

        // One save for the whole run. Either the ledger gains all of these entries or it gains
        // none, and a run that failed halfway would otherwise leave a backlog nobody can tell
        // apart from one that was never run.
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new PostWaitingFactsResult(waiting.Count, posted, waiting.Count - posted);
    }
}

/// <summary>Takes one fact off the waiting list without posting it.</summary>
/// <param name="FactPostingId">The recorded fact.</param>
/// <param name="Reason">Why it does not belong in the ledger.</param>
public sealed record DismissFactPostingCommand(Guid FactPostingId, string Reason) : ICommand;

/// <summary>Dismisses the fact.</summary>
public sealed class DismissFactPostingCommandHandler
    : ICommandHandler<DismissFactPostingCommand>
{
    private readonly IFactPostingRepository _facts;
    private readonly IFinanceUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public DismissFactPostingCommandHandler(
        IFactPostingRepository facts,
        IFinanceUnitOfWork unitOfWork)
    {
        _facts = facts;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        DismissFactPostingCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        FactPosting? fact = await _facts
            .GetByIdAsync(new FactPostingId(request.FactPostingId), cancellationToken)
            .ConfigureAwait(false);

        if (fact is null)
        {
            return FinanceErrors.Posting.FactNotFound(request.FactPostingId.ToString());
        }

        Result dismissed = fact.Dismiss(request.Reason);

        if (dismissed.IsFailure)
        {
            return dismissed;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>Puts a dismissed fact back on the waiting list.</summary>
/// <param name="FactPostingId">The recorded fact.</param>
public sealed record ReinstateFactPostingCommand(Guid FactPostingId) : ICommand;

/// <summary>Reinstates the fact.</summary>
public sealed class ReinstateFactPostingCommandHandler
    : ICommandHandler<ReinstateFactPostingCommand>
{
    private readonly IFactPostingRepository _facts;
    private readonly IFinanceUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public ReinstateFactPostingCommandHandler(
        IFactPostingRepository facts,
        IFinanceUnitOfWork unitOfWork)
    {
        _facts = facts;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        ReinstateFactPostingCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        FactPosting? fact = await _facts
            .GetByIdAsync(new FactPostingId(request.FactPostingId), cancellationToken)
            .ConfigureAwait(false);

        if (fact is null)
        {
            return FinanceErrors.Posting.FactNotFound(request.FactPostingId.ToString());
        }

        Result reinstated = fact.Reinstate();

        if (reinstated.IsFailure)
        {
            return reinstated;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>
/// Every fact still waiting to reach the ledger, and why each one is stuck.
/// <para>
/// The screen that stops the general ledger being quietly incomplete. An empty list means
/// everything that happened is in the books.
/// </para>
/// </summary>
public sealed record ListWaitingFactsQuery : IQuery<IReadOnlyList<WaitingFactView>>;

/// <summary>A fact waiting to be posted.</summary>
/// <param name="FactPostingId">The recorded fact.</param>
/// <param name="FactType">What happened.</param>
/// <param name="Reference">The document behind it.</param>
/// <param name="OccurredOn">The day it belongs to.</param>
/// <param name="Description">What it is.</param>
/// <param name="CurrencyCode">The currency.</param>
/// <param name="Amounts">What it carried, by key.</param>
/// <param name="Reason">Why it has not posted.</param>
public sealed record WaitingFactView(
    Guid FactPostingId,
    string FactType,
    string Reference,
    DateOnly OccurredOn,
    string Description,
    string CurrencyCode,
    IReadOnlyDictionary<string, decimal> Amounts,
    string? Reason);

/// <summary>Lists the waiting facts.</summary>
public sealed class ListWaitingFactsQueryHandler
    : IQueryHandler<ListWaitingFactsQuery, IReadOnlyList<WaitingFactView>>
{
    private readonly IFactPostingRepository _facts;

    /// <summary>Initializes the handler.</summary>
    public ListWaitingFactsQueryHandler(IFactPostingRepository facts)
    {
        _facts = facts;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<WaitingFactView>>> HandleAsync(
        ListWaitingFactsQuery request,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<FactPosting> waiting = await _facts
            .GetWaitingAsync(cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<WaitingFactView> views =
        [
            .. waiting.Select(fact => new WaitingFactView(
                fact.Id.Value,
                fact.FactType,
                fact.Reference,
                fact.OccurredOn,
                fact.Description,
                fact.CurrencyCode,
                fact.Amounts.ToDictionary(
                    amount => amount.Key,
                    amount => amount.Amount.Amount,
                    StringComparer.Ordinal),
                fact.Reason)),
        ];

        return Result.Success(views);
    }
}
