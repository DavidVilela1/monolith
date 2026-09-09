using AutoPartsErp.IntegrationEvents.Invoicing;
using AutoPartsErp.Modules.Sales.Domain;
using AutoPartsErp.Modules.Sales.Domain.Returns;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Sales.Application.EventHandlers;

/// <summary>
/// Marks a return credited once the credit note for it has been issued.
/// <para>
/// The last step of the loop, and the reason it is here rather than in the command that drew the
/// note: drafting is not crediting. A draft can be abandoned, and a return that recorded a
/// half-written document as money returned would show a customer as settled while nobody had paid
/// them anything.
/// </para>
/// <para>
/// Without this a returns list shows goods credited three months ago as still owing somebody
/// money, which is the sort of screen people stop trusting and then stop using.
/// </para>
/// </summary>
public sealed class RecordCreditOnCustomerReturn
    : IIntegrationEventHandler<CustomerReturnCreditedIntegrationEvent>
{
    private readonly ICustomerReturnRepository _returns;
    private readonly ISalesUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public RecordCreditOnCustomerReturn(
        ICustomerReturnRepository returns,
        ISalesUnitOfWork unitOfWork)
    {
        _returns = returns;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// The return refused the credit, which means it is in a state the credit note should never
    /// have been drawn from.
    /// </exception>
    public async Task HandleAsync(
        CustomerReturnCreditedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        CustomerReturn? found = await _returns
            .GetByIdAsync(new CustomerReturnId(integrationEvent.CustomerReturnId), cancellationToken)
            .ConfigureAwait(false);

        // Not an error worth retrying forever. It means the credit note was raised against a
        // return outside this tenant, or one that has since been purged — neither of which this
        // handler can fix by trying again.
        if (found is null)
        {
            return;
        }

        Result recorded = found.RecordCredited(
            integrationEvent.DocumentNumber, integrationEvent.DocumentDate);

        if (recorded.IsFailure)
        {
            throw new InvalidOperationException(
                $"Cannot record {integrationEvent.DocumentNumber} against return "
                + $"{found.Number}: {recorded.Error.Description}");
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
