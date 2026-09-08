using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Inventory.Domain.Transfers;

/// <summary>
/// One part on a transfer: how much was asked for, how much left, how much arrived, and what the
/// part still on the van is worth.
/// </summary>
public sealed class StockTransferLine : Entity<StockTransferLineId>
{
    /// <summary>Longest permitted SKU snapshot.</summary>
    public const int MaxSkuLength = 40;

    /// <summary>Longest permitted description snapshot.</summary>
    public const int MaxDescriptionLength = 200;

    private StockTransferLine(
        StockTransferLineId id,
        int lineNumber,
        PartRef partId,
        string sku,
        string description,
        Quantity quantity)
        : base(id)
    {
        LineNumber = lineNumber;
        PartId = partId;
        Sku = sku;
        Description = description;
        Quantity = quantity;
        DispatchedQuantity = Quantity.Zero(quantity.Unit);
        ReceivedQuantity = Quantity.Zero(quantity.Unit);
        LostQuantity = Quantity.Zero(quantity.Unit);
    }

    /// <summary>Required by EF Core materialization.</summary>
#pragma warning disable CS8618
    private StockTransferLine()
    {
    }
#pragma warning restore CS8618

    /// <summary>Its position on the note.</summary>
    public int LineNumber { get; private set; }

    /// <summary>The part being moved.</summary>
    public PartRef PartId { get; private set; }

    /// <summary>Its SKU when the transfer was raised.</summary>
    public string Sku { get; private set; } = string.Empty;

    /// <summary>Its description when the transfer was raised.</summary>
    public string Description { get; private set; } = string.Empty;

    /// <summary>How much was asked for.</summary>
    public Quantity Quantity { get; private set; } = null!;

    /// <summary>How much actually left the sending warehouse.</summary>
    public Quantity DispatchedQuantity { get; private set; } = null!;

    /// <summary>How much has been booked in at the receiving warehouse.</summary>
    public Quantity ReceivedQuantity { get; private set; } = null!;

    /// <summary>How much was written off as never having arrived.</summary>
    public Quantity LostQuantity { get; private set; } = null!;

    /// <summary>
    /// What the stock still on the van is worth.
    /// <para>
    /// Null rather than zero when the sending shelf had no value of its own — stock that has never
    /// been through a priced receipt. Zero would say the goods are worthless, which is a claim;
    /// null says nobody ever knew what they cost, which is the truth.
    /// </para>
    /// </summary>
    public Money? ValueInTransit { get; private set; }

    /// <summary>What left and has neither arrived nor been written off.</summary>
    public Quantity InTransitQuantity =>
        DispatchedQuantity.Subtract(ReceivedQuantity).Subtract(LostQuantity);

    /// <summary>True once everything that left has been accounted for.</summary>
    public bool IsSettled => InTransitQuantity.Value <= 0m;

    /// <summary>Creates a line.</summary>
    internal static StockTransferLine Create(
        int lineNumber,
        PartRef partId,
        string sku,
        string description,
        Quantity quantity) =>
        new(
            StockTransferLineId.New(),
            lineNumber,
            partId,
            Truncate(sku, MaxSkuLength),
            Truncate(description, MaxDescriptionLength),
            quantity);

    /// <summary>Renumbers the line after one before it was removed from the draft.</summary>
    internal void Renumber(int lineNumber) => LineNumber = lineNumber;

    /// <summary>Records that the whole line left, carrying the value it was worth.</summary>
    internal void Dispatch(Money? value)
    {
        DispatchedQuantity = Quantity.Copy();
        ValueInTransit = value?.Copy();
    }

    /// <summary>
    /// Books in an arrival and answers the share of the transit value that came with it.
    /// <para>
    /// The share is proportional, except for the last of it, which takes whatever is left. Same
    /// rule as emptying a shelf, and for the same reason: proportions round, and rounding would
    /// leave a few cents in transit against a van that has been unloaded.
    /// </para>
    /// </summary>
    internal Result<Money?> Receive(Quantity quantity)
    {
        ArgumentNullException.ThrowIfNull(quantity);

        if (quantity.Unit != Quantity.Unit)
        {
            return Result.Failure<Money?>(
                InventoryErrors.Transfer.UnitMismatch(Sku, Quantity.Unit.Code));
        }

        if (quantity.Value <= 0m)
        {
            return Result.Failure<Money?>(InventoryErrors.Stock.QuantityMustBePositive);
        }

        Quantity inTransit = InTransitQuantity;

        if (quantity > inTransit)
        {
            return Result.Failure<Money?>(
                InventoryErrors.Transfer.OverReceived(Sku, inTransit.Value));
        }

        Money? arrived = TakeValue(quantity, inTransit);
        ReceivedQuantity = ReceivedQuantity.Add(quantity);

        return arrived;
    }

    /// <summary>Writes off whatever is still on the van, and answers what it was worth.</summary>
    internal Money? WriteOffInTransit()
    {
        Quantity inTransit = InTransitQuantity;

        if (inTransit.Value <= 0m)
        {
            return null;
        }

        Money? lost = TakeValue(inTransit, inTransit);
        LostQuantity = LostQuantity.Add(inTransit);

        return lost;
    }

    private Money? TakeValue(Quantity quantity, Quantity inTransit)
    {
        if (ValueInTransit is not { } value || value.IsZero || inTransit.Value <= 0m)
        {
            return null;
        }

        if (quantity.Value >= inTransit.Value)
        {
            ValueInTransit = Money.Zero(value.Currency);

            return value;
        }

        Money share = value.Multiply(quantity.Value / inTransit.Value);
        ValueInTransit = value.Subtract(share);

        return share;
    }

    private static string Truncate(string? value, int maxLength)
    {
        string trimmed = value?.Trim() ?? string.Empty;

        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
