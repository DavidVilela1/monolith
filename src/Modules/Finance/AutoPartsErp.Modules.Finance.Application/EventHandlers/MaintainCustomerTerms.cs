using AutoPartsErp.IntegrationEvents.Partners;
using AutoPartsErp.Modules.Finance.Domain;
using AutoPartsErp.Modules.Finance.Domain.Customers;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Finance.Application.EventHandlers;

/// <summary>
/// Keeps Finance's copy of a customer's payment terms in step with Partners.
/// <para>
/// Sales already builds its own record from this same event, and this is a second consumer of it
/// rather than a reason to share one. Each module holds the fields it can defend: Sales needs a
/// credit limit and a trading status, Finance needs a due date and a currency, and neither can be
/// broken by a change to the other.
/// </para>
/// <para>
/// Idempotent, because the outbox delivers at least once and because Partners can grant the
/// customer role again on an account that was closed. A second delivery re-applies the same
/// terms rather than failing.
/// </para>
/// </summary>
public sealed class MaintainCustomerTerms
    : IIntegrationEventHandler<CustomerAccountOpenedIntegrationEvent>
{
    private readonly ICustomerTermsRepository _terms;
    private readonly IFinanceUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public MaintainCustomerTerms(ICustomerTermsRepository terms, IFinanceUnitOfWork unitOfWork)
    {
        _terms = terms;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// The event named a currency this system does not know, or terms the aggregate refused.
    /// Thrown rather than swallowed: terms that silently failed to arrive produce invoices that
    /// fall due on the day they were issued, and nobody would notice until a customer was chased
    /// for something that was never late.
    /// </exception>
    public async Task HandleAsync(
        CustomerAccountOpenedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        if (!Currency.TryFromCode(integrationEvent.CurrencyCode, out Currency currency))
        {
            throw new InvalidOperationException(
                $"Cannot record payment terms for {integrationEvent.Code}: "
                + $"'{integrationEvent.CurrencyCode}' is not a supported currency.");
        }

        var customerId = new CustomerRef(integrationEvent.PartnerId);

        CustomerTerms? existing = await _terms
            .GetByIdAsync(customerId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            Result changed = existing.Change(
                integrationEvent.LegalName,
                currency,
                integrationEvent.PaymentDueInDays,
                integrationEvent.PaymentEndOfMonth);

            if (changed.IsFailure)
            {
                throw new InvalidOperationException(
                    $"Cannot update payment terms for {integrationEvent.Code}: "
                    + changed.Error.Description);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        Result<CustomerTerms> opened = CustomerTerms.Open(
            customerId,
            integrationEvent.Code,
            integrationEvent.LegalName,
            currency,
            integrationEvent.PaymentDueInDays,
            integrationEvent.PaymentEndOfMonth);

        if (opened.IsFailure)
        {
            throw new InvalidOperationException(
                $"Cannot record payment terms for {integrationEvent.Code}: "
                + opened.Error.Description);
        }

        _terms.Add(opened.Value);

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
