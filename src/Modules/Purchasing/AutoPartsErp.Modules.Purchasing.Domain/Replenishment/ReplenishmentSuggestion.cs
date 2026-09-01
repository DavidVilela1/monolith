using AutoPartsErp.Modules.Purchasing.Domain.Replenishment.Events;
using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Purchasing.Domain.Replenishment;

/// <summary>
/// A standing note that a part has run low somewhere and probably needs buying.
/// <para>
/// This is what Inventory's reorder-point signal turns into. It is deliberately not a purchase
/// order: nobody wants a system that rings up a supplier on its own. A buyer looks at the list,
/// decides which suggestions are real, and raises one order covering several of them — which is
/// also how you avoid four separate £30 orders to the same supplier in one week.
/// </para>
/// <para>
/// There is at most one open suggestion per part per warehouse. Stock crossing the reorder point
/// repeatedly — which it will, every time something is picked — updates the existing suggestion
/// rather than adding to a pile of duplicates. That is what makes the handler safe to run twice.
/// </para>
/// </summary>
public sealed class ReplenishmentSuggestion
    : AggregateRoot<SuggestionId>, IAuditable, ITenantScoped
{
    /// <summary>Longest permitted dismissal reason.</summary>
    public const int MaxReasonLength = 300;

    private ReplenishmentSuggestion(
        SuggestionId id,
        PartRef partId,
        WarehouseRef warehouseId,
        decimal quantityAvailable,
        decimal reorderPoint,
        decimal suggestedQuantity,
        DateTimeOffset raisedAtUtc)
        : base(id)
    {
        PartId = partId;
        WarehouseId = warehouseId;
        QuantityAvailable = quantityAvailable;
        ReorderPoint = reorderPoint;
        SuggestedQuantity = suggestedQuantity;
        RaisedAtUtc = raisedAtUtc;
        LastSeenAtUtc = raisedAtUtc;
        Status = SuggestionStatus.Open;
        CreatedBy = string.Empty;
    }

    /// <summary>Required by EF Core materialization.</summary>
#pragma warning disable CS8618
    private ReplenishmentSuggestion()
    {
    }
#pragma warning restore CS8618

    /// <summary>The part that ran low.</summary>
    public PartRef PartId { get; private set; }

    /// <summary>Where it ran low.</summary>
    public WarehouseRef WarehouseId { get; private set; }

    /// <summary>
    /// What was left that was not already spoken for, when the suggestion was last refreshed.
    /// <para>
    /// A bare decimal rather than a <c>Quantity</c>, because the signal that produced it carries
    /// no unit. Inventory owns the unit a part is counted in; a suggestion is a prompt for a
    /// human, not a ledger entry, and inventing a unit here to make the type look tidier would
    /// mean asserting something this module does not actually know.
    /// </para>
    /// </summary>
    public decimal QuantityAvailable { get; private set; }

    /// <summary>The level that triggered the suggestion.</summary>
    public decimal ReorderPoint { get; private set; }

    /// <summary>How much to order, as Inventory suggested it.</summary>
    public decimal SuggestedQuantity { get; private set; }

    /// <summary>Whether the buyer has dealt with it yet.</summary>
    public SuggestionStatus Status { get; private set; }

    /// <summary>When it first appeared.</summary>
    public DateTimeOffset RaisedAtUtc { get; private set; }

    /// <summary>When it was last confirmed to still be true.</summary>
    public DateTimeOffset LastSeenAtUtc { get; private set; }

    /// <summary>The order it was rolled into, once it was.</summary>
    public PurchaseOrderId? PurchaseOrderId { get; private set; }

    /// <summary>Why the buyer decided not to act on it.</summary>
    public string? DismissedReason { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <inheritdoc />
    public string CreatedBy { get; set; } = string.Empty;

    /// <inheritdoc />
    public DateTimeOffset? ModifiedAtUtc { get; set; }

    /// <inheritdoc />
    public string? ModifiedBy { get; set; }

    /// <summary>True while the buyer has not yet dealt with it.</summary>
    public bool IsOpen => Status == SuggestionStatus.Open;

    /// <summary>How far below the trigger level the part has fallen.</summary>
    public decimal Shortfall => ReorderPoint - QuantityAvailable;

    /// <summary>Opens a suggestion for a part in a warehouse.</summary>
    /// <param name="partId">The part that ran low.</param>
    /// <param name="warehouseId">Where it ran low.</param>
    /// <param name="quantityAvailable">What is left that is not already spoken for.</param>
    /// <param name="reorderPoint">The level that triggered it.</param>
    /// <param name="suggestedQuantity">How much to order.</param>
    /// <param name="raisedAtUtc">When the signal arrived.</param>
    public static Result<ReplenishmentSuggestion> Open(
        PartRef partId,
        WarehouseRef warehouseId,
        decimal quantityAvailable,
        decimal reorderPoint,
        decimal suggestedQuantity,
        DateTimeOffset raisedAtUtc)
    {
        if (partId.IsEmpty)
        {
            return PurchasingErrors.Suggestion.PartRequired;
        }

        if (warehouseId.IsEmpty)
        {
            return PurchasingErrors.Suggestion.WarehouseRequired;
        }

        if (suggestedQuantity <= 0m)
        {
            return PurchasingErrors.Suggestion.QuantityNotPositive;
        }

        var suggestion = new ReplenishmentSuggestion(
            SuggestionId.New(),
            partId,
            warehouseId,
            quantityAvailable,
            reorderPoint,
            suggestedQuantity,
            raisedAtUtc);

        suggestion.Raise(new ReplenishmentSuggestedDomainEvent(
            suggestion.Id, partId, warehouseId, suggestedQuantity));

        return suggestion;
    }

    /// <summary>
    /// Updates an open suggestion with a fresher reading rather than creating a second one.
    /// <para>
    /// No domain event: the buyer has already been told this part needs attention, and telling
    /// them again every time somebody picks one off the shelf is how a useful signal becomes
    /// noise that gets ignored.
    /// </para>
    /// </summary>
    /// <param name="quantityAvailable">What is left now.</param>
    /// <param name="reorderPoint">The trigger level now.</param>
    /// <param name="suggestedQuantity">How much to order now.</param>
    /// <param name="seenAtUtc">When this reading was taken.</param>
    public Result Refresh(
        decimal quantityAvailable,
        decimal reorderPoint,
        decimal suggestedQuantity,
        DateTimeOffset seenAtUtc)
    {
        if (!IsOpen)
        {
            return PurchasingErrors.Suggestion.NotOpen;
        }

        if (suggestedQuantity <= 0m)
        {
            return PurchasingErrors.Suggestion.QuantityNotPositive;
        }

        QuantityAvailable = quantityAvailable;
        ReorderPoint = reorderPoint;
        SuggestedQuantity = suggestedQuantity;
        LastSeenAtUtc = seenAtUtc;

        return Result.Success();
    }

    /// <summary>Records that the suggestion was rolled into a purchase order.</summary>
    /// <param name="purchaseOrderId">The order it went onto.</param>
    public Result MarkOrdered(PurchaseOrderId purchaseOrderId)
    {
        if (!IsOpen)
        {
            return PurchasingErrors.Suggestion.NotOpen;
        }

        Status = SuggestionStatus.Ordered;
        PurchaseOrderId = purchaseOrderId;

        Raise(new ReplenishmentOrderedDomainEvent(Id, purchaseOrderId));

        return Result.Success();
    }

    /// <summary>
    /// Takes the suggestion off the list without buying anything — the part is being run down,
    /// or there is stock in another branch, or the reorder point is simply wrong.
    /// </summary>
    /// <param name="reason">Why, so the next person does not raise it again.</param>
    public Result Dismiss(string? reason)
    {
        if (!IsOpen)
        {
            return PurchasingErrors.Suggestion.NotOpen;
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return PurchasingErrors.Suggestion.DismissReasonRequired;
        }

        string trimmed = reason.Trim();
        if (trimmed.Length > MaxReasonLength)
        {
            trimmed = trimmed[..MaxReasonLength];
        }

        Status = SuggestionStatus.Dismissed;
        DismissedReason = trimmed;

        Raise(new ReplenishmentDismissedDomainEvent(Id, trimmed));

        return Result.Success();
    }
}

/// <summary>Whether a buyer has dealt with a replenishment suggestion.</summary>
public enum SuggestionStatus
{
    /// <summary>Unspecified. Never persisted.</summary>
    Unknown = 0,

    /// <summary>Waiting for a buyer to look at it.</summary>
    Open = 1,

    /// <summary>Rolled into a purchase order.</summary>
    Ordered = 2,

    /// <summary>Deliberately not acted on.</summary>
    Dismissed = 3,
}
