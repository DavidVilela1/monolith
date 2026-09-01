using System.Globalization;
using AutoPartsErp.Modules.Purchasing.Domain;
using AutoPartsErp.Modules.Purchasing.Domain.Orders;
using AutoPartsErp.Modules.Purchasing.Domain.Replenishment;
using AutoPartsErp.SharedKernel.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace AutoPartsErp.Modules.Purchasing.Infrastructure.Persistence.Repositories;

/// <summary>
/// Write-side access to purchase orders.
/// <para>
/// No <c>Include</c> for the lines anywhere in here: they are an owned collection, so EF loads
/// them with their order automatically. An aggregate you can accidentally load half of is not
/// an aggregate.
/// </para>
/// </summary>
public sealed class PurchaseOrderRepository : IPurchaseOrderRepository
{
    private const string NumberPrefix = "PO";

    private readonly PurchasingDbContext _context;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initializes the repository.</summary>
    public PurchaseOrderRepository(PurchasingDbContext context, ITenantContext tenantContext)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public Task<PurchaseOrder?> GetByIdAsync(
        PurchaseOrderId id,
        CancellationToken cancellationToken = default) =>
        _context.PurchaseOrders.FirstOrDefaultAsync(order => order.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsAsync(PurchaseOrderId id, CancellationToken cancellationToken = default) =>
        _context.PurchaseOrders.AnyAsync(order => order.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<PurchaseOrder?> GetByNumberAsync(
        string orderNumber,
        CancellationToken cancellationToken = default)
    {
        string normalized = orderNumber?.Trim().ToUpperInvariant() ?? string.Empty;

        return _context.PurchaseOrders.FirstOrDefaultAsync(
            order => order.OrderNumber == normalized, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<string> NextOrderNumberAsync(int year, CancellationToken cancellationToken = default)
    {
        string prefix = string.Create(CultureInfo.InvariantCulture, $"{NumberPrefix}-{year}-");

        // Query filters are ignored on purpose and the tenant is applied by hand: a soft-deleted
        // order still owns its number, and reissuing it would put two different documents in the
        // supplier's inbox with the same reference.
        //
        // Numbers are zero-padded, so ordering them as text orders them numerically - which is
        // what lets this read one row instead of every order raised this year.
        string? last = await _context.PurchaseOrders
            .IgnoreQueryFilters()
            .Where(order => order.TenantId == _tenantContext.TenantId)
            .Where(order => EF.Functions.Like(order.OrderNumber, prefix + "%"))
            .OrderByDescending(order => order.OrderNumber)
            .Select(order => order.OrderNumber)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        int next = 1;

        if (last is not null && last.Length > prefix.Length)
        {
            string suffix = last[prefix.Length..];
            if (int.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed))
            {
                next = parsed + 1;
            }
        }

        return string.Create(CultureInfo.InvariantCulture, $"{prefix}{next:D5}");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PurchaseOrder>> GetOpenForSupplierAsync(
        SupplierRef supplierId,
        CancellationToken cancellationToken = default)
    {
        List<PurchaseOrder> orders = await _context.PurchaseOrders
            .Where(order => order.SupplierId == supplierId)
            .Where(order => order.Status == PurchaseOrderStatus.Submitted
                || order.Status == PurchaseOrderStatus.Confirmed
                || order.Status == PurchaseOrderStatus.PartiallyReceived)
            .OrderBy(order => order.OrderNumber)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return orders;
    }

    /// <inheritdoc />
    public void Add(PurchaseOrder aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.PurchaseOrders.Add(aggregate);
    }

    /// <inheritdoc />
    public void Remove(PurchaseOrder aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.PurchaseOrders.Remove(aggregate);
    }
}

/// <summary>Write-side access to replenishment suggestions.</summary>
public sealed class ReplenishmentSuggestionRepository : IReplenishmentSuggestionRepository
{
    private readonly PurchasingDbContext _context;

    /// <summary>Initializes the repository.</summary>
    public ReplenishmentSuggestionRepository(PurchasingDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public Task<ReplenishmentSuggestion?> GetByIdAsync(
        SuggestionId id,
        CancellationToken cancellationToken = default) =>
        _context.ReplenishmentSuggestions.FirstOrDefaultAsync(
            suggestion => suggestion.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsAsync(SuggestionId id, CancellationToken cancellationToken = default) =>
        _context.ReplenishmentSuggestions.AnyAsync(
            suggestion => suggestion.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<ReplenishmentSuggestion?> GetOpenForAsync(
        PartRef partId,
        WarehouseRef warehouseId,
        CancellationToken cancellationToken = default) =>
        _context.ReplenishmentSuggestions.FirstOrDefaultAsync(
            suggestion => suggestion.PartId == partId
                && suggestion.WarehouseId == warehouseId
                && suggestion.Status == SuggestionStatus.Open,
            cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReplenishmentSuggestion>> GetOpenForPartAsync(
        PartRef partId,
        CancellationToken cancellationToken = default)
    {
        List<ReplenishmentSuggestion> suggestions = await _context.ReplenishmentSuggestions
            .Where(suggestion => suggestion.PartId == partId
                && suggestion.Status == SuggestionStatus.Open)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return suggestions;
    }

    /// <inheritdoc />
    public void Add(ReplenishmentSuggestion aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.ReplenishmentSuggestions.Add(aggregate);
    }

    /// <inheritdoc />
    public void Remove(ReplenishmentSuggestion aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.ReplenishmentSuggestions.Remove(aggregate);
    }
}
