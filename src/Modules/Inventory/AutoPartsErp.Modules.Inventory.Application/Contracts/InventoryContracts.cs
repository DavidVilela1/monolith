namespace AutoPartsErp.Modules.Inventory.Application.Contracts;

/// <summary>
/// One part's stock position in one warehouse.
/// <para>
/// Note that <see cref="Available"/> is returned alongside the other two rather than left for
/// the caller to subtract. Every consumer needs it, and every consumer that computes it itself
/// eventually gets it wrong.
/// </para>
/// </summary>
public sealed record StockBalance
{
    /// <summary>The balance record.</summary>
    public required Guid StockItemId { get; init; }

    /// <summary>The part.</summary>
    public required Guid PartId { get; init; }

    /// <summary>The warehouse.</summary>
    public required Guid WarehouseId { get; init; }

    /// <summary>The warehouse's short code.</summary>
    public required string WarehouseCode { get; init; }

    /// <summary>The warehouse's display name.</summary>
    public required string WarehouseName { get; init; }

    /// <summary>The unit every quantity here is counted in.</summary>
    public required string Unit { get; init; }

    /// <summary>What is physically present.</summary>
    public required decimal OnHand { get; init; }

    /// <summary>How much of that is already promised.</summary>
    public required decimal Reserved { get; init; }

    /// <summary>What can still be sold.</summary>
    public required decimal Available { get; init; }

    /// <summary>What is on order and not yet received.</summary>
    public required decimal OnOrder { get; init; }

    /// <summary>The level that triggers replenishment, when one is set.</summary>
    public decimal? ReorderPoint { get; init; }

    /// <summary>How much to order when it does.</summary>
    public decimal? ReorderQuantity { get; init; }

    /// <summary>True when available stock has reached the reorder point.</summary>
    public required bool NeedsReplenishment { get; init; }

    /// <summary>When stock here was last physically counted.</summary>
    public DateTimeOffset? LastCountedAtUtc { get; init; }
}

/// <summary>A part's stock across every warehouse, with the totals a salesperson needs.</summary>
/// <param name="PartId">The part.</param>
/// <param name="Unit">The unit all the quantities are in.</param>
/// <param name="TotalOnHand">Physically present across all sites.</param>
/// <param name="TotalAvailable">Sellable across all sites.</param>
/// <param name="ByWarehouse">The breakdown, because "somewhere in the group" rarely helps a customer.</param>
public sealed record PartStockPosition(
    Guid PartId,
    string Unit,
    decimal TotalOnHand,
    decimal TotalAvailable,
    IReadOnlyList<StockBalance> ByWarehouse);

/// <summary>A claim currently held against stock.</summary>
/// <param name="ReservationId">The claim.</param>
/// <param name="Quantity">How much is held.</param>
/// <param name="ReferenceType">What kind of document claimed it.</param>
/// <param name="ReferenceNumber">Which document.</param>
/// <param name="Status">Where the claim stands.</param>
/// <param name="CreatedAtUtc">When it was made.</param>
/// <param name="ExpiresAtUtc">When it lapses, if it does.</param>
public sealed record ReservationDto(
    Guid ReservationId,
    decimal Quantity,
    string ReferenceType,
    string ReferenceNumber,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ExpiresAtUtc);

/// <summary>One line of the stock ledger.</summary>
/// <param name="MovementId">The movement.</param>
/// <param name="PartId">The part that moved.</param>
/// <param name="WarehouseCode">Where it moved.</param>
/// <param name="Type">Why it moved.</param>
/// <param name="Quantity">How much, signed: positive in, negative out.</param>
/// <param name="BalanceAfter">The on-hand balance immediately afterwards.</param>
/// <param name="ReferenceType">What kind of document caused it.</param>
/// <param name="ReferenceNumber">Which document.</param>
/// <param name="OccurredAtUtc">When it happened.</param>
/// <param name="CreatedBy">Who entered it.</param>
public sealed record StockMovementDto(
    Guid MovementId,
    Guid PartId,
    string WarehouseCode,
    string Type,
    decimal Quantity,
    decimal BalanceAfter,
    string ReferenceType,
    string ReferenceNumber,
    DateTimeOffset OccurredAtUtc,
    string CreatedBy);

/// <summary>A warehouse, as returned by the warehouse endpoints.</summary>
/// <param name="Id">The warehouse.</param>
/// <param name="Code">Short uppercase code.</param>
/// <param name="Name">Display name.</param>
/// <param name="Kind">What kind of location it is.</param>
/// <param name="IsActive">Whether it is open to movements.</param>
/// <param name="AllowsNegativeStock">Whether balances here may go below zero.</param>
/// <param name="RequiresBinTracking">Whether movements must name a bin.</param>
/// <param name="StockedPartCount">How many parts currently hold a balance here.</param>
public sealed record WarehouseDto(
    Guid Id,
    string Code,
    string Name,
    string Kind,
    bool IsActive,
    bool AllowsNegativeStock,
    bool RequiresBinTracking,
    int StockedPartCount);
