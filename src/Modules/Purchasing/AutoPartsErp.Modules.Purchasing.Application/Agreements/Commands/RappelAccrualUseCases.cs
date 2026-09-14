using AutoPartsErp.Modules.Purchasing.Domain;
using AutoPartsErp.Modules.Purchasing.Domain.Agreements;
using AutoPartsErp.SharedKernel.Abstractions;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Purchasing.Application.Agreements.Commands;

/// <summary>Records a rebate credit note the supplier sent against a period.</summary>
/// <param name="RappelAccrualId">The period.</param>
/// <param name="Amount">What the credit note is for.</param>
public sealed record RecordRappelCreditNoteCommand(
    Guid RappelAccrualId,
    decimal Amount) : ICommand;

/// <summary>Records the credit note.</summary>
public sealed class RecordRappelCreditNoteCommandHandler
    : ICommandHandler<RecordRappelCreditNoteCommand>
{
    private readonly IRappelAccrualRepository _accruals;
    private readonly IPurchasingUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public RecordRappelCreditNoteCommandHandler(
        IRappelAccrualRepository accruals,
        IPurchasingUnitOfWork unitOfWork)
    {
        _accruals = accruals;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        RecordRappelCreditNoteCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        RappelAccrual? accrual = await _accruals
            .GetByIdAsync(new RappelAccrualId(request.RappelAccrualId), cancellationToken)
            .ConfigureAwait(false);

        if (accrual is null)
        {
            return PurchasingErrors.Accrual.NotFound(request.RappelAccrualId.ToString());
        }

        Result recorded = accrual.RecordCreditNote(
            Money.Of(request.Amount, accrual.Currency));

        if (recorded.IsFailure)
        {
            return recorded;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>Closes a rebate period. What is outstanding on it becomes a claim.</summary>
/// <param name="RappelAccrualId">The period.</param>
public sealed record CloseRappelPeriodCommand(Guid RappelAccrualId) : ICommand;

/// <summary>Closes the period.</summary>
public sealed class CloseRappelPeriodCommandHandler : ICommandHandler<CloseRappelPeriodCommand>
{
    private readonly IRappelAccrualRepository _accruals;
    private readonly IPurchasingUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    /// <summary>Initializes the handler.</summary>
    public CloseRappelPeriodCommandHandler(
        IRappelAccrualRepository accruals,
        IPurchasingUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _accruals = accruals;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        CloseRappelPeriodCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        RappelAccrual? accrual = await _accruals
            .GetByIdAsync(new RappelAccrualId(request.RappelAccrualId), cancellationToken)
            .ConfigureAwait(false);

        if (accrual is null)
        {
            return PurchasingErrors.Accrual.NotFound(request.RappelAccrualId.ToString());
        }

        Result closed = accrual.Close(_clock.TodayUtc);

        if (closed.IsFailure)
        {
            return closed;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>
/// What every supplier still owes in rebate, worst first.
/// <para>
/// The buyer's screen before a supplier meeting, and the reason any of this was built: a figure
/// nobody can see is a figure nobody chases.
/// </para>
/// </summary>
public sealed record GetOutstandingRappelQuery : IQuery<IReadOnlyList<RappelAccrualDto>>;

/// <summary>What one period has earned and how much of it has arrived.</summary>
/// <param name="Id">The period.</param>
/// <param name="SupplierId">The supplier.</param>
/// <param name="SupplierCode">Their short code.</param>
/// <param name="Basis">Whether it comes off invoices or arrives as a credit note.</param>
/// <param name="PeriodFrom">The first day.</param>
/// <param name="PeriodTo">The last day, inclusive.</param>
/// <param name="Purchased">What the period has bought, net.</param>
/// <param name="Earned">What the scale says it has earned on all of that.</param>
/// <param name="TakenOnInvoices">What has already come off documents.</param>
/// <param name="Credited">What the supplier has credited.</param>
/// <param name="Outstanding">What they still owe.</param>
/// <param name="IsOverclaimed">True when more was taken than the period turned out to earn.</param>
/// <param name="CurrencyCode">The currency.</param>
public sealed record RappelAccrualDto(
    Guid Id,
    Guid SupplierId,
    string SupplierCode,
    string Basis,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    decimal Purchased,
    decimal Earned,
    decimal TakenOnInvoices,
    decimal Credited,
    decimal Outstanding,
    bool IsOverclaimed,
    string CurrencyCode);

/// <summary>Answers what is outstanding.</summary>
public sealed class GetOutstandingRappelQueryHandler
    : IQueryHandler<GetOutstandingRappelQuery, IReadOnlyList<RappelAccrualDto>>
{
    private readonly IRappelAccrualRepository _accruals;

    /// <summary>Initializes the handler.</summary>
    public GetOutstandingRappelQueryHandler(IRappelAccrualRepository accruals)
    {
        _accruals = accruals;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<RappelAccrualDto>>> HandleAsync(
        GetOutstandingRappelQuery request,
        CancellationToken cancellationToken = default)
    {
        // Through the repository rather than the read store, which is a departure worth naming.
        // Outstanding is a subtraction the aggregate performs, and a projection would have to
        // re-implement it in SQL — two copies of one rule, which would drift, and the drift would
        // show up as a screen telling a buyer to chase a figure the aggregate disagrees with.
        // There are never many open periods: one per supplier who pays a rebate.
        IReadOnlyList<RappelAccrual> accruals = await _accruals
            .GetOutstandingAsync(cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<RappelAccrualDto> dtos =
        [
            .. accruals.Select(accrual => new RappelAccrualDto(
                accrual.Id.Value,
                accrual.SupplierId.Value,
                accrual.SupplierCode,
                accrual.Basis.ToString(),
                accrual.PeriodFrom,
                accrual.PeriodTo,
                accrual.Purchased.Amount,
                accrual.Earned.Amount,
                accrual.TakenOnInvoices.Amount,
                accrual.Credited.Amount,
                accrual.Outstanding.Amount,
                accrual.IsOverclaimed,
                accrual.CurrencyCode)),
        ];

        return Result.Success(dtos);
    }
}
