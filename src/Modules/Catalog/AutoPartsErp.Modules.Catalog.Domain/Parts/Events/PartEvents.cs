using AutoPartsErp.SharedKernel.Primitives;

namespace AutoPartsErp.Modules.Catalog.Domain.Parts.Events;

/// <summary>Raised when a new part is registered in the catalogue.</summary>
/// <param name="PartId">The new part.</param>
/// <param name="Sku">Its stock keeping unit.</param>
/// <param name="BrandId">The brand it belongs to.</param>
public sealed record PartCreatedDomainEvent(PartId PartId, string Sku, BrandId BrandId) : DomainEvent;

/// <summary>
/// Raised when a part becomes sellable. Inventory listens for this to create stock records,
/// and Pricing to require a price before the part reaches the counter.
/// </summary>
/// <param name="PartId">The part.</param>
/// <param name="Sku">Its stock keeping unit.</param>
/// <param name="StockUnitCode">The unit stock is counted in, carried so Inventory need not ask.</param>
public sealed record PartActivatedDomainEvent(
    PartId PartId,
    string Sku,
    string StockUnitCode) : DomainEvent;

/// <summary>
/// Raised when a part is withdrawn from purchasing. Purchasing stops reordering it and
/// Sales starts offering the superseding part instead.
/// </summary>
/// <param name="PartId">The part.</param>
/// <param name="SupersededBy">The replacement part, when one exists.</param>
public sealed record PartDiscontinuedDomainEvent(PartId PartId, PartId? SupersededBy) : DomainEvent;

/// <summary>Raised when a part is retired completely.</summary>
/// <param name="PartId">The part.</param>
public sealed record PartObsoletedDomainEvent(PartId PartId) : DomainEvent;

/// <summary>
/// Raised when a new foreign number is linked to a part, so search indexes can be updated.
/// </summary>
/// <param name="PartId">The part.</param>
/// <param name="Kind">Why the numbers are linked.</param>
/// <param name="NormalizedNumber">The searchable form of the foreign number.</param>
public sealed record PartCrossReferenceAddedDomainEvent(
    PartId PartId,
    CrossReferenceKind Kind,
    string NormalizedNumber) : DomainEvent;

/// <summary>Raised when a vehicle application is added to a part.</summary>
/// <param name="PartId">The part.</param>
/// <param name="Make">Vehicle manufacturer.</param>
/// <param name="Model">Model designation.</param>
/// <param name="YearFrom">First model year covered.</param>
/// <param name="YearTo">Last model year covered.</param>
public sealed record PartFitmentAddedDomainEvent(
    PartId PartId,
    string Make,
    string Model,
    int YearFrom,
    int YearTo) : DomainEvent;

/// <summary>
/// Raised when the stocking unit changes. Inventory must react: quantities already on hand
/// mean something different afterwards.
/// </summary>
/// <param name="PartId">The part.</param>
/// <param name="PreviousUnitCode">The unit before the change.</param>
/// <param name="NewUnitCode">The unit after the change.</param>
public sealed record PartStockUnitChangedDomainEvent(
    PartId PartId,
    string PreviousUnitCode,
    string NewUnitCode) : DomainEvent;
