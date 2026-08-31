using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Inventory.Domain.Warehouses;

/// <summary>
/// A named place inside a warehouse: aisle, rack, shelf.
/// <para>
/// Its own aggregate rather than a child of <see cref="Warehouse"/> because a real depot has
/// thousands of them. Loading a warehouse should not mean loading its entire shelf layout, and
/// renaming one bin should not contend with a lock on the whole site.
/// </para>
/// </summary>
public sealed class StorageBin : AggregateRoot<BinId>, IAuditable, ISoftDeletable, ITenantScoped
{
    /// <summary>Longest permitted bin code.</summary>
    public const int MaxCodeLength = 30;

    private StorageBin(BinId id, WarehouseId warehouseId, string code, BinKind kind)
        : base(id)
    {
        WarehouseId = warehouseId;
        Code = code;
        Kind = kind;
        IsActive = true;
        CreatedBy = string.Empty;
    }

    /// <summary>Required by EF Core materialization.</summary>
#pragma warning disable CS8618
    private StorageBin()
    {
    }
#pragma warning restore CS8618

    /// <summary>The warehouse this bin belongs to.</summary>
    public WarehouseId WarehouseId { get; private set; }

    /// <summary>The location code as printed on the shelf label, e.g. A-12-3.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>What this bin is used for.</summary>
    public BinKind Kind { get; private set; }

    /// <summary>Sequence used to order a picking route so pickers walk the aisles once.</summary>
    public int PickSequence { get; private set; }

    /// <summary>Whether stock may be put here.</summary>
    public bool IsActive { get; private set; }

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

    /// <inheritdoc />
    public bool IsDeleted { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? DeletedAtUtc { get; set; }

    /// <inheritdoc />
    public string? DeletedBy { get; set; }

    /// <summary>Creates a bin.</summary>
    /// <param name="warehouseId">The owning warehouse.</param>
    /// <param name="code">The shelf label code, uppercased automatically.</param>
    /// <param name="kind">What the bin is used for.</param>
    /// <param name="pickSequence">Where it falls on the picking route.</param>
    public static Result<StorageBin> Create(
        WarehouseId warehouseId,
        string? code,
        BinKind kind = BinKind.Picking,
        int pickSequence = 0)
    {
        if (warehouseId.IsEmpty)
        {
            return InventoryErrors.Stock.WarehouseRequired;
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return InventoryErrors.Bin.CodeRequired;
        }

        string normalized = code.Trim().ToUpperInvariant();

        return normalized.Length > MaxCodeLength
            ? InventoryErrors.Bin.CodeTooLong
            : new StorageBin(BinId.New(), warehouseId, normalized, kind) { PickSequence = pickSequence };
    }

    /// <summary>Moves the bin's position on the picking route.</summary>
    public void Resequence(int pickSequence) => PickSequence = pickSequence;

    /// <summary>Stops stock being put here.</summary>
    public void Deactivate() => IsActive = false;

    /// <summary>Allows stock here again.</summary>
    public void Reactivate() => IsActive = true;
}

/// <summary>What a bin is used for.</summary>
public enum BinKind
{
    /// <summary>Unspecified. Never persisted.</summary>
    Unknown = 0,

    /// <summary>A face location picked from every day.</summary>
    Picking = 1,

    /// <summary>Overflow that replenishes the pick face.</summary>
    Bulk = 2,

    /// <summary>Where deliveries land before being checked in.</summary>
    Receiving = 3,

    /// <summary>Picked stock waiting to be loaded.</summary>
    Dispatch = 4,

    /// <summary>Damaged or suspect goods, held pending a decision.</summary>
    Quarantine = 5,

    /// <summary>Cores waiting to go back to the supplier.</summary>
    CoreHolding = 6,
}
