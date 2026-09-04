using AutoPartsErp.Modules.Pricing.Domain;
using AutoPartsErp.Modules.Pricing.Domain.PriceLists;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Pricing.Application.PriceLists.Commands;

/// <summary>Opens a price list, in draft.</summary>
/// <param name="Code">The code it will be referred to by.</param>
/// <param name="Name">What it is called.</param>
/// <param name="CurrencyCode">The currency every price in it is expressed in.</param>
/// <param name="Kind">Standard, Customer or Promotion.</param>
/// <param name="EffectiveFrom">The first day it applies, or null for always.</param>
/// <param name="EffectiveTo">The last day it applies, or null for never expiring.</param>
public sealed record OpenPriceListCommand(
    string Code,
    string Name,
    string CurrencyCode,
    string Kind,
    DateOnly? EffectiveFrom = null,
    DateOnly? EffectiveTo = null) : ICommand<Guid>;

/// <summary>Checks the shape of an <see cref="OpenPriceListCommand"/>.</summary>
public sealed class OpenPriceListCommandValidator : IValidator<OpenPriceListCommand>
{
    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(
        OpenPriceListCommand instance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var failures = new List<ValidationFailure>();

        if (string.IsNullOrWhiteSpace(instance.Code))
        {
            failures.Add(new ValidationFailure(
                nameof(instance.Code), "required", "A price list code is required."));
        }

        if (string.IsNullOrWhiteSpace(instance.Name))
        {
            failures.Add(new ValidationFailure(
                nameof(instance.Name), "required", "A price list name is required."));
        }

        if (!Currency.TryFromCode(instance.CurrencyCode, out _))
        {
            failures.Add(new ValidationFailure(
                nameof(instance.CurrencyCode), "unknown_currency",
                $"'{instance.CurrencyCode}' is not a supported currency."));
        }

        if (!Enum.TryParse(instance.Kind, ignoreCase: true, out PriceListKind kind)
            || kind == PriceListKind.Unknown)
        {
            failures.Add(new ValidationFailure(
                nameof(instance.Kind), "unknown_kind",
                "A price list is Standard, Customer or Promotion."));
        }

        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}

/// <summary>Opens the list.</summary>
public sealed class OpenPriceListCommandHandler : ICommandHandler<OpenPriceListCommand, Guid>
{
    private readonly IPriceListRepository _lists;
    private readonly IPricingUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public OpenPriceListCommandHandler(IPriceListRepository lists, IPricingUnitOfWork unitOfWork)
    {
        _lists = lists;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result<Guid>> HandleAsync(
        OpenPriceListCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string code = request.Code.Trim().ToUpperInvariant();

        // Checked here rather than left to the unique index, so a duplicate comes back as a 409
        // with something to read rather than a 500 with a constraint name in it. The index is
        // still there and still authoritative under a race.
        if (await _lists.CodeExistsAsync(code, null, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<Guid>(PricingErrors.List.CodeExists);
        }

        Result<PriceList> list = PriceList.Open(
            request.Code,
            request.Name,
            Currency.FromCode(request.CurrencyCode),
            Enum.Parse<PriceListKind>(request.Kind, ignoreCase: true),
            request.EffectiveFrom,
            request.EffectiveTo);

        if (list.IsFailure)
        {
            return Result.Failure<Guid>(list.Error);
        }

        _lists.Add(list.Value);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return list.Value.Id.Value;
    }
}

/// <summary>Renames a list, or moves the period it applies over.</summary>
/// <param name="PriceListId">The list.</param>
/// <param name="Name">The new name.</param>
/// <param name="EffectiveFrom">The new first day, or null for always.</param>
/// <param name="EffectiveTo">The new last day, or null for never expiring.</param>
public sealed record AmendPriceListCommand(
    Guid PriceListId,
    string Name,
    DateOnly? EffectiveFrom = null,
    DateOnly? EffectiveTo = null) : ICommand;

/// <summary>Amends the list.</summary>
public sealed class AmendPriceListCommandHandler : ICommandHandler<AmendPriceListCommand>
{
    private readonly IPriceListRepository _lists;
    private readonly IPricingUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public AmendPriceListCommandHandler(IPriceListRepository lists, IPricingUnitOfWork unitOfWork)
    {
        _lists = lists;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        AmendPriceListCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        PriceList? list = await _lists
            .GetByIdAsync(new PriceListId(request.PriceListId), cancellationToken)
            .ConfigureAwait(false);

        if (list is null)
        {
            return PricingErrors.List.NotFound(request.PriceListId.ToString());
        }

        Result amended = list.Amend(request.Name, request.EffectiveFrom, request.EffectiveTo);

        if (amended.IsFailure)
        {
            return amended;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>Puts a list into service, so quotes start coming from it.</summary>
/// <param name="PriceListId">The list.</param>
public sealed record ActivatePriceListCommand(Guid PriceListId) : ICommand;

/// <summary>Activates the list.</summary>
public sealed class ActivatePriceListCommandHandler : ICommandHandler<ActivatePriceListCommand>
{
    private readonly IPriceListRepository _lists;
    private readonly IPriceListEntryRepository _entries;
    private readonly IPricingUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public ActivatePriceListCommandHandler(
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
        ActivatePriceListCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var id = new PriceListId(request.PriceListId);

        PriceList? list = await _lists.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        if (list is null)
        {
            return PricingErrors.List.NotFound(request.PriceListId.ToString());
        }

        // The entries are not part of this aggregate, so "does it price anything?" is a question
        // the handler has to answer and hand in. See the note on PriceList.
        bool hasAnyPrice = await _entries.AnyInAsync(id, cancellationToken).ConfigureAwait(false);

        Result activated = list.Activate(hasAnyPrice);

        if (activated.IsFailure)
        {
            return activated;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>Withdraws a list. Quotes stop coming from it; old documents still explain themselves.</summary>
/// <param name="PriceListId">The list.</param>
public sealed record ArchivePriceListCommand(Guid PriceListId) : ICommand;

/// <summary>Archives the list.</summary>
public sealed class ArchivePriceListCommandHandler : ICommandHandler<ArchivePriceListCommand>
{
    private readonly IPriceListRepository _lists;
    private readonly IPricingUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public ArchivePriceListCommandHandler(IPriceListRepository lists, IPricingUnitOfWork unitOfWork)
    {
        _lists = lists;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        ArchivePriceListCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        PriceList? list = await _lists
            .GetByIdAsync(new PriceListId(request.PriceListId), cancellationToken)
            .ConfigureAwait(false);

        if (list is null)
        {
            return PricingErrors.List.NotFound(request.PriceListId.ToString());
        }

        Result archived = list.Archive();

        if (archived.IsFailure)
        {
            return archived;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>
/// Makes a list the one customers with no agreement fall back to.
/// <para>
/// Moves the flag: the list that had it loses it in the same transaction. "Exactly one" is a
/// statement about every row in the table, so it cannot live on an aggregate — but it can live
/// here, in the one place that changes it.
/// </para>
/// </summary>
/// <param name="PriceListId">The list.</param>
public sealed record MakeDefaultPriceListCommand(Guid PriceListId) : ICommand;

/// <summary>Moves the default flag.</summary>
public sealed class MakeDefaultPriceListCommandHandler : ICommandHandler<MakeDefaultPriceListCommand>
{
    private readonly IPriceListRepository _lists;
    private readonly IPricingUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public MakeDefaultPriceListCommandHandler(
        IPriceListRepository lists,
        IPricingUnitOfWork unitOfWork)
    {
        _lists = lists;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        MakeDefaultPriceListCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var id = new PriceListId(request.PriceListId);

        PriceList? list = await _lists.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        if (list is null)
        {
            return PricingErrors.List.NotFound(request.PriceListId.ToString());
        }

        // The incoming list is checked first. Clearing the old default and then discovering the
        // new one is a draft would leave the tenant with no default at all, which is the one
        // state the resolver cannot recover from.
        Result promoted = list.MakeDefault();

        if (promoted.IsFailure)
        {
            return promoted;
        }

        PriceList? previous = await _lists.GetDefaultAsync(cancellationToken).ConfigureAwait(false);

        if (previous is not null && previous.Id != id)
        {
            previous.ClearDefault();
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
