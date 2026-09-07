using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Finance.Domain.Receivables.Events;

/// <summary>A document was put on a customer's account.</summary>
/// <param name="OpenItemId">The item raised.</param>
/// <param name="CustomerId">Whose account it went on.</param>
/// <param name="DocumentId">The document in Invoicing.</param>
/// <param name="DocumentNumber">Its number, as printed.</param>
/// <param name="Kind">Invoice, credit note or debit note.</param>
/// <param name="Amount">The gross total.</param>
/// <param name="DueDate">When it falls due.</param>
public sealed record OpenItemRaisedDomainEvent(
    OpenItemId OpenItemId,
    CustomerRef CustomerId,
    DocumentRef DocumentId,
    string DocumentNumber,
    OpenItemKind Kind,
    Money Amount,
    DateOnly DueDate) : DomainEvent;

/// <summary>
/// An item has nothing left outstanding.
/// <para>
/// Raised on the transition, not on every match. "This invoice is now paid" is a fact somebody
/// wants to react to; "eighty euros came off it" is bookkeeping, and the allocation row already
/// records that.
/// </para>
/// </summary>
/// <param name="OpenItemId">The item.</param>
/// <param name="CustomerId">Whose account it was on.</param>
/// <param name="DocumentNumber">Its number.</param>
/// <param name="Amount">What it was for.</param>
public sealed record OpenItemSettledDomainEvent(
    OpenItemId OpenItemId,
    CustomerRef CustomerId,
    string DocumentNumber,
    Money Amount) : DomainEvent;

/// <summary>The document behind an item was voided, so it is no longer owed.</summary>
/// <param name="OpenItemId">The item.</param>
/// <param name="CustomerId">Whose account it was on.</param>
/// <param name="DocumentNumber">Its number.</param>
/// <param name="Amount">What it was for.</param>
/// <param name="Reason">Why the document was voided.</param>
public sealed record OpenItemCancelledDomainEvent(
    OpenItemId OpenItemId,
    CustomerRef CustomerId,
    string DocumentNumber,
    Money Amount,
    string? Reason) : DomainEvent;
