using AutoPartsErp.Modules.Finance.Application.Ledger;
using AutoPartsErp.Modules.Finance.Domain.Ledger;
using AutoPartsErp.Modules.Finance.Domain.Payments.Events;
using AutoPartsErp.Modules.Finance.Domain.Receipts.Events;
using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Finance.Application.EventHandlers;

/// <summary>
/// Posts money arriving from a customer.
/// <para>
/// <b>On recording, not on allocation.</b> The fact is that the money is in the bank; which
/// invoices it settles is the sales ledger's business and can take days to work out. A company
/// whose ledger waited for the allocation would show a bank balance that disagreed with the bank
/// for as long as somebody had not finished matching — which is exactly the state this module
/// exists to make visible rather than to reproduce.
/// </para>
/// <para>
/// A domain event rather than an integration one, because a receipt is this module's own. It uses
/// the poster's save-free path: domain events are dispatched inside the save that caused them, so
/// the receipt and the entry for it commit together or neither does.
/// </para>
/// </summary>
public sealed class PostReceiptOnMoneyReceived
    : IDomainEventHandler<ReceiptRecordedDomainEvent>
{
    private readonly ILedgerPoster _poster;

    /// <summary>Initializes the handler.</summary>
    public PostReceiptOnMoneyReceived(ILedgerPoster poster)
    {
        _poster = poster;
    }

    /// <inheritdoc />
    public async Task HandleAsync(
        ReceiptRecordedDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        if (domainEvent.Amount.IsZero)
        {
            return;
        }

        await _poster.RecordFactAsync(
            PostingFacts.CustomerReceipt,
            domainEvent.Number,
            domainEvent.ReceivedOn,
            $"Receipt {domainEvent.Number} ({domainEvent.Method})",
            new Dictionary<string, Money>(StringComparer.Ordinal)
            {
                [PostingFacts.Gross] = domainEvent.Amount,
            },
            cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Posts money leaving for a supplier.
/// <para>
/// The mirror of the receipt, on recording for the same reason: money can leave the bank before
/// anybody has worked out which of the supplier's invoices it paid, and the ledger should say so
/// the day it leaves.
/// </para>
/// <para>
/// One method distinguishes nothing here — a transfer and a cheque are both money out — but it is
/// written onto the entry, because "which of these was the cheque that never cleared?" is a
/// question somebody asks a bank statement at a time.
/// </para>
/// </summary>
public sealed class PostPaymentOnMoneyPaid
    : IDomainEventHandler<SupplierPaymentRecordedDomainEvent>
{
    private readonly ILedgerPoster _poster;

    /// <summary>Initializes the handler.</summary>
    public PostPaymentOnMoneyPaid(ILedgerPoster poster)
    {
        _poster = poster;
    }

    /// <inheritdoc />
    public async Task HandleAsync(
        SupplierPaymentRecordedDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        if (domainEvent.Amount == 0m)
        {
            return;
        }

        await _poster.RecordFactAsync(
            PostingFacts.SupplierPayment,
            domainEvent.Number,
            domainEvent.PaidOn,
            $"Payment {domainEvent.Number} to {domainEvent.SupplierCode} ({domainEvent.Method})",
            new Dictionary<string, Money>(StringComparer.Ordinal)
            {
                [PostingFacts.Gross] =
                    Money.Of(domainEvent.Amount, Currency.FromCode(domainEvent.CurrencyCode)),
            },
            cancellationToken).ConfigureAwait(false);
    }
}
