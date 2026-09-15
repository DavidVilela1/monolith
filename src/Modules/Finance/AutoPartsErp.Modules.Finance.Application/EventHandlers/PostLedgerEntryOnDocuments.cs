using AutoPartsErp.IntegrationEvents.Invoicing;
using AutoPartsErp.IntegrationEvents.Purchasing;
using AutoPartsErp.Modules.Finance.Application.Ledger;
using AutoPartsErp.Modules.Finance.Domain.Ledger;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Finance.Application.EventHandlers;

/// <summary>
/// Posts an issued sales document to the general ledger.
/// <para>
/// The handler knows how to read its own event and nothing else: what a sale turns into is the
/// rule's business, and whether the month is still open is the poster's. All this does is say
/// "this happened, on this day, and it was worth these three figures".
/// </para>
/// <para>
/// Idempotent through the record of facts rather than here. The outbox delivers at least once, and
/// the row already written for this document is what stops a redelivered message posting a second
/// sale.
/// </para>
/// </summary>
public sealed class PostSaleOnInvoiceIssued
    : IIntegrationEventHandler<InvoiceIssuedIntegrationEvent>
{
    private readonly ILedgerPoster _poster;

    /// <summary>Initializes the handler.</summary>
    public PostSaleOnInvoiceIssued(ILedgerPoster poster)
    {
        _poster = poster;
    }

    /// <inheritdoc />
    public async Task HandleAsync(
        InvoiceIssuedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        Currency currency = Currency.FromCode(integrationEvent.CurrencyCode);

        await _poster.PostFactAsync(
            PostingFacts.SalesInvoiceIssued,
            integrationEvent.DocumentNumber,
            integrationEvent.DocumentDate,
            $"{integrationEvent.Type} {integrationEvent.DocumentNumber}",
            new Dictionary<string, Money>(StringComparer.Ordinal)
            {
                [PostingFacts.Net] = Money.Of(integrationEvent.NetTotal, currency),
                [PostingFacts.Vat] = Money.Of(integrationEvent.VatTotal, currency),
                [PostingFacts.Gross] = Money.Of(integrationEvent.GrossTotal, currency),
            },
            cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Reverses an issued document's posting when it is voided.
/// <para>
/// Its own fact rather than a reversal of the first entry, because the two are mapped separately:
/// a company may want a void to land on a different account from the sale it cancels, and that is
/// the accountant's decision and not this handler's. What the event carries is the gross total,
/// so the rule for it reverses the debt and nothing else.
/// </para>
/// </summary>
public sealed class PostVoidOnInvoiceVoided
    : IIntegrationEventHandler<InvoiceVoidedIntegrationEvent>
{
    private readonly ILedgerPoster _poster;

    /// <summary>Initializes the handler.</summary>
    public PostVoidOnInvoiceVoided(ILedgerPoster poster)
    {
        _poster = poster;
    }

    /// <inheritdoc />
    public async Task HandleAsync(
        InvoiceVoidedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        // The void carries no date of its own, so the entry belongs to the day it is processed.
        // Backdating it into the document's own month would change a month that may already have
        // been reported — which is exactly what the period guard exists to refuse.
        await _poster.PostFactAsync(
            PostingFacts.SalesInvoiceVoided,
            integrationEvent.DocumentNumber,
            DateOnly.FromDateTime(integrationEvent.OccurredAtUtc.UtcDateTime),
            $"Void of {integrationEvent.DocumentNumber}: {integrationEvent.Reason}",
            new Dictionary<string, Money>(StringComparer.Ordinal)
            {
                [PostingFacts.Gross] =
                    Money.Of(integrationEvent.GrossTotal, Currency.Default),
            },
            cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Posts a supplier's reconciled invoice to the general ledger.
/// <para>
/// The other direction of the same idea: the goods and the deductible VAT on one side, what the
/// company now owes on the other. Which accounts those are is configuration; that this happened
/// is a fact.
/// </para>
/// </summary>
public sealed class PostPurchaseOnSupplierInvoiceSettled
    : IIntegrationEventHandler<SupplierInvoiceSettledIntegrationEvent>
{
    private readonly ILedgerPoster _poster;

    /// <summary>Initializes the handler.</summary>
    public PostPurchaseOnSupplierInvoiceSettled(ILedgerPoster poster)
    {
        _poster = poster;
    }

    /// <inheritdoc />
    public async Task HandleAsync(
        SupplierInvoiceSettledIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        Currency currency = Currency.FromCode(integrationEvent.CurrencyCode);

        // The supplier's own number is not unique across suppliers — two of them can both issue a
        // "FA 1/2026" — so the reference carries the supplier code as well. Without it the second
        // one would look like a message already dealt with and never reach the ledger at all.
        string reference =
            $"{integrationEvent.SupplierCode}/{integrationEvent.SupplierDocumentNumber}";

        await _poster.PostFactAsync(
            PostingFacts.SupplierInvoiceSettled,
            reference,
            integrationEvent.DocumentDate,
            $"Supplier invoice {reference}",
            new Dictionary<string, Money>(StringComparer.Ordinal)
            {
                [PostingFacts.Net] = Money.Of(integrationEvent.NetAmount, currency),
                [PostingFacts.Vat] = Money.Of(integrationEvent.VatAmount, currency),
                [PostingFacts.Gross] = Money.Of(integrationEvent.GrossAmount, currency),

                // The rebate is already inside the net the supplier charged. It is carried so a
                // company that wants it on its own account can map it, and a rule that ignores it
                // is the ordinary case rather than an oversight.
                [PostingFacts.Rebate] = Money.Zero(currency),
            },
            cancellationToken).ConfigureAwait(false);
    }
}
