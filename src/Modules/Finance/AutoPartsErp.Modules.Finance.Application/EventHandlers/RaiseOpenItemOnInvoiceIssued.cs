using AutoPartsErp.IntegrationEvents.Invoicing;
using AutoPartsErp.Modules.Finance.Domain;
using AutoPartsErp.Modules.Finance.Domain.Customers;
using AutoPartsErp.Modules.Finance.Domain.Receivables;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Finance.Application.EventHandlers;

/// <summary>
/// Puts an issued document on the customer's account.
/// <para>
/// This is where the sales ledger comes from. Invoicing decides what the document says and this
/// module decides nothing at all: a document that exists is owed, and the only judgement in the
/// whole operation — the day it falls due — was made by whoever set the customer's payment terms.
/// </para>
/// <para>
/// Idempotent on the document rather than on the message. The outbox delivers at least once, and
/// an item that already exists for this document is the same fact arriving twice; raising a
/// second one would double what the customer owes, in a way that looks exactly like two real
/// invoices.
/// </para>
/// </summary>
public sealed class RaiseOpenItemOnInvoiceIssued
    : IIntegrationEventHandler<InvoiceIssuedIntegrationEvent>
{
    private readonly IOpenItemRepository _items;
    private readonly ICustomerTermsRepository _terms;
    private readonly IFinanceUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public RaiseOpenItemOnInvoiceIssued(
        IOpenItemRepository items,
        ICustomerTermsRepository terms,
        IFinanceUnitOfWork unitOfWork)
    {
        _items = items;
        _terms = terms;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// The document named a currency or a type this module does not understand, or the item
    /// refused the figures on it. Thrown rather than swallowed: a document that never reached the
    /// ledger is money nobody will ever chase, and it is invisible from every screen.
    /// </exception>
    public async Task HandleAsync(
        InvoiceIssuedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        var documentId = new DocumentRef(integrationEvent.InvoiceId);

        OpenItem? existing = await _items
            .GetByDocumentAsync(documentId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return;
        }

        if (!Currency.TryFromCode(integrationEvent.CurrencyCode, out Currency currency))
        {
            throw new InvalidOperationException(
                $"Cannot put {integrationEvent.DocumentNumber} on the sales ledger: "
                + $"'{integrationEvent.CurrencyCode}' is not a supported currency.");
        }

        OpenItemKind kind = KindOf(integrationEvent.Type);

        if (kind == OpenItemKind.Unknown)
        {
            throw new InvalidOperationException(
                $"Cannot put {integrationEvent.DocumentNumber} on the sales ledger: "
                + $"'{integrationEvent.Type}' is not a document type this module knows. Adding a "
                + "type to Invoicing means deciding here which way it points.");
        }

        var customerId = new CustomerRef(integrationEvent.CustomerId);

        DateOnly dueDate = await DueDateAsync(
            customerId, integrationEvent.DocumentDate, cancellationToken).ConfigureAwait(false);

        Result<OpenItem> raised = OpenItem.Raise(
            customerId,
            documentId,
            integrationEvent.DocumentNumber,
            kind,
            Money.Of(integrationEvent.GrossTotal, currency),
            integrationEvent.DocumentDate,
            dueDate);

        if (raised.IsFailure)
        {
            throw new InvalidOperationException(
                $"Cannot put {integrationEvent.DocumentNumber} on the sales ledger: "
                + raised.Error.Description);
        }

        _items.Add(raised.Value);

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Which way a document type points.
    /// <para>
    /// FS is a simplified invoice and FR an invoice-receipt; both are invoices for this purpose
    /// even though an FR has already been paid, because the ledger records the debt and then the
    /// receipt that clears it rather than pretending neither happened.
    /// </para>
    /// </summary>
    private static OpenItemKind KindOf(string? type) => type?.Trim().ToUpperInvariant() switch
    {
        "FT" or "FS" or "FR" => OpenItemKind.Invoice,
        "NC" => OpenItemKind.CreditNote,
        "ND" => OpenItemKind.DebitNote,
        _ => OpenItemKind.Unknown,
    };

    /// <summary>
    /// When the document falls due.
    /// <para>
    /// Terms arrive on their own event and there is no ordering guarantee between the two, so a
    /// customer's first invoice can land before their terms do. Falling back to the document date
    /// makes it due immediately, which shows up as overdue on the next ageing report — visible and
    /// wrong in the safe direction, rather than a document that quietly never falls due at all.
    /// </para>
    /// </summary>
    private async Task<DateOnly> DueDateAsync(
        CustomerRef customerId,
        DateOnly documentDate,
        CancellationToken cancellationToken)
    {
        CustomerTerms? terms = await _terms
            .GetByIdAsync(customerId, cancellationToken)
            .ConfigureAwait(false);

        return terms is null ? documentDate : terms.DueDateFor(documentDate);
    }
}

/// <summary>
/// Takes a voided document back off the customer's account.
/// <para>
/// Voiding is Invoicing's remedy for a document raised in error and caught before the customer
/// acted on it. Here it means the debt was never real, so the item comes off — unless somebody
/// has already paid part of it, which the aggregate refuses and this handler lets through as the
/// failure it is.
/// </para>
/// </summary>
public sealed class CancelOpenItemOnInvoiceVoided
    : IIntegrationEventHandler<InvoiceVoidedIntegrationEvent>
{
    private readonly IOpenItemRepository _items;
    private readonly IFinanceUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public CancelOpenItemOnInvoiceVoided(
        IOpenItemRepository items,
        IFinanceUnitOfWork unitOfWork)
    {
        _items = items;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// Money had already been matched against the document. Nothing here can resolve that: the
    /// receipt exists and points at this item, and cancelling it anyway would leave that
    /// allocation pointing at nothing. It needs a credit note or a refund, and a person.
    /// </exception>
    public async Task HandleAsync(
        InvoiceVoidedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        OpenItem? item = await _items
            .GetByDocumentAsync(new DocumentRef(integrationEvent.InvoiceId), cancellationToken)
            .ConfigureAwait(false);

        // Nothing to take off. Either the issue message has not been delivered yet - in which
        // case it will be, and will raise an item for a document that is already void - or this
        // document never reached the ledger at all. Both are worth knowing about and neither is
        // fixed by failing here, so the void is treated as done.
        if (item is null)
        {
            return;
        }

        Result cancelled = item.Cancel(integrationEvent.Reason);

        if (cancelled.IsFailure)
        {
            throw new InvalidOperationException(
                $"Cannot take {integrationEvent.DocumentNumber} off the sales ledger: "
                + cancelled.Error.Description);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
