using AutoPartsErp.Modules.Purchasing.Domain;
using AutoPartsErp.Modules.Purchasing.Domain.Agreements;
using AutoPartsErp.SharedKernel.Abstractions;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Purchasing.Application.Agreements.Commands;

/// <summary>
/// Closes every rebate period that has ended.
/// <para>
/// What the sweeper calls. Closing a period is what turns its shortfall into a claim, so a period
/// nobody closes is a rebate nobody collects — and "somebody presses a button every January" is
/// not a plan, it is a thing that works for two years and then does not.
/// </para>
/// <para>
/// Bounded per pass so a backlog cannot stall the job, and each period is closed on its own: one
/// that refuses stops none of the others.
/// </para>
/// </summary>
/// <param name="BatchSize">How many periods to close in one pass.</param>
public sealed record CloseDueRappelPeriodsCommand(int BatchSize = 200) : ICommand<int>;

/// <summary>Closes the periods.</summary>
public sealed class CloseDueRappelPeriodsCommandHandler
    : ICommandHandler<CloseDueRappelPeriodsCommand, int>
{
    private readonly IRappelAccrualRepository _accruals;
    private readonly IPurchasingUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    /// <summary>Initializes the handler.</summary>
    public CloseDueRappelPeriodsCommandHandler(
        IRappelAccrualRepository accruals,
        IPurchasingUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _accruals = accruals;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result<int>> HandleAsync(
        CloseDueRappelPeriodsCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        DateOnly today = _clock.TodayUtc;

        IReadOnlyList<RappelAccrual> due = await _accruals
            .GetDueForClosingAsync(today, request.BatchSize, cancellationToken)
            .ConfigureAwait(false);

        int closed = 0;

        foreach (RappelAccrual accrual in due)
        {
            if (accrual.Close(today).IsSuccess)
            {
                closed++;
            }
        }

        if (closed > 0)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return closed;
    }
}

/// <summary>Records that the supplier has been asked for a rebate they owe.</summary>
/// <param name="ClaimId">The claim.</param>
/// <param name="Note">What was said, or how they were asked.</param>
public sealed record SendRappelClaimCommand(Guid ClaimId, string? Note = null) : ICommand;

/// <summary>Sends the claim.</summary>
public sealed class SendRappelClaimCommandHandler : ICommandHandler<SendRappelClaimCommand>
{
    private readonly IRappelClaimRepository _claims;
    private readonly IPurchasingUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    /// <summary>Initializes the handler.</summary>
    public SendRappelClaimCommandHandler(
        IRappelClaimRepository claims,
        IPurchasingUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _claims = claims;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        SendRappelClaimCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        RappelClaim? claim = await _claims
            .GetByIdAsync(new RappelClaimId(request.ClaimId), cancellationToken)
            .ConfigureAwait(false);

        if (claim is null)
        {
            return PurchasingErrors.Claim.NotFound(request.ClaimId.ToString());
        }

        Result sent = claim.Send(_clock.TodayUtc, request.Note);

        if (sent.IsFailure)
        {
            return sent;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>
/// Records a credit note the supplier sent against a claim.
/// <para>
/// Goes onto the period as well as the claim. The accrual is what says whether the year was
/// settled in the end, and a credit that reached only the claim would leave it reporting a
/// shortfall the company has already been paid for.
/// </para>
/// </summary>
/// <param name="ClaimId">The claim.</param>
/// <param name="Amount">What the credit note is for.</param>
/// <param name="CreditNoteNumber">Their number for it.</param>
public sealed record CreditRappelClaimCommand(
    Guid ClaimId,
    decimal Amount,
    string? CreditNoteNumber = null) : ICommand;

/// <summary>Credits the claim.</summary>
public sealed class CreditRappelClaimCommandHandler : ICommandHandler<CreditRappelClaimCommand>
{
    private readonly IRappelClaimRepository _claims;
    private readonly IRappelAccrualRepository _accruals;
    private readonly IPurchasingUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public CreditRappelClaimCommandHandler(
        IRappelClaimRepository claims,
        IRappelAccrualRepository accruals,
        IPurchasingUnitOfWork unitOfWork)
    {
        _claims = claims;
        _accruals = accruals;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        CreditRappelClaimCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        RappelClaim? claim = await _claims
            .GetByIdAsync(new RappelClaimId(request.ClaimId), cancellationToken)
            .ConfigureAwait(false);

        if (claim is null)
        {
            return PurchasingErrors.Claim.NotFound(request.ClaimId.ToString());
        }

        Money amount = Money.Of(request.Amount, claim.Currency);

        Result credited = claim.Credit(amount, request.CreditNoteNumber);

        if (credited.IsFailure)
        {
            return credited;
        }

        // The period is closed by now, and RecordCreditNote deliberately still accepts one: a
        // supplier's note arrives weeks after the year ends, and refusing it would leave the
        // accrual permanently claiming a shortfall that has been paid.
        RappelAccrual? accrual = await _accruals
            .GetByIdAsync(claim.AccrualId, cancellationToken)
            .ConfigureAwait(false);

        if (accrual is not null)
        {
            Result recorded = accrual.RecordCreditNote(amount);

            if (recorded.IsFailure)
            {
                return recorded;
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>Gives up on what is left of a claim, with a reason.</summary>
/// <param name="ClaimId">The claim.</param>
/// <param name="Reason">Why the company will not get it.</param>
public sealed record WriteOffRappelClaimCommand(Guid ClaimId, string Reason) : ICommand;

/// <summary>Writes the claim off.</summary>
public sealed class WriteOffRappelClaimCommandHandler
    : ICommandHandler<WriteOffRappelClaimCommand>
{
    private readonly IRappelClaimRepository _claims;
    private readonly IPurchasingUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public WriteOffRappelClaimCommandHandler(
        IRappelClaimRepository claims,
        IPurchasingUnitOfWork unitOfWork)
    {
        _claims = claims;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        WriteOffRappelClaimCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        RappelClaim? claim = await _claims
            .GetByIdAsync(new RappelClaimId(request.ClaimId), cancellationToken)
            .ConfigureAwait(false);

        if (claim is null)
        {
            return PurchasingErrors.Claim.NotFound(request.ClaimId.ToString());
        }

        Result written = claim.WriteOff(request.Reason);

        if (written.IsFailure)
        {
            return written;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>Every rebate claim still waiting on a supplier, oldest period first.</summary>
public sealed record ListOpenRappelClaimsQuery : IQuery<IReadOnlyList<RappelClaimView>>;

/// <summary>A claim, as a buyer's screen shows it.</summary>
/// <param name="ClaimId">The claim.</param>
/// <param name="Number">Our number for it.</param>
/// <param name="SupplierCode">The supplier.</param>
/// <param name="PeriodFrom">The first day of the period.</param>
/// <param name="PeriodTo">The last day of the period.</param>
/// <param name="Status">Where it has got to.</param>
/// <param name="ClaimedAmount">What was asked for.</param>
/// <param name="CreditedAmount">What has arrived.</param>
/// <param name="OutstandingAmount">What is still to come.</param>
/// <param name="CurrencyCode">The currency.</param>
/// <param name="SentOn">When the supplier was asked.</param>
public sealed record RappelClaimView(
    Guid ClaimId,
    string Number,
    string SupplierCode,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    string Status,
    decimal ClaimedAmount,
    decimal CreditedAmount,
    decimal OutstandingAmount,
    string CurrencyCode,
    DateOnly? SentOn);

/// <summary>Lists the open claims.</summary>
public sealed class ListOpenRappelClaimsQueryHandler
    : IQueryHandler<ListOpenRappelClaimsQuery, IReadOnlyList<RappelClaimView>>
{
    private readonly IRappelClaimRepository _claims;

    /// <summary>Initializes the handler.</summary>
    public ListOpenRappelClaimsQueryHandler(IRappelClaimRepository claims)
    {
        _claims = claims;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<RappelClaimView>>> HandleAsync(
        ListOpenRappelClaimsQuery request,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<RappelClaim> claims = await _claims
            .GetOpenAsync(cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<RappelClaimView> views =
        [
            .. claims.Select(claim => new RappelClaimView(
                claim.Id.Value,
                claim.Number,
                claim.SupplierCode,
                claim.PeriodFrom,
                claim.PeriodTo,
                claim.Status.ToString(),
                claim.Claimed.Amount,
                claim.Credited.Amount,
                claim.Outstanding.Amount,
                claim.CurrencyCode,
                claim.SentOn)),
        ];

        return Result.Success(views);
    }
}
