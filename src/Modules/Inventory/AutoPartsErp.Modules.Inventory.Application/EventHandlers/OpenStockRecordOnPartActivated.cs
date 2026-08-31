using AutoPartsErp.IntegrationEvents.Catalog;
using AutoPartsErp.Modules.Inventory.Domain;
using AutoPartsErp.Modules.Inventory.Domain.Stock;
using AutoPartsErp.Modules.Inventory.Domain.Warehouses;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Inventory.Application.EventHandlers;

/// <summary>
/// Opens a zero stock balance in every active warehouse when Catalog reports a part went live.
/// <para>
/// This is the first real cross-module link in the system, and what matters is what is <i>not</i>
/// here. Inventory does not reference the Catalog module, does not call a Catalog service, and
/// does not read the Catalog schema. It receives a record with four fields and acts on it. If
/// Catalog were extracted into its own service tomorrow, this handler would not change.
/// </para>
/// <para>
/// It is idempotent because it has to be: the in-process bus can deliver twice on retry, and any
/// real broker guarantees at-least-once. Opening a balance that already exists is success, not a
/// conflict.
/// </para>
/// </summary>
public sealed class OpenStockRecordOnPartActivated : IIntegrationEventHandler<PartActivatedIntegrationEvent>
{
    private readonly IWarehouseRepository _warehouses;
    private readonly IStockItemRepository _stockItems;
    private readonly IInventoryUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public OpenStockRecordOnPartActivated(
        IWarehouseRepository warehouses,
        IStockItemRepository stockItems,
        IInventoryUnitOfWork unitOfWork)
    {
        _warehouses = warehouses;
        _stockItems = stockItems;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// The event carried a unit of measure this system does not know. Refusing is safer than
    /// guessing: a balance opened in the wrong unit is worse than no balance at all. The event
    /// bus logs the failure with the handler and event identity, and the catalogue entry needs
    /// a human to look at it.
    /// </exception>
    public async Task HandleAsync(
        PartActivatedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        if (!UnitOfMeasure.TryFromCode(integrationEvent.StockUnitCode, out UnitOfMeasure unit))
        {
            throw new InvalidOperationException(
                $"Cannot open stock for part {integrationEvent.Sku} ({integrationEvent.PartId}): " +
                $"'{integrationEvent.StockUnitCode}' is not a known unit of measure.");
        }

        var part = new PartRef(integrationEvent.PartId);

        IReadOnlyList<Warehouse> warehouses =
            await _warehouses.GetActiveAsync(cancellationToken).ConfigureAwait(false);

        int opened = 0;

        foreach (Warehouse warehouse in warehouses)
        {
            bool exists = await _stockItems
                .ExistsAsync(part, warehouse.Id, cancellationToken)
                .ConfigureAwait(false);

            if (exists)
            {
                continue;
            }

            Result<StockItem> stockItem = StockItem.Open(part, warehouse.Id, unit);

            if (stockItem.IsFailure)
            {
                throw new InvalidOperationException(
                    $"Could not open stock for part {integrationEvent.PartId} in warehouse " +
                    $"{warehouse.Code}: {stockItem.Error}");
            }

            _stockItems.Add(stockItem.Value);
            opened++;
        }

        if (opened > 0)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
