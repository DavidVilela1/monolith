using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Inventory.Domain.Warehouses;

/// <summary>
/// A physical place stock lives: the main depot, a branch counter, a van.
/// <para>
/// Distributors almost never run one location. Branches hold their own stock, vans carry
/// consignment stock, and a returns area holds goods that are physically present but not
/// sellable. Every balance in this module is per part <i>per warehouse</i> for that reason —
/// "how many do we have?" is never a single number in this business.
/// </para>
/// </summary>
public sealed class Warehouse : AggregateRoot<WarehouseId>, IAuditable, ISoftDeletable, ITenantScoped
{
    /// <summary>Longest permitted warehouse code.</summary>
    public const int MaxCodeLength = 20;

    /// <summary>Longest permitted warehouse name.</summary>
    public const int MaxNameLength = 120;

    private Warehouse(WarehouseId id, string code, string name, WarehouseKind kind)
        : base(id)
    {
        Code = code;
        Name = name;
        Kind = kind;
        IsActive = true;
        CreatedBy = string.Empty;
    }

    /// <summary>Required by EF Core materialization.</summary>
#pragma warning disable CS8618
    private Warehouse()
    {
    }
#pragma warning restore CS8618

    /// <summary>Short uppercase code shown on documents: MAIN, BR01, VAN07.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>Display name.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>What kind of location this is.</summary>
    public WarehouseKind Kind { get; private set; }

    /// <summary>Whether stock may still be moved in and out.</summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// Whether stock here may go negative.
    /// <para>
    /// Almost always false. Negative stock means the system is describing something physically
    /// impossible, and every downstream valuation and replenishment figure inherits the lie.
    /// The exception is a busy trade counter that sells before the paperwork catches up, where
    /// blocking the sale costs more than the temporary inaccuracy — so it is a per-warehouse
    /// decision made deliberately, not a global default.
    /// </para>
    /// </summary>
    public bool AllowsNegativeStock { get; private set; }

    /// <summary>Whether picking must name a bin. Larger sites need it; a van does not.</summary>
    public bool RequiresBinTracking { get; private set; }

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

    /// <summary>Registers a warehouse.</summary>
    /// <param name="code">Short code, uppercased automatically.</param>
    /// <param name="name">Display name.</param>
    /// <param name="kind">What kind of location this is.</param>
    /// <param name="requiresBinTracking">Whether picking must name a bin.</param>
    public static Result<Warehouse> Create(
        string? code,
        string? name,
        WarehouseKind kind = WarehouseKind.Depot,
        bool requiresBinTracking = false)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return InventoryErrors.Warehouse.CodeRequired;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return InventoryErrors.Warehouse.NameRequired;
        }

        string normalizedCode = code.Trim().ToUpperInvariant();
        if (normalizedCode.Length > MaxCodeLength)
        {
            return InventoryErrors.Warehouse.CodeTooLong;
        }

        string trimmedName = name.Trim();
        if (trimmedName.Length > MaxNameLength)
        {
            return InventoryErrors.Warehouse.NameTooLong;
        }

        return new Warehouse(WarehouseId.New(), normalizedCode, trimmedName, kind)
        {
            RequiresBinTracking = requiresBinTracking,
        };
    }

    /// <summary>Changes the display name.</summary>
    public Result Rename(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return InventoryErrors.Warehouse.NameRequired;
        }

        string trimmed = name.Trim();
        if (trimmed.Length > MaxNameLength)
        {
            return InventoryErrors.Warehouse.NameTooLong;
        }

        Name = trimmed;
        return Result.Success();
    }

    /// <summary>
    /// Permits stock here to go negative. Deliberately explicit, and worth a conversation
    /// with whoever owns the stock figures before it is switched on.
    /// </summary>
    public void AllowNegativeStock() => AllowsNegativeStock = true;

    /// <summary>Stops stock here going negative.</summary>
    public void DisallowNegativeStock() => AllowsNegativeStock = false;

    /// <summary>Requires bins to be named on movements.</summary>
    public void RequireBinTracking() => RequiresBinTracking = true;

    /// <summary>Closes the warehouse to new movements. Existing balances are untouched.</summary>
    public void Deactivate() => IsActive = false;

    /// <summary>Reopens the warehouse.</summary>
    public void Reactivate() => IsActive = true;
}

/// <summary>What a warehouse is for. Drives default behaviour and reporting.</summary>
public enum WarehouseKind
{
    /// <summary>Unspecified. Never persisted.</summary>
    Unknown = 0,

    /// <summary>A main distribution depot.</summary>
    Depot = 1,

    /// <summary>A branch with a trade counter.</summary>
    Branch = 2,

    /// <summary>Stock carried on a van.</summary>
    Van = 3,

    /// <summary>Goods received but not yet checked in.</summary>
    Receiving = 4,

    /// <summary>Damaged goods, returns pending inspection, and warranty holds.</summary>
    Quarantine = 5,

    /// <summary>Cores awaiting return to the supplier.</summary>
    CoreReturn = 6,
}
