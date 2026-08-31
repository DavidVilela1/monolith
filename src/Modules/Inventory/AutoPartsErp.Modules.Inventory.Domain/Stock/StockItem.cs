using AutoPartsErp.Modules.Inventory.Domain.Stock.Events;
using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Inventory.Domain.Stock;

/// <summary>
/// The stock balance for one part in one warehouse, and the consistency boundary for every
/// change to it.
/// <para>
/// Three numbers matter, and conflating them is the classic inventory bug:
/// <b>on hand</b> is what is physically on the shelf, <b>reserved</b> is how much of that is
/// already promised, and <b>available</b> is the difference — the only number a salesperson
/// should ever be shown. A part with 10 on hand and 10 reserved is not "in stock".
/// </para>
/// <para>
/// Every mutation runs through this aggregate so the three stay in step, and every mutation
/// produces a movement in the ledger. There is no setter for a balance: you cannot correct
/// stock without saying why.
/// </para>
/// </summary>
public sealed class StockItem : AggregateRoot<StockItemId>, IAuditable, ITenantScoped
{
    private readonly List<StockReservation> _reservations = [];

    private StockItem(StockItemId id, PartRef part, WarehouseId warehouseId, UnitOfMeasure unit)
        : base(id)
    {
        Part = part;
        WarehouseId = warehouseId;
        Unit = unit;
        OnHand = Quantity.Zero(unit);
        Reserved = Quantity.Zero(unit);
        OnOrder = Quantity.Zero(unit);
        CreatedBy = string.Empty;
    }

    /// <summary>Required by EF Core materialization.</summary>
#pragma warning disable CS8618
    private StockItem()
    {
    }
#pragma warning restore CS8618

    /// <summary>The part this balance is for. An identity only; Catalog owns the part itself.</summary>
    public PartRef Part { get; private set; }

    /// <summary>Where the stock is.</summary>
    public WarehouseId WarehouseId { get; private set; }

    /// <summary>
    /// The unit every quantity here is expressed in, copied from the catalogue when the record
    /// was opened. Catalog freezes a part's stocking unit on activation precisely so this cannot
    /// drift underneath the balances.
    /// </summary>
    public UnitOfMeasure Unit { get; private set; } = UnitOfMeasure.Each;

    /// <summary>What is physically present, promised or not.</summary>
    public Quantity OnHand { get; private set; } = null!;

    /// <summary>How much of <see cref="OnHand"/> is already spoken for.</summary>
    public Quantity Reserved { get; private set; } = null!;

    /// <summary>What is on a purchase order and not yet received.</summary>
    public Quantity OnOrder { get; private set; } = null!;

    /// <summary>What can still be sold: on hand minus reserved.</summary>
    public Quantity Available => OnHand.Subtract(Reserved);

    /// <summary>The level at which replenishment should be suggested.</summary>
    public Quantity? ReorderPoint { get; private set; }

    /// <summary>How much to order when the reorder point is reached.</summary>
    public Quantity? ReorderQuantity { get; private set; }

    /// <summary>The default bin picked from, when the warehouse tracks bins.</summary>
    public BinId? DefaultBinId { get; private set; }

    /// <summary>When stock was last physically counted here.</summary>
    public DateTimeOffset? LastCountedAtUtc { get; private set; }

    /// <summary>Claims currently held against this balance.</summary>
    public IReadOnlyCollection<StockReservation> Reservations => _reservations.AsReadOnly();

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
    /// Opens a zero balance for a part in a warehouse.
    /// <para>
    /// Called when Catalog reports that a part went live. The record exists before any stock
    /// does, so that the first receipt has somewhere to land and so "we hold none" is
    /// distinguishable from "we have never heard of it".
    /// </para>
    /// </summary>
    public static Result<StockItem> Open(PartRef part, WarehouseId warehouseId, UnitOfMeasure unit)
    {
        ArgumentNullException.ThrowIfNull(unit);

        if (part.IsEmpty)
        {
            return InventoryErrors.Stock.PartRequired;
        }

        if (warehouseId.IsEmpty)
        {
            return InventoryErrors.Stock.WarehouseRequired;
        }

        var item = new StockItem(StockItemId.New(), part, warehouseId, unit);
        item.Raise(new StockRecordOpenedDomainEvent(item.Id, part, warehouseId, unit.Code));

        return item;
    }

    /// <summary>Brings stock in.</summary>
    /// <param name="quantity">How much. Must be positive.</param>
    /// <param name="reference">The document that caused it.</param>
    /// <param name="now">The current instant, supplied by the caller's clock.</param>
    public Result<StockMovement> Receive(decimal quantity, MovementReference reference, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(reference);

        Result<Quantity> parsed = ParsePositive(quantity);
        if (parsed.IsFailure)
        {
            return Result.Failure<StockMovement>(parsed.Error);
        }

        OnHand = OnHand.Add(parsed.Value);

        StockMovement movement = StockMovement.Record(
            Part, WarehouseId, MovementType.Receipt, parsed.Value, OnHand, reference, now);

        Raise(new StockReceivedDomainEvent(Id, Part, WarehouseId, parsed.Value.Value, reference.Number));

        return movement;
    }

