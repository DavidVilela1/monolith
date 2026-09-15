using System.Globalization;
using AutoPartsErp.Modules.Purchasing.Domain;
using AutoPartsErp.Modules.Purchasing.Domain.Agreements;
using AutoPartsErp.Modules.Purchasing.Domain.Invoices;
using Microsoft.EntityFrameworkCore;

namespace AutoPartsErp.Modules.Purchasing.Infrastructure.Persistence.Repositories;

/// <summary>
/// Write-side access to what was agreed with a supplier.
/// <para>
/// No <c>Include</c> for the rebate steps: they are an owned collection, so EF loads them with the
/// agreement. An aggregate you can accidentally load half of is not an aggregate — and half a
/// rebate scale is a rate somebody would act on.
/// </para>
/// </summary>
public sealed class SupplierAgreementRepository : ISupplierAgreementRepository
{
    private readonly PurchasingDbContext _context;

    /// <summary>Initializes the repository.</summary>
    public SupplierAgreementRepository(PurchasingDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public Task<SupplierAgreement?> GetByIdAsync(
        SupplierAgreementId id,
        CancellationToken cancellationToken = default) =>
        _context.SupplierAgreements.FirstOrDefaultAsync(
            agreement => agreement.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsAsync(
        SupplierAgreementId id,
        CancellationToken cancellationToken = default) =>
        _context.SupplierAgreements.AnyAsync(agreement => agreement.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<SupplierAgreement?> GetForSupplierAsync(
        SupplierRef supplierId,
        DateOnly on,
        CancellationToken cancellationToken = default) =>
        _context.SupplierAgreements
            .Where(agreement => agreement.SupplierId == supplierId)
            .Where(agreement => agreement.EffectiveFrom <= on)
            .Where(agreement => agreement.EffectiveTo == null || agreement.EffectiveTo >= on)
            .OrderByDescending(agreement => agreement.EffectiveFrom)
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public Task<bool> HasLiveAgreementAsync(
        SupplierRef supplierId,
        SupplierAgreementId? excluding = null,
        CancellationToken cancellationToken = default) =>
        _context.SupplierAgreements
            .Where(agreement => agreement.SupplierId == supplierId)
            .Where(agreement => agreement.EffectiveTo == null)
            .Where(agreement => excluding == null || agreement.Id != excluding.Value)
            .AnyAsync(cancellationToken);

    /// <inheritdoc />
    public void Add(SupplierAgreement aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.SupplierAgreements.Add(aggregate);
    }

    /// <inheritdoc />
    public void Remove(SupplierAgreement aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.SupplierAgreements.Remove(aggregate);
    }
}

/// <summary>Write-side access to what a supplier charges for a part.</summary>
public sealed class SupplierPriceRepository : ISupplierPriceRepository
{
    private readonly PurchasingDbContext _context;

    /// <summary>Initializes the repository.</summary>
    public SupplierPriceRepository(PurchasingDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public Task<SupplierPrice?> GetByIdAsync(
        SupplierPriceId id,
        CancellationToken cancellationToken = default) =>
        _context.SupplierPrices.FirstOrDefaultAsync(price => price.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsAsync(SupplierPriceId id, CancellationToken cancellationToken = default) =>
        _context.SupplierPrices.AnyAsync(price => price.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<SupplierPrice?> GetPriceOnAsync(
        SupplierRef supplierId,
        PartRef partId,
        DateOnly on,
        CancellationToken cancellationToken = default) =>
        _context.SupplierPrices
            .Where(price => price.SupplierId == supplierId && price.PartId == partId)
            .Where(price => price.EffectiveFrom <= on)
            .OrderByDescending(price => price.EffectiveFrom)
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsForAsync(
        SupplierRef supplierId,
        PartRef partId,
        DateOnly effectiveFrom,
        CancellationToken cancellationToken = default) =>
        _context.SupplierPrices.AnyAsync(
            price => price.SupplierId == supplierId
                && price.PartId == partId
                && price.EffectiveFrom == effectiveFrom,
            cancellationToken);

    /// <inheritdoc />
    public void Add(SupplierPrice aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.SupplierPrices.Add(aggregate);
    }

    /// <inheritdoc />
    public void Remove(SupplierPrice aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.SupplierPrices.Remove(aggregate);
    }
}

/// <summary>Write-side access to suppliers' own documents.</summary>
public sealed class SupplierInvoiceRepository : ISupplierInvoiceRepository
{
    private readonly PurchasingDbContext _context;

    /// <summary>Initializes the repository.</summary>
    public SupplierInvoiceRepository(PurchasingDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public Task<SupplierInvoice?> GetByIdAsync(
        SupplierInvoiceId id,
        CancellationToken cancellationToken = default) =>
        _context.SupplierInvoices.FirstOrDefaultAsync(invoice => invoice.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsAsync(SupplierInvoiceId id, CancellationToken cancellationToken = default) =>
        _context.SupplierInvoices.AnyAsync(invoice => invoice.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<SupplierInvoice?> GetOpenDraftAsync(
        SupplierRef supplierId,
        DateOnly receivedOn,
        CancellationToken cancellationToken = default) =>
        _context.SupplierInvoices.FirstOrDefaultAsync(
            invoice => invoice.SupplierId == supplierId
                && invoice.ReceivedOn == receivedOn
                && invoice.Status == SupplierInvoiceStatus.Drafted,
            cancellationToken);

    /// <inheritdoc />
    public Task<bool> HasLineForReceiptAsync(
        PurchaseOrderLineId purchaseOrderLineId,
        decimal quantity,
        CancellationToken cancellationToken = default) =>
        // The quantity is part of the question, not only the line. One order line legitimately
        // arrives twice — sixty today and forty next week — and both belong on documents. What
        // must not happen twice is the same arrival, so a redelivered event carrying the same
        // line and the same quantity finds its own earlier work and stops.
        _context.SupplierInvoices
            .Where(invoice => invoice.Status != SupplierInvoiceStatus.Cancelled)
            .SelectMany(invoice => invoice.Lines)
            .AnyAsync(
                line => line.PurchaseOrderLineId == purchaseOrderLineId
                    && line.Quantity.Value == quantity,
                cancellationToken);

    /// <inheritdoc />
    public void Add(SupplierInvoice aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.SupplierInvoices.Add(aggregate);
    }

    /// <inheritdoc />
    public void Remove(SupplierInvoice aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.SupplierInvoices.Remove(aggregate);
    }
}

/// <summary>Write-side access to what a supplier's rebate has earned so far.</summary>
public sealed class RappelAccrualRepository : IRappelAccrualRepository
{
    private readonly PurchasingDbContext _context;

    /// <summary>Initializes the repository.</summary>
    public RappelAccrualRepository(PurchasingDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public Task<RappelAccrual?> GetByIdAsync(
        RappelAccrualId id,
        CancellationToken cancellationToken = default) =>
        _context.RappelAccruals.FirstOrDefaultAsync(accrual => accrual.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsAsync(RappelAccrualId id, CancellationToken cancellationToken = default) =>
        _context.RappelAccruals.AnyAsync(accrual => accrual.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<RappelAccrual?> GetForPeriodAsync(
        SupplierRef supplierId,
        DateOnly on,
        CancellationToken cancellationToken = default) =>
        _context.RappelAccruals.FirstOrDefaultAsync(
            accrual => accrual.SupplierId == supplierId
                && accrual.PeriodFrom <= on
                && accrual.PeriodTo >= on,
            cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<RappelAccrual>> GetOutstandingAsync(
        CancellationToken cancellationToken = default)
    {
        // Filtered in the database on the same subtraction the aggregate exposes, rather than
        // loading every open period and asking each one. Written out because Outstanding is
        // computed and has no column to query.
        List<RappelAccrual> accruals = await _context.RappelAccruals
            .Where(accrual => !accrual.IsClosed)
            .Where(accrual =>
                accrual.Earned.Amount > accrual.TakenOnInvoices.Amount + accrual.Credited.Amount)
            .OrderByDescending(accrual =>
                accrual.Earned.Amount - accrual.TakenOnInvoices.Amount - accrual.Credited.Amount)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return accruals;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RappelAccrual>> GetDueForClosingAsync(
        DateOnly today,
        int take,
        CancellationToken cancellationToken = default) =>
        await _context.RappelAccruals
            .Where(accrual => !accrual.IsClosed && accrual.PeriodTo < today)
            .OrderBy(accrual => accrual.PeriodTo)
            .Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void Add(RappelAccrual aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.RappelAccruals.Add(aggregate);
    }

    /// <inheritdoc />
    public void Remove(RappelAccrual aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.RappelAccruals.Remove(aggregate);
    }
}

/// <summary>Reads and writes what suppliers owe the company for rebates.</summary>
public sealed class RappelClaimRepository : IRappelClaimRepository
{
    private const string NumberPrefix = "RC";

    private const string NumberKey = "rappel-claim";

    private readonly PurchasingDbContext _context;

    /// <summary>Initializes the repository.</summary>
    public RappelClaimRepository(PurchasingDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public Task<RappelClaim?> GetByIdAsync(
        RappelClaimId id,
        CancellationToken cancellationToken = default) =>
        _context.RappelClaims.FirstOrDefaultAsync(claim => claim.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsAsync(RappelClaimId id, CancellationToken cancellationToken = default) =>
        _context.RappelClaims.AnyAsync(claim => claim.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<RappelClaim?> GetForPeriodAsync(
        RappelAccrualId accrualId,
        CancellationToken cancellationToken = default) =>
        _context.RappelClaims.FirstOrDefaultAsync(
            claim => claim.AccrualId == accrualId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<RappelClaim>> GetOpenAsync(
        CancellationToken cancellationToken = default) =>
        await _context.RappelClaims
            .Where(claim =>
                claim.Status == RappelClaimStatus.Raised
                || claim.Status == RappelClaimStatus.Sent)
            .OrderBy(claim => claim.PeriodTo)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<string> NextClaimNumberAsync(
        int year,
        CancellationToken cancellationToken = default)
    {
        // From the database counter, in one statement, like every other run of numbers here. Two
        // claims sharing a number is a supplier meeting where nobody can tell which is which.
        int next = await _context
            .TakeNextNumberAsync(NumberKey, year, cancellationToken)
            .ConfigureAwait(false);

        return string.Create(CultureInfo.InvariantCulture, $"{NumberPrefix}-{year}-{next:D5}");
    }

    /// <inheritdoc />
    public void Add(RappelClaim aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.RappelClaims.Add(aggregate);
    }

    /// <inheritdoc />
    public void Remove(RappelClaim aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.RappelClaims.Remove(aggregate);
    }
}
