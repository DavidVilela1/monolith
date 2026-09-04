using AutoPartsErp.ModuleContracts.Catalog;
using AutoPartsErp.Modules.Pricing.Domain;
using AutoPartsErp.Modules.Pricing.Domain.PriceLists;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Pricing.Application.PriceLists.Commands;

/// <summary>
/// Sets what a part costs in a list, from a quantity upwards.
/// <para>
/// One command for both "price this part" and "add a break to it". The caller has an intention —
/// "ten or more is €22" — and should not have to know whether the part is already in the list.
/// </para>
/// </summary>
/// <param name="PriceListId">The list.</param>
/// <param name="PartId">The part.</param>
/// <param name="MinimumQuantity">The quantity the price applies from. Usually 1.</param>
/// <param name="UnitPrice">What one unit costs from there upwards, in the list's currency.</param>
public sealed record SetPartPriceCommand(
    Guid PriceListId,
    Guid PartId,
    decimal MinimumQuantity,
    decimal UnitPrice) : ICommand<Guid>;

/// <summary>Checks the shape of a <see cref="SetPartPriceCommand"/>.</summary>
public sealed class SetPartPriceCommandValidator : IValidator<SetPartPriceCommand>
{
    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(
        SetPartPriceCommand instance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var failures = new List<ValidationFailure>();

        if (instance.PriceListId == Guid.Empty)
        {
            failures.Add(new ValidationFailure(
                nameof(instance.PriceListId), "required", "A price list is required."));
        }

        if (instance.PartId == Guid.Empty)
        {
            failures.Add(new ValidationFailure(
                nameof(instance.PartId), "required", "A part is required."));
        }

        if (instance.MinimumQuantity <= 0m)
        {
            failures.Add(new ValidationFailure(
                nameof(instance.MinimumQuantity), "not_positive",
                "A quantity break has to start at a quantity above zero."));
        }

        if (instance.UnitPrice < 0m)
        {
            failures.Add(new ValidationFailure(
                nameof(instance.UnitPrice), "negative", "A price cannot be negative."));
        }

        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}

/// <summary>Sets the price.</summary>
public sealed class SetPartPriceCommandHandler : ICommandHandler<SetPartPriceCommand, Guid>
{
    private readonly IPriceListRepository _lists;
    private readonly IPriceListEntryRepository _entries;
    private readonly ICatalogDirectory _catalogue;
    private readonly IPricingUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public SetPartPriceCommandHandler(
        IPriceListRepository lists,
        IPriceListEntryRepository entries,
        ICatalogDirectory catalogue,
        IPricingUnitOfWork unitOfWork)
    {
        _lists = lists;
        _entries = entries;
        _catalogue = catalogue;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result<Guid>> HandleAsync(
        SetPartPriceCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var listId = new PriceListId(request.PriceListId);
        var partId = new PartRef(request.PartId);

        PriceList? list = await _lists.GetByIdAsync(listId, cancellationToken).ConfigureAwait(false);

        if (list is null)
        {
            return Result.Failure<Guid>(PricingErrors.List.NotFound(request.PriceListId.ToString()));
        }

        if (!list.IsEditable)
        {
            return Result.Failure<Guid>(PricingErrors.List.Archived);
        }

        // A price list full of parts the catalogue has never heard of is a price list nobody can
        // sell from, and the entries would sit there forever because nothing ever looks at them.
        // Sellability is deliberately NOT checked: pricing a part before it goes live is exactly
        // how you get a list ready for the day it does.
        PartDescriptor? part = await _catalogue
            .GetAsync(request.PartId, cancellationToken)
            .ConfigureAwait(false);

        if (part is null)
        {
            return Result.Failure<Guid>(PricingErrors.Entry.NotFound(request.PartId.ToString()));
        }

        Money price = Money.Of(request.UnitPrice, list.Currency);

        PriceListEntry? entry = await _entries
            .GetForAsync(listId, partId, cancellationToken)
            .ConfigureAwait(false);

        if (entry is null)
        {
            Result<PriceListEntry> created = PriceListEntry.Price(
                listId, partId, request.MinimumQuantity, price);

            if (created.IsFailure)
            {
                return Result.Failure<Guid>(created.Error);
            }

            _entries.Add(created.Value);
            await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return created.Value.Id.Value;
        }

        Result set = entry.SetBreak(request.MinimumQuantity, price);

        if (set.IsFailure)
        {
            return Result.Failure<Guid>(set.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return entry.Id.Value;
    }
}

/// <summary>Removes one quantity break from a price.</summary>
/// <param name="PriceListId">The list.</param>
/// <param name="PartId">The part.</param>
/// <param name="MinimumQuantity">The break to remove.</param>
public sealed record RemovePriceBreakCommand(
    Guid PriceListId,
    Guid PartId,
    decimal MinimumQuantity) : ICommand;

/// <summary>Removes the break.</summary>
public sealed class RemovePriceBreakCommandHandler : ICommandHandler<RemovePriceBreakCommand>
{
    private readonly IPriceListRepository _lists;
    private readonly IPriceListEntryRepository _entries;
    private readonly IPricingUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public RemovePriceBreakCommandHandler(
        IPriceListRepository lists,
        IPriceListEntryRepository entries,
        IPricingUnitOfWork unitOfWork)
    {
        _lists = lists;
        _entries = entries;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        RemovePriceBreakCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var listId = new PriceListId(request.PriceListId);

        PriceList? list = await _lists.GetByIdAsync(listId, cancellationToken).ConfigureAwait(false);

        if (list is null)
        {
            return PricingErrors.List.NotFound(request.PriceListId.ToString());
        }

        if (!list.IsEditable)
        {
            return PricingErrors.List.Archived;
        }

        PriceListEntry? entry = await _entries
            .GetForAsync(listId, new PartRef(request.PartId), cancellationToken)
            .ConfigureAwait(false);

        if (entry is null)
        {
            return PricingErrors.Entry.NotFound(request.PartId.ToString());
        }

        Result removed = entry.RemoveBreak(request.MinimumQuantity);

        if (removed.IsFailure)
        {
            return removed;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>Takes a part out of a list entirely.</summary>
/// <param name="PriceListId">The list.</param>
/// <param name="PartId">The part.</param>
public sealed record RemovePartPriceCommand(Guid PriceListId, Guid PartId) : ICommand;

/// <summary>Removes the price.</summary>
public sealed class RemovePartPriceCommandHandler : ICommandHandler<RemovePartPriceCommand>
{
    private readonly IPriceListRepository _lists;
    private readonly IPriceListEntryRepository _entries;
    private readonly IPricingUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public RemovePartPriceCommandHandler(
        IPriceListRepository lists,
        IPriceListEntryRepository entries,
        IPricingUnitOfWork unitOfWork)
    {
        _lists = lists;
        _entries = entries;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        RemovePartPriceCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var listId = new PriceListId(request.PriceListId);

        PriceList? list = await _lists.GetByIdAsync(listId, cancellationToken).ConfigureAwait(false);

        if (list is null)
        {
            return PricingErrors.List.NotFound(request.PriceListId.ToString());
        }

        if (!list.IsEditable)
        {
            return PricingErrors.List.Archived;
        }

        PriceListEntry? entry = await _entries
            .GetForAsync(listId, new PartRef(request.PartId), cancellationToken)
            .ConfigureAwait(false);

        if (entry is null)
        {
            return PricingErrors.Entry.NotFound(request.PartId.ToString());
        }

        // Soft delete, like everything else here: an order raised last March quoted this price and
        // the row is how that document still explains itself.
        _entries.Remove(entry);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
