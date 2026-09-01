using AutoPartsErp.SharedKernel.Primitives;

namespace AutoPartsErp.Modules.Purchasing.Domain.Replenishment.Events;

/// <summary>Raised the first time a part is flagged as needing reordering in a warehouse.</summary>
/// <param name="SuggestionId">The new suggestion.</param>
/// <param name="PartId">The part that ran low.</param>
/// <param name="WarehouseId">Where it ran low.</param>
/// <param name="SuggestedQuantity">How much to order.</param>
public sealed record ReplenishmentSuggestedDomainEvent(
    SuggestionId SuggestionId,
    PartRef PartId,
    WarehouseRef WarehouseId,
    decimal SuggestedQuantity) : DomainEvent;

/// <summary>Raised when a suggestion is turned into a line on a purchase order.</summary>
/// <param name="SuggestionId">The suggestion.</param>
/// <param name="PurchaseOrderId">The order it went onto.</param>
public sealed record ReplenishmentOrderedDomainEvent(
    SuggestionId SuggestionId,
    PurchaseOrderId PurchaseOrderId) : DomainEvent;

/// <summary>Raised when a buyer decides not to act on a suggestion.</summary>
/// <param name="SuggestionId">The suggestion.</param>
/// <param name="Reason">Why it was dismissed.</param>
public sealed record ReplenishmentDismissedDomainEvent(
    SuggestionId SuggestionId,
    string Reason) : DomainEvent;
