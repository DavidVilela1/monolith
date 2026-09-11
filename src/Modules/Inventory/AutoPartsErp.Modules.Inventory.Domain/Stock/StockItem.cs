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
    private readonly List<IncomingStock> _incoming = [];

    private StockItem(StockItemId id, PartRef part, WarehouseId warehouseId, UnitOfMeasure unit)
        : base(id)
    {
        Part = part;
        WarehouseId = warehouseId;
        Unit = unit;
        OnHand = Quantity.Zero(unit);
        Reserved = Quantity.Zero(unit);
        OnOrder = Quantity.Zero(unit);
        StockValue = Money.Zero(Currency.Default);
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

    /// <summary>
    /// What is on a purchase order and not yet received.
    /// <para>
    /// Kept in step with <see cref="Incoming"/> rather than set directly: it is the sum of the
    /// outstanding expectations, stored because the reorder query filters on it and a filter
    /// cannot be written against a sum computed in C#.
    /// </para>
    /// </summary>
    public Quantity OnOrder { get; private set; } = null!;

    /// <summary>What can still be sold: on hand minus reserved.</summary>
    public Quantity Available => OnHand.Subtract(Reserved);

    /// <summary>
    /// What the stock on this shelf is worth.
    /// <para>
    /// The stored figure, and the one the balance sheet is built from. Average cost is derived
    /// from it rather than stored, which is the opposite of how it is usually described and is
    /// the only version that stays correct: <see cref="Money"/> rounds to the currency's decimal
    /// places, so a stored per-unit cost would round on every single receipt and the error would
    /// compound for the life of the part. A total in euros rounds once, against a number that is
    /// actually denominated in euros.
    /// </para>
    /// <para>
    /// Always in the company's own currency — <c>Currency.Default</c>. Stock is valued in what
    /// the company reports in, whatever it happened to be bought in.
    /// </para>
    /// </summary>
    public Money StockValue { get; private set; } = null!;

    /// <summary>
    /// What one unit is currently worth on average, or null when there is nothing on the shelf.
    /// <para>
    /// Derived and rounded, for people to read. Never the figure anything is calculated from —
    /// see <see cref="StockValue"/>.
    /// </para>
    /// </summary>
    public Money? AverageCost =>
        OnHand.Value > 0m ? StockValue.Divide(OnHand.Value) : null;

    /// <summary>
    /// Available plus what is on order — the position used to decide whether to buy more.
    /// <para>
    /// Never show this to a salesperson. It counts goods that are not in the building, and a
    /// counter that promises them has promised a delivery date it does not know. It exists for
    /// exactly one decision: whether ordering more would be ordering the same thing twice.
    /// </para>
    /// </summary>
    public Quantity ProjectedAvailable => Available.Add(OnOrder);

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

    /// <summary>Deliveries expected against this balance, arrived and cancelled ones included.</summary>
    public IReadOnlyCollection<IncomingStock> Incoming => _incoming.AsReadOnly();

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

    /// <summary>
    /// Brings stock in.
    /// <para>
    /// The unit price is optional and its absence is meaningful rather than lazy. A purchase
    /// receipt knows what was paid; a transfer from another branch, a customer return and a count
    /// that found more than expected do not, and there is no honest price to invent for them.
    /// A receipt with no price comes in at whatever the shelf is already worth per unit, which
    /// leaves the average exactly where it was. Adding the quantity and no value would be the
    /// same as calling the goods free: the average would be diluted towards nothing, and the
    /// first customer return would wreck the valuation of a part sold for years.
    /// </para>
    /// <para>
    /// Into an empty or never-priced shelf there is no average to apply and the quantity simply
    /// joins the balance uncosted — the state a part is in before its first priced receipt, and
    /// one the ledger reports honestly as a movement with no cost rather than a cost of zero.
    /// </para>
    /// </summary>
    /// <param name="quantity">How much. Must be positive.</param>
    /// <param name="reference">The document that caused it.</param>
    /// <param name="now">The current instant, supplied by the caller's clock.</param>
    /// <param name="unitPrice">What was paid per unit, when the receipt knows.</param>
    public Result<StockMovement> Receive(
        decimal quantity,
        MovementReference reference,
        DateTimeOffset now,
        Money? unitPrice = null)
    {
        ArgumentNullException.ThrowIfNull(reference);

        Result<Quantity> parsed = ParsePositive(quantity);
        if (parsed.IsFailure)
        {
            return Result.Failure<StockMovement>(parsed.Error);
        }

        // Checked before the balance moves, so a receipt in the wrong currency changes nothing.
        if (unitPrice is not null && unitPrice.Currency != StockValue.Currency)
        {
            return Result.Failure<StockMovement>(
                InventoryErrors.Stock.CostCurrencyMismatch(
                    unitPrice.Currency.Code, StockValue.Currency.Code));
        }

        Quantity before = OnHand;
        OnHand = OnHand.Add(parsed.Value);

        Money? value;

        if (unitPrice is not null)
        {
            value = unitPrice.Multiply(parsed.Value.Value);
            StockValue = StockValue.Add(value);
        }
        else
        {
            value = AddValueAtAverage(parsed.Value, before);
        }

        StockMovement movement = StockMovement.Record(
            Part, WarehouseId, MovementType.Receipt, parsed.Value, OnHand, reference, now);

        if (value is not null)
        {
            movement.AtValue(value);
        }

        Raise(new StockReceivedDomainEvent(Id, Part, WarehouseId, parsed.Value.Value, reference.Number));

        return movement;
    }

    /// <summary>
    /// Brings stock in at a value that is already known exactly.
    /// <para>
    /// For goods arriving from another shelf in the same company. The value came off the sending
    /// warehouse's balance as a total, and it has to land here as the same total — deriving a unit
    /// price from it and multiplying back would round twice, and the company would quietly gain or
    /// lose a few cents every time a van moved between branches. Same reason the ledger stores
    /// what a movement was worth rather than what a unit cost.
    /// </para>
    /// <para>
    /// Separate from <see cref="Receive"/> rather than an overload of it because the two are
    /// genuinely different facts. A purchase knows a price per unit and the total follows; a
    /// transfer knows a total and no price per unit exists — the goods may have been bought on
    /// four different days at four different prices.
    /// </para>
    /// </summary>
    /// <param name="quantity">How much arrived. Must be positive.</param>
    /// <param name="reference">The transfer that sent it.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="value">
    /// What the goods were worth when they left the other warehouse. Null when the sending shelf
    /// had no value of its own, which happens for stock that has never been through a priced
    /// receipt.
    /// </param>
    public Result<StockMovement> ReceiveValued(
        decimal quantity,
        MovementReference reference,
        DateTimeOffset now,
        Money? value)
    {
        ArgumentNullException.ThrowIfNull(reference);

        Result<Quantity> parsed = ParsePositive(quantity);
        if (parsed.IsFailure)
        {
            return Result.Failure<StockMovement>(parsed.Error);
        }

        if (value is not null && value.Currency != StockValue.Currency)
        {
            return Result.Failure<StockMovement>(
                InventoryErrors.Stock.CostCurrencyMismatch(
                    value.Currency.Code, StockValue.Currency.Code));
        }

        OnHand = OnHand.Add(parsed.Value);

        if (value is not null)
        {
            StockValue = StockValue.Add(value);
        }

        StockMovement movement = StockMovement.Record(
            Part, WarehouseId, MovementType.TransferIn, parsed.Value, OnHand, reference, now);

        if (value is not null)
        {
            movement.AtValue(value);
        }

        Raise(new StockReceivedDomainEvent(Id, Part, WarehouseId, parsed.Value.Value, reference.Number));

        return movement;
    }

    /// <summary>
    /// Corrects what the shelf is worth when the supplier's invoice charged something the purchase
    /// order had not predicted.
    /// <para>
    /// No quantity moves. Nothing arrived and nothing left — the goods were booked in at the price
    /// the order was placed at, the supplier billed a different figure, and the balance sheet now
    /// has to agree with the money that will leave the bank. The ledger row carries a quantity of
    /// zero and a value, which is the honest shape of the fact and the reason
    /// <see cref="MovementType.PriceVariance"/> exists.
    /// </para>
    /// <para>
    /// <b>Only the part still on the shelf is corrected, and the caller works out how much that
    /// is.</b> Goods already sold went out at the old cost and are gone; their share of the
    /// difference belongs in cost of sale, and there is no general ledger to put it in. Moving it
    /// onto the shelf anyway would make the remaining units carry the whole error — twenty units
    /// left out of a hundred would each absorb five times what they should, and the stock value
    /// would stop being a number anybody could explain.
    /// </para>
    /// <para>
    /// The value may be negative: a supplier who charged less than the order said makes the shelf
    /// worth less, not more. What is refused is a correction that would take the shelf below
    /// nothing, because a negative stock value is not a fact about any warehouse.
    /// </para>
    /// </summary>
    /// <param name="difference">
    /// What to add to the shelf's value. Positive when the supplier charged more than the order
    /// said, negative when they charged less.
    /// </param>
    /// <param name="reference">The supplier's document that caused it.</param>
    /// <param name="now">The current instant.</param>
    public Result<StockMovement> Revalue(Money difference, MovementReference reference, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(difference);
        ArgumentNullException.ThrowIfNull(reference);

        if (difference.Currency != StockValue.Currency)
        {
            return Result.Failure<StockMovement>(
                InventoryErrors.Stock.CostCurrencyMismatch(
                    difference.Currency.Code, StockValue.Currency.Code));
        }

        // Nothing to record. A variance of zero is the ordinary case — most deliveries are
        // invoiced at the price they were ordered at — and writing a ledger row for it would bury
        // the ones that matter under thousands that do not.
        if (difference.Amount == 0m)
        {
            return Result.Failure<StockMovement>(InventoryErrors.Stock.NoVariance);
        }

        Money revalued = StockValue.Add(difference);

        if (revalued.IsNegative)
        {
            return Result.Failure<StockMovement>(InventoryErrors.Stock.VarianceBelowZero);
        }

        StockValue = revalued;

        StockMovement movement = StockMovement.Record(
            Part,
            WarehouseId,
            MovementType.PriceVariance,
            Quantity.Zero(Unit),
            OnHand,
            reference,
            now);

        movement.AtValue(difference);

        return movement;
    }

    /// <summary>
    /// Puts a customer's return back on the shelf, at what it was worth when it left.
    /// <para>
    /// The arithmetic is <see cref="ReceiveValued"/>'s — a known total joins the balance rather
    /// than a unit price being multiplied back up — and the reason is the same one: what came off
    /// the shelf has to be what goes back on it, to the cent.
    /// </para>
    /// <para>
    /// <b>Not at today's average.</b> A part sold in March at €40 and returned in September onto
    /// a shelf that has since averaged down to €31 would arrive worth nine euros more than it
    /// cost, and the difference would be a silent profit on a transaction where the company made
    /// nothing at all. Enough of those and the stock value stops being a number anybody can
    /// explain. What it cost when it left is a fact in this module's own ledger, so it is looked
    /// up rather than guessed.
    /// </para>
    /// <para>
    /// A null value means the original issue took nothing off the balance either — stock that has
    /// never been through a priced receipt. The quantity joins the shelf uncosted, which is the
    /// honest state rather than a cost of zero.
    /// </para>
    /// </summary>
    /// <param name="quantity">How much came back. Must be positive.</param>
    /// <param name="reference">The return that brought it.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="value">What the goods were worth when they went out, when that is known.</param>
    public Result<StockMovement> ReceiveReturn(
        decimal quantity,
        MovementReference reference,
        DateTimeOffset now,
        Money? value)
    {
        ArgumentNullException.ThrowIfNull(reference);

        Result<Quantity> parsed = ParsePositive(quantity);
        if (parsed.IsFailure)
        {
            return Result.Failure<StockMovement>(parsed.Error);
        }

        if (value is not null && value.Currency != StockValue.Currency)
        {
            return Result.Failure<StockMovement>(
                InventoryErrors.Stock.CostCurrencyMismatch(
                    value.Currency.Code, StockValue.Currency.Code));
        }

        OnHand = OnHand.Add(parsed.Value);

        if (value is not null)
        {
            StockValue = StockValue.Add(value);
        }

        // Its own movement type, not a receipt. "What did we buy this month" and "what did
        // customers bring back this month" are different questions, and a ledger that answered
        // both with the same row could answer neither.
        StockMovement movement = StockMovement.Record(
            Part, WarehouseId, MovementType.CustomerReturn, parsed.Value, OnHand, reference, now);

        if (value is not null)
        {
            movement.AtValue(value);
        }

        Raise(new StockReceivedDomainEvent(Id, Part, WarehouseId, parsed.Value.Value, reference.Number));

        return movement;
    }

    /// <summary>
    /// Sends stock to another warehouse, and answers what value went with it.
    /// <para>
    /// The value is the whole reason this is not just an issue. Stock moving between two of the
    /// company's own shelves must not change what the company owns: whatever comes off here has
    /// to land there, to the cent. An issue that threw the figure away and a receipt that invented
    /// a new one would make every transfer a small, silent revaluation.
    /// </para>
    /// </summary>
    /// <param name="quantity">How much is going. Must be positive and on the shelf.</param>
    /// <param name="reference">The transfer sending it.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="allowNegative">Whether the warehouse permits negative balances.</param>
    public Result<TransferredStock> Dispatch(
        decimal quantity,
        MovementReference reference,
        DateTimeOffset now,
        bool allowNegative = false)
    {
        ArgumentNullException.ThrowIfNull(reference);

        Result<Quantity> parsed = ParsePositive(quantity);
        if (parsed.IsFailure)
        {
            return Result.Failure<TransferredStock>(parsed.Error);
        }

        if (!allowNegative && parsed.Value > OnHand)
        {
            return Result.Failure<TransferredStock>(
                InventoryErrors.Stock.InsufficientOnHand(OnHand.Value, parsed.Value.Value, Unit.Code));
        }

        Quantity before = OnHand;
        OnHand = OnHand.Subtract(parsed.Value);

        Money? value = TakeValueOut(parsed.Value, before);

        StockMovement movement = StockMovement.Record(
            Part, WarehouseId, MovementType.TransferOut, parsed.Value.Multiply(-1m), OnHand, reference, now);

        if (value is not null)
        {
            movement.AtValue(value);
        }

        Raise(new StockIssuedDomainEvent(Id, Part, WarehouseId, parsed.Value.Value, reference.Number));

        CheckReorderPoint();

        return new TransferredStock(movement, value);
    }

    /// <summary>
    /// Takes the value of a departing quantity off the balance, and answers what it was.
    /// <para>
    /// One of the two places costing happens, and the only place a going-out cost is decided.
    /// Everything that reduces stock comes through here — a sale, a write-off, a count that found
    /// less — so swapping moving average for FIFO means rewriting this method and its partner in
    /// <see cref="Receive"/>, and nothing else in this module or any other. That is the whole
    /// seam: outside Inventory a cost only ever appears as a value stamped on a ledger row.
    /// </para>
    /// <para>
    /// The last issue takes whatever is left rather than its proportional share. Proportions
    /// round, and rounding leaves a few cents of value sitting against a shelf with nothing on
    /// it — a balance sheet that says the company owns €0.03 of a part it has none of. Somebody
    /// eventually writes a correction routine for that; the correction is to not create it.
    /// </para>
    /// </summary>
    private Money? TakeValueOut(Quantity quantity, Quantity onHandBefore)
    {
        if (StockValue.IsZero || onHandBefore.Value <= 0m)
        {
            return null;
        }

        if (quantity.Value >= onHandBefore.Value)
        {
            Money everything = StockValue;
            StockValue = Money.Zero(StockValue.Currency);

            return everything;
        }

        Money value = StockValue.Multiply(quantity.Value / onHandBefore.Value);
        StockValue = StockValue.Subtract(value);

        return value;
    }

    /// <summary>
    /// Adds quantity at whatever the stock already on the shelf is worth per unit.
    /// <para>
    /// For arrivals with no price of their own. Nothing to do when the shelf was empty or has no
    /// value: there is no average to apply, and the quantity simply joins the balance uncosted —
    /// the same state a part is in before its first priced receipt.
    /// </para>
    /// </summary>
    private Money? AddValueAtAverage(Quantity quantity, Quantity onHandBefore)
    {
        if (StockValue.IsZero || onHandBefore.Value <= 0m)
        {
            return null;
        }

        Money value = StockValue.Multiply(quantity.Value / onHandBefore.Value);
        StockValue = StockValue.Add(value);

        return value;
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

        Quantity before = OnHand;
        OnHand = OnHand.Subtract(parsed.Value);

        Money? value = TakeValueOut(parsed.Value, before);

        StockMovement movement = StockMovement.Record(
            Part, WarehouseId, MovementType.Issue, parsed.Value.Multiply(-1m), OnHand, reference, now);

        if (value is not null)
        {
            movement.AtValue(value);
        }

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

        Quantity before = OnHand;
        OnHand = parsed.Value;
        LastCountedAtUtc = now;

        // A count that found less takes value out at the average, like any other departure. A
        // count that found more takes the average with it: the extra units are the same part off
        // the same shelf, and the only price anybody could defend for them is what the rest cost.
        // Valuing a windfall at zero would say the company found something worthless, and valuing
        // it at a purchase price would mean inventing one.
        Money? value = delta.Value < 0m
            ? TakeValueOut(delta.Multiply(-1m), before)
            : AddValueAtAverage(delta, before);

        StockMovement movement = StockMovement.Record(
            Part, WarehouseId, MovementType.Adjustment, delta, OnHand, reference, now);

        if (value is not null)
        {
            movement.AtValue(value);
        }

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

        Quantity before = OnHand;
        OnHand = OnHand.Subtract(quantity);

        Money? value = TakeValueOut(quantity, before);

        StockMovement movement = StockMovement.Record(
            Part, WarehouseId, MovementType.Issue, quantity.Multiply(-1m), OnHand, reservation.Reference, now);

        if (value is not null)
        {
            movement.AtValue(value);
        }

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

    /// <summary>
    /// Records that a submitted purchase order line is bringing stock in.
    /// <para>
    /// Idempotent on the order line. The outbox delivers at-least-once and the inbox already
    /// makes application exactly-once, so this is belt and braces — but it is the cheap kind, and
    /// the failure it prevents is an expectation counted twice, which reads as stock arriving
    /// that never will.
    /// </para>
    /// </summary>
    /// <param name="purchaseOrderId">The order.</param>
    /// <param name="purchaseOrderLineId">Its line.</param>
    /// <param name="orderNumber">The order's number, for anyone asking where the goods are.</param>
    /// <param name="quantity">How much was ordered. Must be positive.</param>
    /// <param name="expectedOn">When it is expected, if the supplier has said.</param>
    public Result ExpectIncoming(
        PurchaseOrderRef purchaseOrderId,
        PurchaseOrderLineRef purchaseOrderLineId,
        string orderNumber,
        decimal quantity,
        DateOnly? expectedOn)
    {
        if (purchaseOrderId.IsEmpty || purchaseOrderLineId.IsEmpty)
        {
            return InventoryErrors.Stock.PurchaseOrderRequired;
        }

        if (_incoming.Exists(item => item.PurchaseOrderLineId == purchaseOrderLineId))
        {
            return Result.Success();
        }

        Result<Quantity> parsed = ParsePositive(quantity);
        if (parsed.IsFailure)
        {
            return Result.Failure(parsed.Error);
        }

        _incoming.Add(IncomingStock.Create(
            purchaseOrderId,
            purchaseOrderLineId,
            Truncate(orderNumber, IncomingStock.MaxOrderNumberLength),
            parsed.Value,
            expectedOn));

        OnOrder = OnOrder.Add(parsed.Value);

        return Result.Success();
    }

    /// <summary>
    /// Takes an arrival off the expected figure.
    /// <para>
    /// Deliberately cannot fail, and deliberately says nothing when the line is unknown. This runs
    /// beside the receipt that puts the goods on the shelf, and the goods being on the shelf is
    /// the fact that matters: a bookkeeping counter must never be the reason a delivery cannot be
    /// booked in. An unknown line means the submission never reached this module — an order placed
    /// before this was built, or an event that died in the dead letters — and the honest response
    /// is to leave the figure where it is rather than drive it negative.
    /// </para>
    /// </summary>
    /// <param name="purchaseOrderLineId">The line received against.</param>
    /// <param name="quantity">How much arrived.</param>
    /// <returns>How much of the arrival came off the on-order figure.</returns>
    public Quantity ReceiveIncoming(PurchaseOrderLineRef purchaseOrderLineId, decimal quantity)
    {
        IncomingStock? expected = _incoming.Find(
            item => item.PurchaseOrderLineId == purchaseOrderLineId);

        if (expected is null || quantity <= 0m)
        {
            return Quantity.Zero(Unit);
        }

        Result<Quantity> parsed = Quantity.Create(quantity, Unit);

        if (parsed.IsFailure)
        {
            return Quantity.Zero(Unit);
        }

        Quantity absorbed = expected.Receive(parsed.Value);
        OnOrder = OnOrder.Subtract(absorbed);

        return absorbed;
    }

    /// <summary>
    /// Stops expecting everything still outstanding on an order, because it was cancelled or
    /// closed short.
    /// <para>
    /// Checks the reorder point afterwards, and that check is the point of the method. The goods
    /// stopping is exactly the moment the part may need buying again — from somebody else, or in
    /// a hurry — and it is the one moment nothing else would notice, because no stock moved.
    /// </para>
    /// </summary>
    /// <param name="purchaseOrderId">The order that will not be delivering.</param>
    /// <returns>How many expectations were dropped.</returns>
    public int CancelIncoming(PurchaseOrderRef purchaseOrderId)
    {
        int dropped = 0;

        foreach (IncomingStock expected in _incoming)
        {
            if (expected.PurchaseOrderId != purchaseOrderId || !expected.IsOutstanding)
            {
                continue;
            }

            OnOrder = OnOrder.Subtract(expected.Cancel());
            dropped++;
        }

        if (dropped > 0)
        {
            CheckReorderPoint();
        }

        return dropped;
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

    /// <summary>
    /// True when the position has reached the level that should trigger a reorder.
    /// <para>
    /// Measured against <see cref="ProjectedAvailable"/>, not <see cref="Available"/>. A part with
    /// two on the shelf, a reorder point of ten and twenty arriving on Thursday does not need
    /// buying; a version of this that said it did would put the same line on the buyer's list
    /// every morning for two weeks, and a list that is wrong every morning is a list nobody reads.
    /// </para>
    /// </summary>
    public bool NeedsReplenishment =>
        ReorderPoint is { } point && ProjectedAvailable <= point;

    private void CheckReorderPoint()
    {
        if (ReorderPoint is not { } point || ReorderQuantity is not { } amount)
        {
            return;
        }

        if (ProjectedAvailable <= point)
        {
            Raise(new StockFellBelowReorderPointDomainEvent(
                Id, Part, WarehouseId, Available.Value, OnOrder.Value, point.Value, amount.Value));
        }
    }

    private static string Truncate(string? value, int maxLength)
    {
        string trimmed = value?.Trim() ?? string.Empty;

        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
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
