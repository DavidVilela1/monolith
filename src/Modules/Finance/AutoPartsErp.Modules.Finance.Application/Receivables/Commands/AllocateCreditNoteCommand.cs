using AutoPartsErp.Modules.Finance.Application.Receipts.Commands;
using AutoPartsErp.Modules.Finance.Domain;
using AutoPartsErp.Modules.Finance.Domain.Receivables;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Finance.Application.Receivables.Commands;

/// <summary>
/// Offsets a credit note against documents the customer owes.
/// <para>
/// The same operation as allocating a receipt, with no money in it. A credit note is worth
/// exactly what an equivalent payment would be worth, which is why the domain settles both
/// through one service and why the two commands read almost identically.
/// </para>
/// <para>
/// Separate from issuing the credit note, and deliberately: Invoicing raises the document, and
/// what it is set against is a decision somebody in the office makes afterwards. Often it is the
/// invoice it was drawn from; often enough it is the oldest thing outstanding instead.
/// </para>
/// </summary>
/// <param name="CreditNoteItemId">The credit note's item on the account.</param>
/// <param name="Allocations">What it offsets, and how much of each.</param>
public sealed record AllocateCreditNoteCommand(
    Guid CreditNoteItemId,
    IReadOnlyList<AllocationLine> Allocations) : ICommand;

/// <summary>Checks the shape of an <see cref="AllocateCreditNoteCommand"/>.</summary>
public sealed class AllocateCreditNoteCommandValidator : IValidator<AllocateCreditNoteCommand>
{
    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(
        AllocateCreditNoteCommand instance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var failures = new List<ValidationFailure>();

        if (instance.CreditNoteItemId == Guid.Empty)
        {
            failures.Add(new ValidationFailure(
                nameof(instance.CreditNoteItemId),
                "required",
                "Say which credit note is being allocated."));
        }

        if (instance.Allocations is null || instance.Allocations.Count == 0)
        {
            failures.Add(new ValidationFailure(
                nameof(instance.Allocations),
                "required",
                "Say which documents the credit offsets and how much of each."));
        }
        else if (instance.Allocations.Select(line => line.OpenItemId).Distinct().Count()
                 != instance.Allocations.Count)
        {
            failures.Add(new ValidationFailure(
                nameof(instance.Allocations),
                "duplicate_item",
                "The same document appears more than once. Give each one a single amount."));
        }

        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}

/// <summary>Offsets the credit.</summary>
public sealed class AllocateCreditNoteCommandHandler : ICommandHandler<AllocateCreditNoteCommand>
{
    private readonly IOpenItemRepository _items;
    private readonly IFinanceUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public AllocateCreditNoteCommandHandler(
        IOpenItemRepository items,
        IFinanceUnitOfWork unitOfWork)
    {
        _items = items;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        AllocateCreditNoteCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var creditId = new OpenItemId(request.CreditNoteItemId);

        OpenItem? credit = await _items
            .GetByIdAsync(creditId, cancellationToken)
            .ConfigureAwait(false);

        if (credit is null)
        {
            return FinanceErrors.OpenItem.NotFound(request.CreditNoteItemId.ToString());
        }

        // Loaded in one query with the credit excluded, so that a caller pointing the note at
        // itself gets the domain's message about it rather than a not-found on a row that is
        // sitting right there.
        List<OpenItemId> ids =
            [.. request.Allocations.Select(line => new OpenItemId(line.OpenItemId))];

        IReadOnlyList<OpenItem> items = await _items
            .GetManyAsync(ids, cancellationToken)
            .ConfigureAwait(false);

        Dictionary<OpenItemId, OpenItem> byId = items.ToDictionary(item => item.Id);

        // The credit itself may be among them, and the repository will have returned the same
        // tracked instance. Using it keeps the two sides of the match on one object, which is
        // what lets the domain notice.
        byId[creditId] = credit;

        var lines = new List<SettlementLine>(request.Allocations.Count);

        foreach (AllocationLine line in request.Allocations)
        {
            if (!byId.TryGetValue(new OpenItemId(line.OpenItemId), out OpenItem? item))
            {
                return FinanceErrors.OpenItem.NotFound(line.OpenItemId.ToString());
            }

            lines.Add(new SettlementLine(item, Money.Of(line.Amount, credit.Currency)));
        }

        Result applied = Settlement.ApplyCredit(credit, lines);

        if (applied.IsFailure)
        {
            return applied;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
