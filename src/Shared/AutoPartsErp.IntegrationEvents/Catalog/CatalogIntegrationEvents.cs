using AutoPartsErp.SharedKernel.Messaging;

namespace AutoPartsErp.IntegrationEvents.Catalog;

/// <summary>
/// A part became sellable. Inventory opens stock records for it; Pricing requires a price
/// before it reaches the counter.
/// <para>
/// The stocking unit travels with the event on purpose. Inventory has to record quantities in
/// the same unit the catalogue uses, and it must not have to call back into Catalog to find out
/// which one — an event that forces the consumer to query the publisher is not really decoupled.
/// </para>
/// </summary>
/// <param name="PartId">The part.</param>
/// <param name="Sku">Its stock keeping unit, for logs and human-readable references.</param>
/// <param name="StockUnitCode">The unit stock is counted in, e.g. EA, SET, L.</param>
/// <param name="TenantId">The owning tenant.</param>
public sealed record PartActivatedIntegrationEvent(
    Guid PartId,
    string Sku,
    string StockUnitCode,
    Guid TenantId) : IntegrationEvent;

/// <summary>
/// A part was withdrawn from purchasing. Purchasing stops reordering it; Inventory flags the
/// remaining quantity as sell-down stock rather than something to replenish.
/// </summary>
/// <param name="PartId">The part.</param>
/// <param name="SupersededByPartId">The replacement part, when the brand named one.</param>
/// <param name="TenantId">The owning tenant.</param>
public sealed record PartDiscontinuedIntegrationEvent(
    Guid PartId,
    Guid? SupersededByPartId,
    Guid TenantId) : IntegrationEvent;

/// <summary>
/// A part was retired completely. Nothing new may be stocked, bought or sold against it.
/// </summary>
/// <param name="PartId">The part.</param>
/// <param name="TenantId">The owning tenant.</param>
public sealed record PartObsoletedIntegrationEvent(Guid PartId, Guid TenantId) : IntegrationEvent;
