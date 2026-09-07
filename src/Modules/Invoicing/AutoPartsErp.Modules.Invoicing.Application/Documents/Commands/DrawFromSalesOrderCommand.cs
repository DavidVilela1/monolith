using AutoPartsErp.ModuleContracts.Partners;
using AutoPartsErp.ModuleContracts.Sales;
using AutoPartsErp.Modules.Invoicing.Application.Options;
using AutoPartsErp.Modules.Invoicing.Domain;
using AutoPartsErp.Modules.Invoicing.Domain.Invoices;
using AutoPartsErp.Modules.Invoicing.Domain.Series;
using AutoPartsErp.SharedKernel.Abstractions;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Invoicing.Application.Documents.Commands;

/// <summary>
/// Draws a draft document from a dispatched sales order.
/// <para>
/// It produces a draft and stops there rather than issuing in the same breath, which looks like
/// an extra step and is not. Issuing takes a number out of a gapless sequence; a draft can be
/// looked at, corrected, and abandoned at no cost. Somebody should see what is about to be sent
/// to a customer, and on the one flow where nobody will — a counter sale being handed over — the
/// caller makes two requests instead of one, which is a cheap price for the other case.
/// </para>
/// </summary>
/// <param name="SalesOrderId">The order to bill.</param>
/// <param name="DocumentDate">
/// The date on the document, defaulting to today. Not the dispatch date: an invoice is dated when
/// it is raised, and the law allows a few days between the goods leaving and the paperwork.
/// </param>
public sealed record DrawFromSalesOrderCommand(Guid SalesOrderId, DateOnly? DocumentDate = null)
    : ICommand<Guid>;

/// <summary>Draws the draft.</summary>
public sealed class DrawFromSalesOrderCommandHandler : ICommandHandler<DrawFromSalesOrderCommand, Guid>
{
    private readonly ISalesOrderDirectory _orders;
    private readonly IBillingPartyDirectory _customers;
    private readonly IInvoiceRepository _invoices;
    private readonly IInvoicingUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;
    private readonly InvoicingOptions _options;

    /// <summary>Initializes the handler.</summary>
    public DrawFromSalesOrderCommandHandler(
        ISalesOrderDirectory orders,
        IBillingPartyDirectory customers,
        IInvoiceRepository invoices,
        IInvoicingUnitOfWork unitOfWork,
        IDateTimeProvider clock,
        InvoicingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _orders = orders;
        _customers = customers;
        _invoices = invoices;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _options = options;
    }

    /// <inheritdoc />
    public async Task<Result<Guid>> HandleAsync(
        DrawFromSalesOrderCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        BillableOrder? order = await _orders
            .GetBillableAsync(request.SalesOrderId, cancellationToken)
            .ConfigureAwait(false);

        if (order is null)
        {
            return Result.Failure<Guid>(
                InvoicingErrors.FromOrder.OrderNotFound(request.SalesOrderId.ToString()));
        }

        if (!order.CanInvoice)
        {
            return Result.Failure<Guid>(ExplainRefusal(order));
        }

        BillingParty? customer = await _customers
            .GetAsync(order.CustomerId, cancellationToken)
            .ConfigureAwait(false);

        if (customer is null)
        {
            return Result.Failure<Guid>(
                InvoicingErrors.FromOrder.CustomerNotFound(order.CustomerId.ToString()));
        }

        // A counter sale is paid as it is handed over, which is an invoice-receipt; an account
        // order is invoiced and settled later, which is an invoice. The distinction is one field
        // wide in Sales and two letters wide on the document, and getting it wrong misreports
        // every counter sale in the SAF-T file as an unpaid receivable.
        DocumentType type = string.Equals(order.Kind, "CounterSale", StringComparison.OrdinalIgnoreCase)
            ? DocumentType.InvoiceReceipt
            : DocumentType.Invoice;

        if (type == DocumentType.Invoice && string.IsNullOrWhiteSpace(customer.TaxNumber))
        {
            return Result.Failure<Guid>(InvoicingErrors.FromOrder.CustomerHasNoTaxNumber);
        }

        Result<Invoice> draft = Invoice.Draft(
            type,
            new CustomerRef(customer.PartnerId),
            customer.LegalName,
            customer.TaxNumber,
            customer.TaxCountryCode,
            Currency.FromCode(order.CurrencyCode),
            _options.TaxRegion,
            request.DocumentDate ?? _clock.TodayUtc,
            new SalesOrderRef(order.SalesOrderId));

        if (draft.IsFailure)
        {
            return Result.Failure<Guid>(draft.Error);
        }

        Invoice invoice = draft.Value;

        foreach (BillableOrderLine line in order.Lines)
        {
            Result added = AddLine(invoice, line);

            if (added.IsFailure)
            {
                // The whole document fails on one bad line, and nothing is saved. A draft missing
                // a line is worse than no draft: it is a plausible-looking document that undercharges
                // the customer, and the line it is missing is the one somebody would have to
                // notice was gone.
                return Result.Failure<Guid>(added.Error);
            }
        }

        _invoices.Add(invoice);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return invoice.Id.Value;
    }

    private static Error ExplainRefusal(BillableOrder order)
    {
        if (order.InvoiceDocumentNumber is { Length: > 0 } number)
        {
            return InvoicingErrors.FromOrder.AlreadyInvoiced(number);
        }

        return string.Equals(order.Status, "Cancelled", StringComparison.OrdinalIgnoreCase)
            ? InvoicingErrors.FromOrder.OrderCancelled
            : InvoicingErrors.FromOrder.NotDispatched;
    }

    private Result AddLine(Invoice invoice, BillableOrderLine line)
    {
        // The rate comes back as a bare percentage because that is all an order needs. Which
        // category it belongs to depends on the region this establishment invoices from, so the
        // translation happens here and not in Sales.
        Result<VatRate> rate = PortugueseVatRates.FromPercent(_options.TaxRegion, line.VatRatePercent);

        if (rate.IsFailure)
        {
            return rate.Error;
        }

        Result<Quantity> quantity = Quantity.Create(
            line.Quantity, UnitOfMeasure.FromCode(line.UnitCode));

        if (quantity.IsFailure)
        {
            return quantity.Error;
        }

        // The SKU and description come from the order, not from the catalogue as it stands today.
        // The order recorded them when the customer agreed to buy, and that is what the document
        // is a record of — a part renamed between the sale and the invoice keeps the name it was
        // sold under.
        Result<InvoiceLineId> added = invoice.AddLine(
            new PartRef(line.PartId),
            line.Sku,
            line.Description,
            quantity.Value,
            Money.Of(line.UnitPrice, invoice.Currency),
            line.DiscountPercent,
            rate.Value);

        return added.IsFailure ? Result.Failure(added.Error) : Result.Success();
    }
}
