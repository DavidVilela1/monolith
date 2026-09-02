using AutoPartsErp.IntegrationEvents.Partners;
using AutoPartsErp.Modules.Sales.Domain;
using AutoPartsErp.Modules.Sales.Domain.Customers;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Sales.Application.EventHandlers;

/// <summary>
/// Opens Sales' own record of a customer when Partners grants the customer role.
/// <para>
/// Three of Partners' four published events have had no consumer since that module was written —
/// a hold that stopped nothing, a released hold nobody was waiting for, and a credit limit
/// change with no one to tell. This handler and the three below it are the other end of those
/// contracts, and the reason the counter can refuse a held account in milliseconds without
/// asking another module anything.
/// </para>
/// <para>
/// Idempotent, because the outbox delivers at-least-once and because Partners can grant the role
/// again on an account that was closed. A second delivery re-applies the same terms rather than
/// failing.
/// </para>
/// </summary>
public sealed class OpenCustomerAccountOnRoleGranted
    : IIntegrationEventHandler<CustomerAccountOpenedIntegrationEvent>
{
    private readonly ICustomerAccountRepository _customers;
    private readonly ISalesUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public OpenCustomerAccountOnRoleGranted(
        ICustomerAccountRepository customers,
        ISalesUnitOfWork unitOfWork)
    {
        _customers = customers;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// The event named a currency this system does not know, or terms the account rejected.
    /// Refusing is safer than guessing: an account opened with the wrong limit is worse than no
    /// account, because the first is invisible and the second is a phone call.
    /// </exception>
    public async Task HandleAsync(
        CustomerAccountOpenedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        if (!Currency.TryFromCode(integrationEvent.CurrencyCode, out Currency currency))
        {
            throw new InvalidOperationException(
                $"Cannot open a customer account for {integrationEvent.Code}: " +
                $"'{integrationEvent.CurrencyCode}' is not a supported currency.");
        }

        var customerId = new CustomerRef(integrationEvent.PartnerId);
        Money creditLimit = Money.Of(integrationEvent.CreditLimit, currency);

        CustomerAccount? existing = await _customers
            .GetByIdAsync(customerId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            Result applied = existing.ApplyTerms(
                integrationEvent.Code,
                integrationEvent.LegalName,
                creditLimit,
                integrationEvent.PaymentDueInDays,
                integrationEvent.PaymentEndOfMonth,
                integrationEvent.PriceListCode);

            if (applied.IsFailure)
            {
                throw new InvalidOperationException(
                    $"Could not update the customer account for {integrationEvent.Code}: {applied.Error}");
            }
        }
        else
        {
            Result<CustomerAccount> account = CustomerAccount.Open(
                customerId,
                integrationEvent.Code,
                integrationEvent.LegalName,
                creditLimit,
                integrationEvent.PaymentDueInDays,
                integrationEvent.PaymentEndOfMonth,
                integrationEvent.PriceListCode);

            if (account.IsFailure)
            {
                throw new InvalidOperationException(
                    $"Could not open a customer account for {integrationEvent.Code}: {account.Error}");
            }

            _customers.Add(account.Value);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Stops a held partner from ordering.
/// <para>
/// This is the event Partners raised with a comment saying Sales would listen for it, back when
/// there was no Sales. A held customer reaching the counter and being served anyway is the exact
/// failure the hold exists to prevent, and the person on the counter is rarely the person who
/// knows about the unpaid invoice.
/// </para>
/// </summary>
public sealed class HoldCustomerOnPartnerHeld
    : IIntegrationEventHandler<PartnerPlacedOnHoldIntegrationEvent>
{
    private readonly ICustomerAccountRepository _customers;
    private readonly ISalesUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public HoldCustomerOnPartnerHeld(
        ICustomerAccountRepository customers,
        ISalesUnitOfWork unitOfWork)
    {
        _customers = customers;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task HandleAsync(
        PartnerPlacedOnHoldIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        CustomerAccount? account = await _customers
            .GetByIdAsync(new CustomerRef(integrationEvent.PartnerId), cancellationToken)
            .ConfigureAwait(false);

        // A partner who is only a supplier has no account here, and holding them is not Sales'
        // business. Nothing to do is a success, not a failure.
        if (account is null)
        {
            return;
        }

        Result held = account.PlaceOnHold(integrationEvent.Reason);
        if (held.IsFailure)
        {
            throw new InvalidOperationException(
                $"Could not hold the account for {integrationEvent.Code}: {held.Error}");
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Lets a partner order again once the hold is lifted.</summary>
public sealed class ReleaseCustomerOnPartnerHoldReleased
    : IIntegrationEventHandler<PartnerHoldReleasedIntegrationEvent>
{
    private readonly ICustomerAccountRepository _customers;
    private readonly ISalesUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public ReleaseCustomerOnPartnerHoldReleased(
        ICustomerAccountRepository customers,
        ISalesUnitOfWork unitOfWork)
    {
        _customers = customers;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task HandleAsync(
        PartnerHoldReleasedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        CustomerAccount? account = await _customers
            .GetByIdAsync(new CustomerRef(integrationEvent.PartnerId), cancellationToken)
            .ConfigureAwait(false);

        if (account is null)
        {
            return;
        }

        Result released = account.ReleaseHold();
        if (released.IsFailure)
        {
            throw new InvalidOperationException(
                $"Could not release the hold on {integrationEvent.Code}: {released.Error}");
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Applies a credit limit change.
/// <para>
/// The new limit does not disturb what is already committed. Dropping a limit below current
/// exposure is a real thing that happens after a bad payment run, and the orders already out
/// were already promised — the new figure binds the next one.
/// </para>
/// </summary>
public sealed class ApplyCreditLimitChange
    : IIntegrationEventHandler<CreditLimitChangedIntegrationEvent>
{
    private readonly ICustomerAccountRepository _customers;
    private readonly ISalesUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public ApplyCreditLimitChange(
        ICustomerAccountRepository customers,
        ISalesUnitOfWork unitOfWork)
    {
        _customers = customers;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task HandleAsync(
        CreditLimitChangedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        CustomerAccount? account = await _customers
            .GetByIdAsync(new CustomerRef(integrationEvent.PartnerId), cancellationToken)
            .ConfigureAwait(false);

        if (account is null)
        {
            return;
        }

        // The event carries no currency - Partners only ever changes the amount - so the
        // account's own currency is the right one to use.
        Result changed = account.ChangeCreditLimit(
            Money.Of(integrationEvent.NewLimit, account.Currency));

        if (changed.IsFailure)
        {
            throw new InvalidOperationException(
                $"Could not change the credit limit for {integrationEvent.Code}: {changed.Error}");
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Closes the account when the relationship ends.</summary>
public sealed class CloseCustomerOnPartnerClosed
    : IIntegrationEventHandler<PartnerClosedIntegrationEvent>
{
    private readonly ICustomerAccountRepository _customers;
    private readonly ISalesUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public CloseCustomerOnPartnerClosed(
        ICustomerAccountRepository customers,
        ISalesUnitOfWork unitOfWork)
    {
        _customers = customers;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task HandleAsync(
        PartnerClosedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        CustomerAccount? account = await _customers
            .GetByIdAsync(new CustomerRef(integrationEvent.PartnerId), cancellationToken)
            .ConfigureAwait(false);

        if (account is null)
        {
            return;
        }

        // Deliberately does not check for open orders. Partners is the authority on the
        // relationship ending, and an order already out still has to be shipped and invoiced -
        // closing the account stops the next one, not this one.
        Result closed = account.Close();
        if (closed.IsFailure)
        {
            throw new InvalidOperationException(
                $"Could not close the account for {integrationEvent.Code}: {closed.Error}");
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
