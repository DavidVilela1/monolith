using AutoPartsErp.Modules.Purchasing.Domain;
using AutoPartsErp.Modules.Purchasing.Domain.Agreements;
using AutoPartsErp.Modules.Purchasing.Domain.Invoices;
using AutoPartsErp.Modules.Purchasing.Domain.Invoices.Events;
using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Purchasing.Application.EventHandlers;

/// <summary>
/// Counts a settled document into what the supplier's rebate has earned this period.
/// <para>
/// The half that makes the rebate visible. Until a document is counted, nobody can say what the
/// year has bought, what the scale has reached, or how much of it the company has actually had —
/// and a rebate nobody can see is a rebate nobody chases.
/// </para>
/// <para>
/// A domain event handler inside the module, so the period moves in the same transaction as the
/// document that moved it. A settled invoice with no accrual behind it is a purchase the year
/// forgot, and it would be found in January by a supplier's statement rather than by anybody here.
/// </para>
/// <para>
/// The period is opened lazily, by the first document that falls into it. A job creating every
/// period for every supplier every January would fill the table with rows for suppliers nobody
/// bought from that year.
/// </para>
/// </summary>
public sealed class AccrueRappelOnSupplierInvoiceSettled
    : IDomainEventHandler<SupplierInvoiceSettledDomainEvent>
{
    private readonly ISupplierInvoiceRepository _invoices;
    private readonly ISupplierAgreementRepository _agreements;
    private readonly IRappelAccrualRepository _accruals;

    /// <summary>Initializes the handler.</summary>
    public AccrueRappelOnSupplierInvoiceSettled(
        ISupplierInvoiceRepository invoices,
        ISupplierAgreementRepository agreements,
        IRappelAccrualRepository accruals)
    {
        _invoices = invoices;
        _agreements = agreements;
        _accruals = accruals;
    }

    /// <inheritdoc />
    public async Task HandleAsync(
        SupplierInvoiceSettledDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        SupplierAgreement? agreement = await _agreements
            .GetForSupplierAsync(domainEvent.SupplierId, domainEvent.DocumentDate, cancellationToken)
            .ConfigureAwait(false);

        // No agreement, or one with no rebate, means there is nothing to count towards. Not an
        // error: most of a distributor's suppliers do not pay a rappel at all.
        if (agreement?.Scale is not { } scale)
        {
            return;
        }

        if (agreement.PeriodFor(domainEvent.DocumentDate) is not { } period)
        {
            return;
        }

        if (agreement.CurrencyCode != domainEvent.CurrencyCode)
        {
            return;
        }

        SupplierInvoice? invoice = await _invoices
            .GetByIdAsync(domainEvent.SupplierInvoiceId, cancellationToken)
            .ConfigureAwait(false);

        if (invoice is null)
        {
            return;
        }

        RappelAccrual? accrual = await _accruals
            .GetForPeriodAsync(domainEvent.SupplierId, domainEvent.DocumentDate, cancellationToken)
            .ConfigureAwait(false);

        if (accrual is null)
        {
            Result<RappelAccrual> opened = RappelAccrual.Open(
                domainEvent.SupplierId,
                domainEvent.SupplierCode,
                agreement.RappelBasis,
                period.From,
                period.To,
                agreement.Currency);

            if (opened.IsFailure)
            {
                return;
            }

            accrual = opened.Value;
            _accruals.Add(accrual);
        }

        // The net figure, which is what the scale is written against: "a partir de 25.000 de
        // compras" means twenty-five thousand actually invoiced, not twenty-five thousand before
        // the discount the supplier themselves gave.
        accrual.Record(invoice.NetTotal, invoice.RappelAmount, scale);
    }
}
