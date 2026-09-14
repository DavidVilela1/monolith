using AutoPartsErp.IntegrationEvents.Purchasing;
using AutoPartsErp.Modules.Finance.Domain;
using AutoPartsErp.Modules.Finance.Domain.Payables;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Finance.Application.EventHandlers;

/// <summary>
/// Puts a supplier's accepted document on the purchase ledger.
/// <para>
/// The other end of the three-way check. Purchasing decides what the company owes; Finance records
/// that it owes it, from when, and to whom — and until this existed the whole chain from purchase
/// order to counted pallet to accepted invoice ended in a document nobody was going to pay.
/// </para>
/// <para>
/// The amount is the supplier's own stated figure, carried on the event. A payable opened for what
/// the system computed would never match the money leaving the bank, and reconciling those two
/// afterwards is exactly the work the check exists to avoid.
/// </para>
/// </summary>
public sealed class OpenPayableOnSupplierInvoiceSettled
    : IIntegrationEventHandler<SupplierInvoiceSettledIntegrationEvent>
{
    private readonly IPayableItemRepository _payables;
    private readonly IFinanceUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public OpenPayableOnSupplierInvoiceSettled(
        IPayableItemRepository payables,
        IFinanceUnitOfWork unitOfWork)
    {
        _payables = payables;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task HandleAsync(
        SupplierInvoiceSettledIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        var document = new SupplierInvoiceRef(integrationEvent.SupplierInvoiceId);

        // The outbox delivers at least once. An item that already exists for this document means
        // the message has been seen before, and paying a supplier twice because a message was
        // redelivered is the failure this one check prevents.
        PayableItem? existing = await _payables
            .GetByDocumentAsync(document, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return;
        }

        Result<PayableItem> item = PayableItem.Raise(
            new SupplierRef(integrationEvent.SupplierId),
            integrationEvent.SupplierCode,
            document,
            integrationEvent.SupplierDocumentNumber,
            PayableItemKind.Invoice,
            Money.Of(integrationEvent.GrossAmount, integrationEvent.CurrencyCode),
            integrationEvent.DocumentDate,
            integrationEvent.DueDate);

        // Thrown rather than swallowed, unlike most of what this system does with a bad message.
        // A supplier document that cannot reach the purchase ledger is money the company owes and
        // has no record of, and the outbox retrying loudly is better than a debt nobody knows
        // about — which is found by a supplier, months later, with interest.
        if (item.IsFailure)
        {
            throw new InvalidOperationException(
                $"Could not open a payable for supplier document " +
                $"'{integrationEvent.SupplierDocumentNumber}': {item.Error.Description}");
        }

        _payables.Add(item.Value);

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
