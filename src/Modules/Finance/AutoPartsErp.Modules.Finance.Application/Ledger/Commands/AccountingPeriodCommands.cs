using System.Globalization;
using AutoPartsErp.Modules.Finance.Domain;
using AutoPartsErp.Modules.Finance.Domain.Ledger;
using AutoPartsErp.SharedKernel.Abstractions;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Finance.Application.Ledger.Commands;

/// <summary>Opens an accounting month, so entries dated inside it have somewhere to go.</summary>
/// <param name="Year">The calendar year.</param>
/// <param name="Month">The calendar month, 1 to 12.</param>
public sealed record OpenAccountingPeriodCommand(int Year, int Month) : ICommand<Guid>;

/// <summary>Opens the period.</summary>
public sealed class OpenAccountingPeriodCommandHandler
    : ICommandHandler<OpenAccountingPeriodCommand, Guid>
{
    private readonly IAccountingPeriodRepository _periods;
    private readonly IFinanceUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public OpenAccountingPeriodCommandHandler(
        IAccountingPeriodRepository periods,
        IFinanceUnitOfWork unitOfWork)
    {
        _periods = periods;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result<Guid>> HandleAsync(
        OpenAccountingPeriodCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Checked here so a second attempt reads as a conflict with a sentence rather than a
        // constraint violation. The unique index on (tenant, year, month) is still the authority.
        AccountingPeriod? existing = await _periods
            .GetForAsync(request.Year, request.Month, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return Result.Failure<Guid>(FinanceErrors.Period.AlreadyExists);
        }

        Result<AccountingPeriod> period = AccountingPeriod.Open(request.Year, request.Month);

        if (period.IsFailure)
        {
            return Result.Failure<Guid>(period.Error);
        }

        _periods.Add(period.Value);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return period.Value.Id.Value;
    }
}

/// <summary>Closes an accounting month. Nothing more is posted into it.</summary>
/// <param name="Year">The calendar year.</param>
/// <param name="Month">The calendar month, 1 to 12.</param>
public sealed record CloseAccountingPeriodCommand(int Year, int Month) : ICommand;

/// <summary>Closes the period.</summary>
public sealed class CloseAccountingPeriodCommandHandler
    : ICommandHandler<CloseAccountingPeriodCommand>
{
    private readonly IAccountingPeriodRepository _periods;
    private readonly IFinanceUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    /// <summary>Initializes the handler.</summary>
    public CloseAccountingPeriodCommandHandler(
        IAccountingPeriodRepository periods,
        IFinanceUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _periods = periods;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        CloseAccountingPeriodCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        AccountingPeriod? period = await _periods
            .GetForAsync(request.Year, request.Month, cancellationToken)
            .ConfigureAwait(false);

        if (period is null)
        {
            return FinanceErrors.Period.NotFound(
                string.Create(
                    CultureInfo.InvariantCulture, $"{request.Year}-{request.Month:D2}"));
        }

        // The question the aggregate cannot ask: whether every month before this one is closed.
        bool previousIsClosed = await _periods
            .EveryEarlierIsClosedAsync(request.Year, request.Month, cancellationToken)
            .ConfigureAwait(false);

        Result closed = period.Close(_clock.UtcNow, _clock.TodayUtc, previousIsClosed);

        if (closed.IsFailure)
        {
            return closed;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>Reopens a closed accounting month, with a reason somebody wrote.</summary>
/// <param name="Year">The calendar year.</param>
/// <param name="Month">The calendar month, 1 to 12.</param>
/// <param name="Reason">Why it is being reopened.</param>
public sealed record ReopenAccountingPeriodCommand(
    int Year,
    int Month,
    string Reason) : ICommand;

/// <summary>Reopens the period.</summary>
public sealed class ReopenAccountingPeriodCommandHandler
    : ICommandHandler<ReopenAccountingPeriodCommand>
{
    private readonly IAccountingPeriodRepository _periods;
    private readonly IFinanceUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public ReopenAccountingPeriodCommandHandler(
        IAccountingPeriodRepository periods,
        IFinanceUnitOfWork unitOfWork)
    {
        _periods = periods;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        ReopenAccountingPeriodCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        AccountingPeriod? period = await _periods
            .GetForAsync(request.Year, request.Month, cancellationToken)
            .ConfigureAwait(false);

        if (period is null)
        {
            return FinanceErrors.Period.NotFound(
                string.Create(
                    CultureInfo.InvariantCulture, $"{request.Year}-{request.Month:D2}"));
        }

        bool laterIsClosed = await _periods
            .AnyLaterIsClosedAsync(request.Year, request.Month, cancellationToken)
            .ConfigureAwait(false);

        Result reopened = period.Reopen(request.Reason, laterIsClosed);

        if (reopened.IsFailure)
        {
            return reopened;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
