using AutoPartsErp.SharedKernel.Primitives;

namespace AutoPartsErp.Modules.Sales.Domain.Customers.Events;

/// <summary>Raised when Sales opens its own record of a customer.</summary>
/// <param name="CustomerId">The customer.</param>
/// <param name="Code">Their short code.</param>
public sealed record CustomerAccountOpenedDomainEvent(CustomerRef CustomerId, string Code) : DomainEvent;

/// <summary>
/// Raised when an order takes a customer over a threshold worth telling somebody about — here,
/// past nine tenths of their limit. The counter finding out at the moment of refusal is too late.
/// </summary>
/// <param name="CustomerId">The customer.</param>
/// <param name="Code">Their short code.</param>
/// <param name="Committed">What they now have outstanding.</param>
/// <param name="CreditLimit">Their limit.</param>
public sealed record CustomerCreditNearlyExhaustedDomainEvent(
    CustomerRef CustomerId,
    string Code,
    decimal Committed,
    decimal CreditLimit) : DomainEvent;
