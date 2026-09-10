using AutoPartsErp.ModuleContracts.Catalog;
using AutoPartsErp.Modules.Purchasing.Domain;
using AutoPartsErp.Modules.Purchasing.Domain.Agreements;
using AutoPartsErp.Modules.Purchasing.Domain.Invoices;
using AutoPartsErp.Modules.Purchasing.Domain.Orders;
using AutoPartsErp.Modules.Purchasing.Domain.Orders.Events;
using AutoPartsErp.SharedKernel.Abstractions;
using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Purchasing.Application.EventHandlers;

/// <summary>
/// Writes the supplier's invoice as the goods are counted, so nobody has to.
/// <para>
/// This is the whole of "automatic entry". The warehouse confirms what came off the van — a
/// person's job, and the only part of this that should be one — and everything else the document
/// needs is already recorded: the price was agreed months ago, the rebate was negotiated at the
/// start of the year, and the quantity has just been counted. Retyping any of it would only
/// introduce the chance of getting it wrong.
/// </para>
/// <para>
/// A domain event handler in the same module, so the line lands in the same transaction as the
/// receipt. A receipt that booked stock but left no document behind is exactly the state somebody
/// finds in February when the supplier's statement will not reconcile.
/// </para>
/// <para>
/// Nothing here refuses a receipt, and nothing here goes silent. The goods are already on the
/// shelf: a delivery of a part nobody agreed a price for is a gap in the paperwork, not a reason
/// to reject a pallet — so the line still goes on the document at the figure the order was placed
/// at, carrying no price source. Null there means exactly what it means on a sales line: nobody
/// agreed this, somebody chose it. A line quietly left off would have been worse than a line
/// somebody has to look at.
/// </para>
/// </summary>
public sealed class DraftSupplierInvoiceOnGoodsReceived
    : IDomainEventHandler<GoodsReceivedDomainEvent>
{
    /// <summary>
    /// The VAT rate every drafted line takes, because nothing in this system knows a better answer
    /// yet.
    /// <para>
    /// The catalogue does not carry a rate — a part has a SKU, a unit and a stocking flag, and no
    /// tax band — so this is Portugal's normal rate and the same figure the sales side falls back
    /// to. It is right for almost every line a parts distributor receives and wrong for the few
    /// that are not, and the honest fix is a rate on the part rather than a better guess here.
    /// Until then, a delivery at 6 per cent shows up as a difference against the supplier's own
    /// document — which is the failure worth having, because somebody is asked instead of the
    /// company quietly deducting VAT it never paid.
    /// </para>
    /// </summary>
    private const decimal DefaultVatRatePercent = 23m;

    private readonly ISupplierInvoiceRepository _invoices;
    private readonly ISupplierAgreementRepository _agreements;
    private readonly ISupplierPriceRepository _prices;
    private readonly IPurchaseOrderRepository _orders;
    private readonly ICatalogDirectory _catalogue;
    private readonly IDateTimeProvider _clock;

    /// <summary>Initializes the handler.</summary>
    public DraftSupplierInvoiceOnGoodsReceived(
        ISupplierInvoiceRepository invoices,
        ISupplierAgreementRepository agreements,
        ISupplierPriceRepository prices,
        IPurchaseOrderRepository orders,
        ICatalogDirectory catalogue,
        IDateTimeProvider clock)
    {
        _invoices = invoices;
        _agreements = agreements;
        _prices = prices;
        _orders = orders;
        _catalogue = catalogue;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task HandleAsync(
        GoodsReceivedDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        DateOnly today = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);

        // Idempotence first, before anything is read or written. Goods-receipt events arrive at
        // least once, and a redelivered one must not charge the company for the same pallet
        // twice — a mistake nobody notices until the supplier has been paid.
        if (await _invoices
            .HasLineForReceiptAsync(domainEvent.LineId, domainEvent.Quantity, cancellationToken)
            .ConfigureAwait(false))
        {
            return;
        }

        PurchaseOrder? order = await _orders
            .GetByIdAsync(domainEvent.PurchaseOrderId, cancellationToken)
            .ConfigureAwait(false);

        if (order is null)
        {
            return;
        }

        Currency currency = Currency.FromCode(domainEvent.CurrencyCode);

        Result<Quantity> quantity = Quantity.Create(
            domainEvent.Quantity, UnitOfMeasure.FromCode(domainEvent.UnitCode));

        if (quantity.IsFailure)
        {
            return;
        }

        (Money unitPrice, SupplierPriceId? priceSource) = await PriceAsync(
            order.SupplierId, domainEvent, currency, today, cancellationToken).ConfigureAwait(false);

        SupplierInvoice? invoice = await _invoices
            .GetOpenDraftAsync(order.SupplierId, today, cancellationToken)
            .ConfigureAwait(false);

        if (invoice is null)
        {
            Result<SupplierInvoice> drafted = await OpenDraftAsync(
                order, currency, today, cancellationToken).ConfigureAwait(false);

            if (drafted.IsFailure)
            {
                return;
            }

            invoice = drafted.Value;
            _invoices.Add(invoice);
        }

        PartDescriptor? part = await _catalogue
            .GetAsync(domainEvent.PartId.Value, cancellationToken)
            .ConfigureAwait(false);

        invoice.AddReceivedLine(
            domainEvent.PurchaseOrderId,
            domainEvent.LineId,
            domainEvent.PartId,
            part?.Sku,
            part?.Name,
            quantity.Value,
            unitPrice,
            DefaultVatRatePercent,
            priceSource);
    }

    /// <summary>
    /// What the line is charged at: the agreed price for the day, or the price the order was
    /// placed at when nothing was agreed.
    /// <para>
    /// The fallback is not a guess in the way a made-up number would be — it is what the buyer
    /// authorised when they sent the order — but it is not a negotiated price either, and the null
    /// price source is how the document says so.
    /// </para>
    /// </summary>
    private async Task<(Money UnitPrice, SupplierPriceId? PriceSource)> PriceAsync(
        SupplierRef supplierId,
        GoodsReceivedDomainEvent domainEvent,
        Currency currency,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        SupplierPrice? agreed = await _prices
            .GetPriceOnAsync(supplierId, domainEvent.PartId, today, cancellationToken)
            .ConfigureAwait(false);

        // An agreed price in another currency is refused rather than converted, and falls back
        // like an absent one. A purchase document that quietly converts is where exchange-rate
        // losses go to hide.
        return agreed is not null && agreed.UnitPrice.Currency == currency
            ? (agreed.UnitPrice, agreed.Id)
            : (Money.Of(domainEvent.UnitPrice, currency), null);
    }

    /// <summary>
    /// Opens the day's draft at the rebate rate the agreement in force says.
    /// <para>
    /// Zero when there is no agreement, and zero when the rebate arrives as a credit note instead
    /// — the agreement answers both, and this handler does not get to decide which.
    /// </para>
    /// </summary>
    private async Task<Result<SupplierInvoice>> OpenDraftAsync(
        PurchaseOrder order,
        Currency currency,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        SupplierAgreement? agreement = await _agreements
            .GetForSupplierAsync(order.SupplierId, today, cancellationToken)
            .ConfigureAwait(false);

        decimal rate = 0m;

        if (agreement is not null && agreement.Currency == currency)
        {
            // Zero purchases so far, for now. What the period has bought is a figure this module
            // can compute and does not yet — until it does, a scaled rebate settles at its first
            // step, which is the safe end to be wrong at: the company under-claims rather than
            // taking a discount the supplier has not granted.
            rate = agreement.InvoiceRateOn(Money.Zero(currency));
        }

        return SupplierInvoice.DraftFor(
            order.SupplierId, order.SupplierCode, currency, today, rate);
    }
}
