using AutoPartsErp.Modules.Invoicing.Domain;
using AutoPartsErp.Modules.Invoicing.Domain.Series;
using AutoPartsErp.SharedKernel.Abstractions;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Invoicing.Application.Series.Commands;

/// <summary>Opens a document series, before it has been declared to the tax authority.</summary>
/// <param name="Type">FT, FS, FR, NC or ND.</param>
/// <param name="Code">The series identifier, e.g. <c>SERIE2026</c>.</param>
/// <param name="Year">The year it belongs to.</param>
public sealed record OpenDocumentSeriesCommand(string Type, string Code, int Year) : ICommand<Guid>;

/// <summary>Checks the shape of an <see cref="OpenDocumentSeriesCommand"/>.</summary>
public sealed class OpenDocumentSeriesCommandValidator : IValidator<OpenDocumentSeriesCommand>
{
    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(
        OpenDocumentSeriesCommand instance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var failures = new List<ValidationFailure>();

        if (!DocumentTypeCodes.TryFromCode(instance.Type, out _))
        {
            failures.Add(new ValidationFailure(
                nameof(instance.Type), "unknown_type",
                $"'{instance.Type}' is not a document type this module issues. Use FT, FS, FR, NC or ND."));
        }

        if (string.IsNullOrWhiteSpace(instance.Code))
        {
            failures.Add(new ValidationFailure(
                nameof(instance.Code), "required", "A series code is required."));
        }

        if (instance.Year is < 2000 or > 2999)
        {
            failures.Add(new ValidationFailure(
                nameof(instance.Year), "implausible", "That is not a plausible year."));
        }

        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}

/// <summary>Opens the series.</summary>
public sealed class OpenDocumentSeriesCommandHandler : ICommandHandler<OpenDocumentSeriesCommand, Guid>
{
    private readonly IDocumentSeriesRepository _series;
    private readonly IInvoicingUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public OpenDocumentSeriesCommandHandler(
        IDocumentSeriesRepository series,
        IInvoicingUnitOfWork unitOfWork)
    {
        _series = series;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result<Guid>> HandleAsync(
        OpenDocumentSeriesCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        DocumentTypeCodes.TryFromCode(request.Type, out DocumentType type);
        string code = request.Code.Trim().ToUpperInvariant();

        if (await _series.CodeExistsAsync(type, code, request.Year, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<Guid>(InvoicingErrors.Series.CodeExists);
        }

        Result<DocumentSeries> series = DocumentSeries.Open(type, request.Code, request.Year);

        if (series.IsFailure)
        {
            return Result.Failure<Guid>(series.Error);
        }

        _series.Add(series.Value);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return series.Value.Id.Value;
    }
}

/// <summary>
/// Records the validation code the tax authority returned when the series was declared.
/// <para>
/// This is a manual step today. The AT accepts series declarations through a web service, and
/// automating it is worth doing — but a wrong code here breaks every ATCUD the series will ever
/// produce, so somebody typing it off the AT's own screen is not the wrong shape to start with.
/// </para>
/// </summary>
/// <param name="SeriesId">The series.</param>
/// <param name="ValidationCode">The code from the tax authority, e.g. <c>CSDF7T5H</c>.</param>
public sealed record ValidateDocumentSeriesCommand(Guid SeriesId, string ValidationCode) : ICommand;

/// <summary>Records the code.</summary>
public sealed class ValidateDocumentSeriesCommandHandler : ICommandHandler<ValidateDocumentSeriesCommand>
{
    private readonly IDocumentSeriesRepository _series;
    private readonly IInvoicingUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    /// <summary>Initializes the handler.</summary>
    public ValidateDocumentSeriesCommandHandler(
        IDocumentSeriesRepository series,
        IInvoicingUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _series = series;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        ValidateDocumentSeriesCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        DocumentSeries? series = await _series
            .GetByIdAsync(new DocumentSeriesId(request.SeriesId), cancellationToken)
            .ConfigureAwait(false);

        if (series is null)
        {
            return InvoicingErrors.Series.NotFound(request.SeriesId.ToString());
        }

        Result validated = series.Validate(request.ValidationCode, _clock.UtcNow);

        if (validated.IsFailure)
        {
            return validated;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>Puts a series into service, so documents start coming out of it.</summary>
/// <param name="SeriesId">The series.</param>
public sealed record ActivateDocumentSeriesCommand(Guid SeriesId) : ICommand;

/// <summary>Activates the series.</summary>
public sealed class ActivateDocumentSeriesCommandHandler : ICommandHandler<ActivateDocumentSeriesCommand>
{
    private readonly IDocumentSeriesRepository _series;
    private readonly IInvoicingUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public ActivateDocumentSeriesCommandHandler(
        IDocumentSeriesRepository series,
        IInvoicingUnitOfWork unitOfWork)
    {
        _series = series;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        ActivateDocumentSeriesCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        DocumentSeries? series = await _series
            .GetByIdAsync(new DocumentSeriesId(request.SeriesId), cancellationToken)
            .ConfigureAwait(false);

        if (series is null)
        {
            return InvoicingErrors.Series.NotFound(request.SeriesId.ToString());
        }

        Result activated = series.Activate();

        if (activated.IsFailure)
        {
            return activated;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>
/// Closes a series to new documents.
/// <para>
/// The tax authority has to be told separately. A series that goes quiet without being closed
/// looks like paperwork somebody is hiding, which is a conversation worth not having.
/// </para>
/// </summary>
/// <param name="SeriesId">The series.</param>
public sealed record CloseDocumentSeriesCommand(Guid SeriesId) : ICommand;

/// <summary>Closes the series.</summary>
public sealed class CloseDocumentSeriesCommandHandler : ICommandHandler<CloseDocumentSeriesCommand>
{
    private readonly IDocumentSeriesRepository _series;
    private readonly IInvoicingUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    /// <summary>Initializes the handler.</summary>
    public CloseDocumentSeriesCommandHandler(
        IDocumentSeriesRepository series,
        IInvoicingUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _series = series;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        CloseDocumentSeriesCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        DocumentSeries? series = await _series
            .GetByIdAsync(new DocumentSeriesId(request.SeriesId), cancellationToken)
            .ConfigureAwait(false);

        if (series is null)
        {
            return InvoicingErrors.Series.NotFound(request.SeriesId.ToString());
        }

        Result closed = series.Close(_clock.UtcNow);

        if (closed.IsFailure)
        {
            return closed;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
