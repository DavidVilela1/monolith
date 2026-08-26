using AutoPartsErp.Modules.Catalog.Domain;
using AutoPartsErp.Modules.Catalog.Domain.Parts;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Catalog.Application.Parts.Commands;

/// <summary>Links a foreign number, usually an OEM number, to a part.</summary>
/// <param name="PartId">The part.</param>
/// <param name="Kind">Why the numbers are linked: Oem, Competitor, Supersedes, Interchange, TradingPartner.</param>
/// <param name="Number">The foreign number, as printed.</param>
/// <param name="SourceBrand">Whose number it is, when known.</param>
/// <param name="Notes">Optional qualifier, for example "up to 05/2012".</param>
public sealed record AddCrossReferenceCommand(
    Guid PartId,
    string Kind,
    string Number,
    string? SourceBrand = null,
    string? Notes = null) : ICommand;

/// <summary>Checks the shape of an <see cref="AddCrossReferenceCommand"/>.</summary>
public sealed class AddCrossReferenceCommandValidator : IValidator<AddCrossReferenceCommand>
{
    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(
        AddCrossReferenceCommand instance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var failures = new List<ValidationFailure>();

        if (string.IsNullOrWhiteSpace(instance.Number))
        {
            failures.Add(new ValidationFailure(
                nameof(instance.Number), "required", "A cross-reference number is required."));
        }

        if (!Enum.TryParse(instance.Kind, ignoreCase: true, out CrossReferenceKind kind) ||
            kind == CrossReferenceKind.Unknown)
        {
            failures.Add(new ValidationFailure(
                nameof(instance.Kind),
                "unknown_kind",
                "Kind must be one of: Oem, Competitor, Supersedes, Interchange, TradingPartner."));
        }

        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}

/// <summary>Adds the cross-reference to the part.</summary>
public sealed class AddCrossReferenceCommandHandler : ICommandHandler<AddCrossReferenceCommand>
{
    private readonly IPartRepository _parts;
    private readonly ICatalogUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public AddCrossReferenceCommandHandler(IPartRepository parts, ICatalogUnitOfWork unitOfWork)
    {
        _parts = parts;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        AddCrossReferenceCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Part? part = await _parts.GetByIdAsync(new PartId(request.PartId), cancellationToken)
            .ConfigureAwait(false);

        if (part is null)
        {
            return CatalogErrors.Part.NotFound(request.PartId.ToString());
        }

        var kind = Enum.Parse<CrossReferenceKind>(request.Kind, ignoreCase: true);

        Result<CrossReference> crossReference =
            CrossReference.Create(kind, request.Number, request.SourceBrand, request.Notes);

        if (crossReference.IsFailure)
        {
            return Result.Failure(crossReference.Error);
        }

        Result added = part.AddCrossReference(crossReference.Value);
        if (added.IsFailure)
        {
            return added;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}

/// <summary>Records that a part fits a particular vehicle.</summary>
/// <param name="PartId">The part.</param>
/// <param name="Make">Vehicle manufacturer.</param>
/// <param name="Model">Model designation.</param>
/// <param name="YearFrom">First model year covered, inclusive.</param>
/// <param name="YearTo">Last model year covered, inclusive.</param>
/// <param name="EngineCode">Optional engine or type code.</param>
/// <param name="Position">Optional fitting position, such as FRONT.</param>
/// <param name="Notes">Optional qualifier.</param>
public sealed record AddFitmentCommand(
    Guid PartId,
    string Make,
    string Model,
    int YearFrom,
    int YearTo,
    string? EngineCode = null,
    string? Position = null,
    string? Notes = null) : ICommand;

/// <summary>Checks the shape of an <see cref="AddFitmentCommand"/>.</summary>
public sealed class AddFitmentCommandValidator : IValidator<AddFitmentCommand>
{
    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(
        AddFitmentCommand instance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var failures = new List<ValidationFailure>();

        if (string.IsNullOrWhiteSpace(instance.Make))
        {
            failures.Add(new ValidationFailure(nameof(instance.Make), "required", "A vehicle make is required."));
        }

        if (string.IsNullOrWhiteSpace(instance.Model))
        {
            failures.Add(new ValidationFailure(nameof(instance.Model), "required", "A vehicle model is required."));
        }

        if (instance.YearTo < instance.YearFrom)
        {
            failures.Add(new ValidationFailure(
                nameof(instance.YearTo),
                "range_inverted",
                "The last model year cannot be earlier than the first."));
        }

        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}

/// <summary>Adds the vehicle application to the part.</summary>
public sealed class AddFitmentCommandHandler : ICommandHandler<AddFitmentCommand>
{
    private readonly IPartRepository _parts;
    private readonly ICatalogUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public AddFitmentCommandHandler(IPartRepository parts, ICatalogUnitOfWork unitOfWork)
    {
        _parts = parts;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        AddFitmentCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Part? part = await _parts.GetByIdAsync(new PartId(request.PartId), cancellationToken)
            .ConfigureAwait(false);

        if (part is null)
        {
            return CatalogErrors.Part.NotFound(request.PartId.ToString());
        }

        Result<Fitment> fitment = Fitment.Create(
            request.Make,
            request.Model,
            request.YearFrom,
            request.YearTo,
            request.EngineCode,
            request.Position,
            request.Notes);

        if (fitment.IsFailure)
        {
            return Result.Failure(fitment.Error);
        }

        Result added = part.AddFitment(fitment.Value);
        if (added.IsFailure)
        {
            return added;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
