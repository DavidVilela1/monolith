using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Inventory.Domain.Stock;

/// <summary>
/// What caused a stock movement: which document, and which line of it.
/// <para>
/// Every movement must be traceable to a business event. Six months later, "why does the system
/// think we have three of these?" has to be answerable, and the answer is a chain of references
/// back to a goods receipt, a sales order or a named person's stock count. Untraceable
/// adjustments are how stock figures stop being believed.
/// </para>
/// </summary>
public sealed class MovementReference : ValueObject
{
    /// <summary>Longest permitted document number.</summary>
    public const int MaxNumberLength = 40;

    private MovementReference(ReferenceType type, string number, string? note)
    {
        Type = type;
        Number = number;
        Note = note;
    }

    /// <summary>Required by EF Core materialization.</summary>
#pragma warning disable CS8618
    private MovementReference()
    {
    }
#pragma warning restore CS8618

    /// <summary>What kind of document this is.</summary>
    public ReferenceType Type { get; }

    /// <summary>The document number, e.g. GRN-2026-00042.</summary>
    public string Number { get; } = string.Empty;

    /// <summary>Free text for the cases a number cannot explain, such as a damage write-off.</summary>
    public string? Note { get; }

    /// <summary>Creates a reference.</summary>
    /// <param name="type">What kind of document caused the movement.</param>
    /// <param name="number">The document number.</param>
    /// <param name="note">Optional explanation.</param>
    public static Result<MovementReference> Create(ReferenceType type, string? number, string? note = null)
    {
        if (type == ReferenceType.Unknown)
        {
            return InventoryErrors.Movement.ReferenceTypeRequired;
        }

        if (string.IsNullOrWhiteSpace(number))
        {
            return InventoryErrors.Movement.ReferenceNumberRequired;
        }

        string trimmed = number.Trim().ToUpperInvariant();

        return trimmed.Length > MaxNumberLength
            ? InventoryErrors.Movement.ReferenceNumberTooLong
            : new MovementReference(type, trimmed, string.IsNullOrWhiteSpace(note) ? null : note.Trim());
    }

    /// <summary>Rehydrates a reference already known to be valid.</summary>
    public static MovementReference FromStorage(ReferenceType type, string number, string? note) =>
        new(type, number, note);

    /// <inheritdoc />
    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Type;
        yield return Number;
    }

    /// <inheritdoc />
    public override string ToString() => $"{Type}:{Number}";
}

/// <summary>The kind of business document behind a movement or reservation.</summary>
public enum ReferenceType
{
    /// <summary>Unspecified. Never persisted.</summary>
    Unknown = 0,

    /// <summary>Goods received note.</summary>
    GoodsReceipt = 1,

    /// <summary>Purchase order, for expected stock.</summary>
    PurchaseOrder = 2,

    /// <summary>Sales order.</summary>
    SalesOrder = 3,

    /// <summary>A counter sale taken and picked on the spot.</summary>
    CounterSale = 4,

    /// <summary>A quote holding stock while the customer decides.</summary>
    Quote = 5,

    /// <summary>A customer return coming back in.</summary>
    CustomerReturn = 6,

    /// <summary>A return going back to the supplier.</summary>
    SupplierReturn = 7,

    /// <summary>Movement between two warehouses.</summary>
    StockTransfer = 8,

    /// <summary>A counted correction.</summary>
    StockCount = 9,

    /// <summary>A manual correction, which always needs a note.</summary>
    Adjustment = 10,

    /// <summary>A returnable core coming back from a customer.</summary>
    CoreReturn = 11,
}
