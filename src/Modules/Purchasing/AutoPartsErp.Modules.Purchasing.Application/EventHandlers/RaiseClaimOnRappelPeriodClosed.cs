using AutoPartsErp.Modules.Purchasing.Domain;
using AutoPartsErp.Modules.Purchasing.Domain.Agreements;
using AutoPartsErp.Modules.Purchasing.Domain.Agreements.Events;
using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Purchasing.Application.EventHandlers;

/// <summary>
/// Turns what a closed rebate period is still owed into something somebody chases.
/// <para>
/// The moment the arithmetic becomes a job. The accrual has always known the figure — what the
/// scale earned, less what has already come off documents — and it has always been a number on a
/// screen that nobody had to do anything about. A claim has a number, a state and a supplier
/// attached to it, and a buyer's list of open ones is short enough to work through.
/// </para>
/// <para>
/// Both rebate bases end up here, because they are the same gap seen from two distances. On an
/// invoice, the shortfall is what crossing a step in October did to every document settled since
/// January, and a supplier's issued invoice cannot be re-rated by anybody. By credit note, it is
/// the whole year's rebate, which was never going to arrive unasked.
/// </para>
/// <para>
/// Nothing is raised for a period that is owed nothing, which is the ordinary outcome of a year
/// that settled its rebate on the invoices as it went.
/// </para>
/// </summary>
public sealed class RaiseClaimOnRappelPeriodClosed
    : IDomainEventHandler<RappelPeriodClosedDomainEvent>
{
    private readonly IRappelClaimRepository _claims;
    private readonly IPurchasingUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public RaiseClaimOnRappelPeriodClosed(
        IRappelClaimRepository claims,
        IPurchasingUnitOfWork unitOfWork)
    {
        _claims = claims;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// The claim refused the figures the period closed with. Thrown rather than swallowed: a
    /// rebate nobody raised a claim for is money the company simply does not collect, and it is
    /// invisible from every screen.
    /// </exception>
    public async Task HandleAsync(
        RappelPeriodClosedDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        if (domainEvent.OutstandingAmount <= 0m)
        {
            return;
        }

        // A second claim for one period would have the company asking for the same money twice.
        RappelClaim? existing = await _claims
            .GetForPeriodAsync(domainEvent.AccrualId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return;
        }

        string number = await _claims
            .NextClaimNumberAsync(domainEvent.PeriodTo.Year, cancellationToken)
            .ConfigureAwait(false);

        Result<RappelClaim> claim = RappelClaim.Raise(
            number,
            domainEvent.AccrualId,
            domainEvent.SupplierId,
            domainEvent.SupplierCode,
            domainEvent.PeriodFrom,
            domainEvent.PeriodTo,
            Money.Of(domainEvent.OutstandingAmount, Currency.FromCode(domainEvent.CurrencyCode)));

        if (claim.IsFailure)
        {
            throw new InvalidOperationException(
                $"A rebate claim could not be raised for {domainEvent.SupplierCode}: "
                + claim.Error.Description);
        }

        _claims.Add(claim.Value);

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