    /// <summary>
    /// Takes stock out.
    /// <para>
    /// Refuses to go below zero unless the warehouse explicitly permits it. That check is the
    /// difference between a stock figure people trust and one they work around.
    /// </para>
    /// </summary>
    /// <param name="quantity">How much. Must be positive.</param>
    /// <param name="reference">The document that caused it.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="allowNegative">Whether the warehouse permits negative balances.</param>
    public Result<StockMovement> Issue(
        decimal quantity,
        MovementReference reference,
        DateTimeOffset now,
        bool allowNegative = false)
    {
        ArgumentNullException.ThrowIfNull(reference);

        Result<Quantity> parsed = ParsePositive(quantity);
        if (parsed.IsFailure)
        {
            return Result.Failure<StockMovement>(parsed.Error);
        }

        if (!allowNegative && parsed.Value > OnHand)
        {
            return Result.Failure<StockMovement>(
                InventoryErrors.Stock.InsufficientOnHand(OnHand.Value, parsed.Value.Value, Unit.Code));
        }

        OnHand = OnHand.Subtract(parsed.Value);

        StockMovement movement = StockMovement.Record(
            Part, WarehouseId, MovementType.Issue, parsed.Value.Multiply(-1m), OnHand, reference, now);

        Raise(new StockIssuedDomainEvent(Id, Part, WarehouseId, parsed.Value.Value, reference.Number));

        CheckReorderPoint();

        return movement;
    }

    /// <summary>
    /// Corrects the balance to a counted figure, recording the difference as a movement.
    /// The caller supplies what was actually on the shelf; the system works out the delta.
    /// </summary>
    /// <param name="countedQuantity">What the count found. May be zero, never negative.</param>
    /// <param name="reference">The count or adjustment document. A note is expected.</param>
    /// <param name="now">The current instant.</param>
    public Result<StockMovement> AdjustTo(
        decimal countedQuantity,
        MovementReference reference,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(reference);

        if (countedQuantity < 0m)
        {
            return Result.Failure<StockMovement>(InventoryErrors.Stock.CountCannotBeNegative);
        }

        Result<Quantity> parsed = Quantity.Create(countedQuantity, Unit);
        if (parsed.IsFailure)
        {
            return Result.Failure<StockMovement>(parsed.Error);
        }

        Quantity delta = parsed.Value.Subtract(OnHand);

        if (delta.IsZero)
        {
            return Result.Failure<StockMovement>(InventoryErrors.Stock.AdjustmentChangesNothing);
        }

        // A count that lands below what is already promised leaves reservations that cannot be
        // met. Better to refuse and have someone look than to quietly create an impossible state.
        if (parsed.Value < Reserved)
        {
            return Result.Failure<StockMovement>(
                InventoryErrors.Stock.CountBelowReserved(countedQuantity, Reserved.Value, Unit.Code));
        }

        OnHand = parsed.Value;
        LastCountedAtUtc = now;

        StockMovement movement = StockMovement.Record(
            Part, WarehouseId, MovementType.Adjustment, delta, OnHand, reference, now);

        Raise(new StockAdjustedDomainEvent(Id, Part, WarehouseId, delta.Value, reference.Number));

        CheckReorderPoint();

        return movement;
    }

    /// <summary>
    /// Holds stock back for a document without moving it.
    /// </summary>
    /// <param name="quantity">How much to hold. Must be positive and available.</param>
    /// <param name="reference">What is claiming it.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="expiresAtUtc">When the claim lapses if nobody acts on it.</param>
    public Result<StockReservation> Reserve(
        decimal quantity,
        MovementReference reference,
        DateTimeOffset now,
        DateTimeOffset? expiresAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(reference);

        Result<Quantity> parsed = ParsePositive(quantity);
        if (parsed.IsFailure)
        {
            return Result.Failure<StockReservation>(parsed.Error);
        }

        if (parsed.Value > Available)
        {
            return Result.Failure<StockReservation>(
                InventoryErrors.Stock.InsufficientAvailable(Available.Value, parsed.Value.Value, Unit.Code));
        }

        if (expiresAtUtc is { } expiry && expiry <= now)
        {
            return Result.Failure<StockReservation>(InventoryErrors.Stock.ReservationExpiryInPast);
        }

        StockReservation reservation = StockReservation.Create(parsed.Value, reference, now, expiresAtUtc);
        _reservations.Add(reservation);
        Reserved = Reserved.Add(parsed.Value);

        Raise(new StockReservedDomainEvent(Id, Part, WarehouseId, parsed.Value.Value, reference.Number));

        return reservation;
    }

