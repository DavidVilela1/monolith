using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Inventory.Domain.Stock;

/// <summary>Why stock moved.</summary>
public enum MovementType
{
    /// <summary>Unspecified. Never persisted.</summary>
    Unknown = 0,

    /// <summary>Stock came in.</summary>
    Receipt = 1,

    /// <summary>Stock went out.</summary>
    Issue = 2,

    /// <summary>A correction, usually from a count.</summary>
    Adjustment = 3,

    /// <summary>Arrived from another warehouse.</summary>
    TransferIn = 4,

    /// <summary>Left for another warehouse.</summary>
    TransferOut = 5,

    /// <summary>A customer brought something back.</summary>
    CustomerReturn = 6,

    /// <summary>Sent back to the supplier.</summary>
    SupplierReturn = 7,

    /// <summary>Written off: damaged, lost, or expired.</summary>
    WriteOff = 8,
}

/// <summary>
/// One line in the stock ledger: an immutable record that a quantity moved, when, and why.
/// <para>
/// This is append-only by design. Movements are never edited or deleted — a mistake is corrected
/// by posting a compensating movement, exactly as an accountant would. That constraint is what
/// makes the ledger worth having: the balance on the <see cref="StockItem"/> is a running total
/// that can always be reconstructed and audited from these rows.
/// </para>
/// <para>
/// <see cref="BalanceAfter"/> is stored rather than recomputed. It costs one column and turns
/// "what did we think we had on the 14th?" from a full replay of history into a single indexed
/// lookup — the question stock disputes always come down to.
/// </para>
/// </summary>
public sealed class StockMovement : AggregateRoot<MovementId>, IAuditable, ITenantScoped
{
    private StockMovement(
        MovementId id,
        PartRef part,
        WarehouseId warehouseId,
        MovementType type,
        Quantity quantity,
        Quantity balanceAfter,
        MovementReference reference,
        DateTimeOffset occurredAtUtc)
        : base(id)
    {
        Part = part;
        WarehouseId = warehouseId;
        Type = type;
        Quantity = quantity;
        BalanceAfter = balanceAfter;
        Reference = reference;
        OccurredAtUtc = occurredAtUtc;
        CreatedBy = string.Empty;
    }

    /// <summary>Required by EF Core materialization.</summary>
#pragma warning disable CS8618
    private StockMovement()
    {
    }
#pragma warning restore CS8618

    /// <summary>The part that moved.</summary>
    public PartRef Part { get; private set; }

    /// <summary>Where it moved.</summary>
    public WarehouseId WarehouseId { get; private set; }

    /// <summary>Why it moved.</summary>
    public MovementType Type { get; private set; }

    /// <summary>
    /// How much moved, signed: positive brought stock in, negative took it out.
    /// A signed quantity means the ledger sums to the balance with no per-type special casing.
    /// </summary>
    public Quantity Quantity { get; private set; } = null!;

    /// <summary>The on-hand balance immediately after this movement.</summary>
    public Quantity BalanceAfter { get; private set; } = null!;

    /// <summary>The document behind it.</summary>
    public MovementReference Reference { get; private set; } = null!;

    /// <summary>When it happened. Not necessarily when it was entered.</summary>
    public DateTimeOffset OccurredAtUtc { get; private set; }

    /// <summary>The bin it came from or went to, where the warehouse tracks bins.</summary>
    public BinId? BinId { get; private set; }

    /// <summary>Unit cost at the time, when known. Finance values the movement from this.</summary>
    public Money? UnitCost { get; private set; }

    /// <summary>True when this movement increased stock.</summary>
    public bool IsInbound => Quantity.Value > 0m;

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

    /// <summary>
    /// Records a movement. Internal because only <see cref="StockItem"/> may create one:
    /// a movement without a matching balance change would corrupt the ledger.
    /// </summary>
    internal static StockMovement Record(
        PartRef part,
        WarehouseId warehouseId,
        MovementType type,
        Quantity quantity,
        Quantity balanceAfter,
        MovementReference reference,
        DateTimeOffset occurredAtUtc) =>
        new(MovementId.New(), part, warehouseId, type, quantity, balanceAfter, reference, occurredAtUtc);

    /// <summary>Attaches the bin the stock came from or went to.</summary>
    public StockMovement InBin(BinId binId)
    {
        BinId = binId;
        return this;
    }

    /// <summary>Attaches the unit cost, for valuation.</summary>
    public StockMovement AtCost(Money unitCost)
    {
        UnitCost = unitCost;
        return this;
    }

    /// <summary>The total value of this movement, when a cost is known.</summary>
    public Money? TotalCost => UnitCost?.Multiply(Math.Abs(Quantity.Value));
}
