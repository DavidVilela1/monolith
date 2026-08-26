using AutoPartsErp.Modules.Catalog.Domain;
using AutoPartsErp.Modules.Catalog.Domain.Parts;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Catalog.Application.Parts.Commands;

/// <summary>Makes a draft part orderable and sellable.</summary>
/// <param name="PartId">The part to activate.</param>
public sealed record ActivatePartCommand(Guid PartId) : ICommand;

/// <summary>Activates the part, or reports why it is not ready.</summary>
public sealed class ActivatePartCommandHandler : ICommandHandler<ActivatePartCommand>
{
    private readonly IPartRepository _parts;
    private readonly ICatalogUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public ActivatePartCommandHandler(IPartRepository parts, ICatalogUnitOfWork unitOfWork)
    {
        _parts = parts;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        ActivatePartCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Part? part = await _parts.GetByIdAsync(new PartId(request.PartId), cancellationToken)
            .ConfigureAwait(false);

        if (part is null)
        {
            return CatalogErrors.Part.NotFound(request.PartId.ToString());
        }

        Result activation = part.Activate();
        if (activation.IsFailure)
        {
            return activation;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}

/// <summary>Withdraws a part from purchasing while remaining stock is sold down.</summary>
/// <param name="PartId">The part to discontinue.</param>
/// <param name="SupersededByPartId">The replacement part, when the brand has named one.</param>
public sealed record DiscontinuePartCommand(Guid PartId, Guid? SupersededByPartId = null) : ICommand;

/// <summary>Discontinues the part and records its replacement.</summary>
public sealed class DiscontinuePartCommandHandler : ICommandHandler<DiscontinuePartCommand>
{
    private readonly IPartRepository _parts;
    private readonly ICatalogUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public DiscontinuePartCommandHandler(IPartRepository parts, ICatalogUnitOfWork unitOfWork)
    {
        _parts = parts;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        DiscontinuePartCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Part? part = await _parts.GetByIdAsync(new PartId(request.PartId), cancellationToken)
            .ConfigureAwait(false);

        if (part is null)
        {
            return CatalogErrors.Part.NotFound(request.PartId.ToString());
        }

        PartId? replacement = null;

        if (request.SupersededByPartId is { } supersededBy)
        {
            var replacementId = new PartId(supersededBy);

            if (!await _parts.ExistsAsync(replacementId, cancellationToken).ConfigureAwait(false))
            {
                return CatalogErrors.Part.NotFound(supersededBy.ToString());
            }

            replacement = replacementId;
        }

        Result discontinue = part.Discontinue(replacement);
        if (discontinue.IsFailure)
        {
            return discontinue;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}

/// <summary>Records the deposit charged on a part sold against a returnable core.</summary>
/// <param name="PartId">The part.</param>
/// <param name="Amount">The refundable deposit. Must be greater than zero.</param>
/// <param name="CurrencyCode">ISO currency code of the deposit.</param>
public sealed record SetCoreChargeCommand(Guid PartId, decimal Amount, string CurrencyCode) : ICommand;

/// <summary>Checks the shape of a <see cref="SetCoreChargeCommand"/>.</summary>
public sealed class SetCoreChargeCommandValidator : IValidator<SetCoreChargeCommand>
{
    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(
        SetCoreChargeCommand instance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var failures = new List<ValidationFailure>();

        if (instance.Amount <= 0m)
        {
            failures.Add(new ValidationFailure(
                nameof(instance.Amount), "must_be_positive", "A core charge must be greater than zero."));
        }

        if (!Currency.TryFromCode(instance.CurrencyCode, out _))
        {
            failures.Add(new ValidationFailure(
                nameof(instance.CurrencyCode),
                "unknown_currency",
                $"'{instance.CurrencyCode}' is not a supported currency."));
        }

        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}

/// <summary>Sets the core charge.</summary>
public sealed class SetCoreChargeCommandHandler : ICommandHandler<SetCoreChargeCommand>
{
    private readonly IPartRepository _parts;
    private readonly ICatalogUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public SetCoreChargeCommandHandler(IPartRepository parts, ICatalogUnitOfWork unitOfWork)
    {
        _parts = parts;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        SetCoreChargeCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Part? part = await _parts.GetByIdAsync(new PartId(request.PartId), cancellationToken)
            .ConfigureAwait(false);

        if (part is null)
        {
            return CatalogErrors.Part.NotFound(request.PartId.ToString());
        }

        Money charge = Money.Of(request.Amount, Currency.FromCode(request.CurrencyCode));

        Result applied = part.RequireCoreReturn(charge);
        if (applied.IsFailure)
        {
            return applied;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