    /// <summary>Gives a claim back, returning its quantity to available stock.</summary>
    public Result Release(ReservationId reservationId)
    {
        StockReservation? reservation = _reservations.Find(r => r.Id == reservationId);

        if (reservation is null)
        {
            return InventoryErrors.Stock.ReservationNotFound;
        }

        if (!reservation.IsActive)
        {
            return InventoryErrors.Stock.ReservationNotActive;
        }

        reservation.Release();
        Reserved = Reserved.Subtract(reservation.Quantity);

        Raise(new StockReservationReleasedDomainEvent(Id, reservationId, reservation.Quantity.Value));

        return Result.Success();
    }

    /// <summary>
    /// Issues stock against an existing claim: the picker took what was promised.
    /// Consumes the reservation and reduces on-hand in one step, so the two can never
    /// drift apart the way they would if the caller did both separately.
    /// </summary>
    public Result<StockMovement> Fulfil(ReservationId reservationId, DateTimeOffset now)
    {
        StockReservation? reservation = _reservations.Find(r => r.Id == reservationId);

        if (reservation is null)
        {
            return Result.Failure<StockMovement>(InventoryErrors.Stock.ReservationNotFound);
        }

        if (!reservation.IsActive)
        {
            return Result.Failure<StockMovement>(InventoryErrors.Stock.ReservationNotActive);
        }

        Quantity quantity = reservation.Quantity;

        reservation.Fulfil();
        Reserved = Reserved.Subtract(quantity);
        OnHand = OnHand.Subtract(quantity);

        StockMovement movement = StockMovement.Record(
            Part, WarehouseId, MovementType.Issue, quantity.Multiply(-1m), OnHand, reservation.Reference, now);

        Raise(new StockIssuedDomainEvent(Id, Part, WarehouseId, quantity.Value, reservation.Reference.Number));

        CheckReorderPoint();

        return movement;
    }

    /// <summary>
    /// Lapses every claim that has passed its expiry, returning the stock to available.
    /// Run on a schedule; without it, abandoned quotes slowly consume the shelf.
    /// </summary>
    /// <returns>How many reservations were expired.</returns>
    public int ExpireLapsedReservations(DateTimeOffset now)
    {
        int expired = 0;

        foreach (StockReservation reservation in _reservations)
        {
            if (!reservation.HasExpired(now))
            {
                continue;
            }

            reservation.Expire();
            Reserved = Reserved.Subtract(reservation.Quantity);
            Raise(new StockReservationExpiredDomainEvent(Id, reservation.Id, reservation.Quantity.Value));
            expired++;
        }

        return expired;
    }

    /// <summary>Records what is expected in from suppliers but not yet received.</summary>
    public Result SetOnOrder(decimal quantity)
    {
        if (quantity < 0m)
        {
            return InventoryErrors.Stock.OnOrderCannotBeNegative;
        }

        Result<Quantity> parsed = Quantity.Create(quantity, Unit);
        if (parsed.IsFailure)
        {
            return Result.Failure(parsed.Error);
        }

        OnOrder = parsed.Value;
        return Result.Success();
    }

    /// <summary>Sets when to reorder and how much, or clears the policy when both are null.</summary>
    public Result SetReplenishmentPolicy(decimal? reorderPoint, decimal? reorderQuantity)
    {
        if (reorderPoint is null && reorderQuantity is null)
        {
            ReorderPoint = null;
            ReorderQuantity = null;
            return Result.Success();
        }

        if (reorderPoint is null || reorderQuantity is null)
        {
            return InventoryErrors.Stock.IncompleteReplenishmentPolicy;
        }

        if (reorderPoint < 0m || reorderQuantity <= 0m)
        {
            return InventoryErrors.Stock.InvalidReplenishmentPolicy;
        }

        Result<Quantity> point = Quantity.Create(reorderPoint.Value, Unit);
        if (point.IsFailure)
        {
            return Result.Failure(point.Error);
        }

        Result<Quantity> amount = Quantity.Create(reorderQuantity.Value, Unit);
        if (amount.IsFailure)
        {
            return Result.Failure(amount.Error);
        }

        ReorderPoint = point.Value;
        ReorderQuantity = amount.Value;

        return Result.Success();
    }

    /// <summary>Sets the bin this part is normally picked from.</summary>
    public void AssignDefaultBin(BinId binId) => DefaultBinId = binId;

    /// <summary>True when available stock has reached the level that should trigger a reorder.</summary>
    public bool NeedsReplenishment =>
        ReorderPoint is { } point && Available <= point;

    private void CheckReorderPoint()
    {
        if (ReorderPoint is not { } point || ReorderQuantity is not { } amount)
        {
            return;
        }

        if (Available <= point)
        {
            Raise(new StockFellBelowReorderPointDomainEvent(
                Id, Part, WarehouseId, Available.Value, point.Value, amount.Value));
        }
    }

    private Result<Quantity> ParsePositive(decimal quantity)
    {
        if (quantity <= 0m)
        {
            return Result.Failure<Quantity>(InventoryErrors.Stock.QuantityMustBePositive);
        }

        return Quantity.Create(quantity, Unit);
    }
}
