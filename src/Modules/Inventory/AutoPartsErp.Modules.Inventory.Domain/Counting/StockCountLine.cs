using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Inventory.Domain.Counting;

/// <summary>
/// One part on a count sheet: what the system said, what somebody found, and the gap.
/// </summary>
public sealed class StockCountLine : Entity<StockCountLineId>
{
    /// <summary>Longest permitted SKU snapshot.</summary>
    public const int MaxSkuLength = 40;

    /// <summary>Longest permitted description snapshot.</summary>
    public const int MaxDescriptionLength = 200;

    private StockCountLine(
        StockCountLineId id,
        int lineNumber,
        PartRef partId,
        string sku,
        string description,
        Quantity systemQuantity)
        : base(id)
    {
        LineNumber = lineNumber;
        PartId = partId;
        Sku = sku;
        Description = description;
        SystemQuantity = systemQuantity;
    }

    /// <summary>Required by EF Core materialization.</summary>
#pragma warning disable CS8618
    private StockCountLine()
    {
    }
#pragma warning restore CS8618

    /// <summary>Its position on the sheet, so a printed sheet and the screen agree.</summary>
    public int LineNumber { get; private set; }

    /// <summary>The part being counted.</summary>
    public PartRef PartId { get; private set; }

    /// <summary>Its SKU when the sheet was opened.</summary>
    public string Sku { get; private set; } = string.Empty;

    /// <summary>Its description when the sheet was opened.</summary>
    public string Description { get; private set; } = string.Empty;

    /// <summary>
    /// What the system believed was on the shelf when the sheet was opened.
    /// <para>
    /// Frozen at that moment and never refreshed. Refreshing it would destroy the only evidence
    /// that separates "the count was wrong" from "stock moved while we were counting", and those
    /// are different problems with different people to talk to.
    /// </para>
    /// </summary>
    public Quantity SystemQuantity { get; private set; } = null!;

    /// <summary>
    /// What somebody found, or null while nobody has been to this shelf.
    /// <para>
    /// Nullable on purpose, and it is the single most important nullable in this aggregate. Zero
    /// means the shelf was empty; null means nobody looked. A design that used zero for both
    /// would write off every part the counter did not reach before going home.
    /// </para>
    /// </summary>
    public Quantity? CountedQuantity { get; private set; }

    /// <summary>Who counted it.</summary>
    public string? CountedBy { get; private set; }

    /// <summary>When they counted it.</summary>
    public DateTimeOffset? CountedAtUtc { get; private set; }

    /// <summary>True once somebody has actually been to this shelf.</summary>
    public bool IsCounted => CountedQuantity is not null;

    /// <summary>
    /// Counted less system, or null while uncounted. Positive means more was found than expected.
    /// </summary>
    public Quantity? Variance =>
        CountedQuantity is null ? null : CountedQuantity.Subtract(SystemQuantity);

    /// <summary>True when a counted line disagrees with what the system expected.</summary>
    public bool HasVariance => Variance is { } variance && variance.Value != 0m;

    /// <summary>Creates a line with the system's figure snapshotted.</summary>
    internal static StockCountLine Create(
        int lineNumber,
        PartRef partId,
        string sku,
        string description,
        Quantity systemQuantity) =>
        new(
            StockCountLineId.New(),
            lineNumber,
            partId,
            Truncate(sku, MaxSkuLength),
            Truncate(description, MaxDescriptionLength),
            systemQuantity);

    /// <summary>
    /// Records what was found. Counting a line twice replaces the earlier figure — a recount is
    /// the normal response to a surprising number, and the second walk is the one to believe.
    /// </summary>
    internal Result Record(Quantity countedQuantity, string countedBy, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(countedQuantity);

        if (countedQuantity.Value < 0m)
        {
            return InventoryErrors.Stock.CountCannotBeNegative;
        }

        if (countedQuantity.Unit != SystemQuantity.Unit)
        {
            return InventoryErrors.Count.UnitMismatch(Sku, SystemQuantity.Unit.Code);
        }

        CountedQuantity = countedQuantity.Copy();
        CountedBy = string.IsNullOrWhiteSpace(countedBy) ? null : countedBy.Trim();
        CountedAtUtc = now;

        return Result.Success();
    }

    private static string Truncate(string? value, int maxLength)
    {
        string trimmed = value?.Trim() ?? string.Empty;

        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
