using System.Globalization;
using AutoPartsErp.Modules.Purchasing.Domain;
using AutoPartsErp.Modules.Purchasing.Domain.Orders;
using AutoPartsErp.Modules.Purchasing.Domain.Replenishment;
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

    /// <summary>Identifies this run of numbers in the module's counter table.</summary>
    private const string NumberKey = "purchase-order";

    private readonly PurchasingDbContext _context;

    /// <summary>Initializes the repository.</summary>
    public PurchaseOrderRepository(PurchasingDbContext context)
    {
        _context = context;
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
        // This used to read the highest number already taken and add one, which is unique only
        // while no two people raise an order at the same moment. When they do, both read the same
        // highest number and the supplier receives two different orders quoting one reference.
        //
        // The counter increments in the database, in one statement, so there is no window between
        // a read and a write for the second caller to arrive in.
        int next = await _context
            .TakeNextNumberAsync(NumberKey, year, cancellationToken)
            .ConfigureAwait(false);

        return string.Create(CultureInfo.InvariantCulture, $"{NumberPrefix}-{year}-{next:D5}");
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
