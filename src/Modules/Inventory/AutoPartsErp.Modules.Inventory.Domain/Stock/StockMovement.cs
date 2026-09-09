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

    /// <summary>
    /// What this movement was worth, when a cost is known. Always the magnitude, never signed —
    /// the direction is in <see cref="Quantity"/>.
    /// <para>
    /// The <em>value</em> is stored and the unit cost is derived from it, not the other way round.
    /// <see cref="Money"/> rounds to the currency's decimal places, so a stored unit cost of
    /// €0.35 against 3,000 units would report €1,050.00 for a movement that actually took
    /// €1,046.87 off the balance sheet, and the ledger would stop tying out to the stock value it
    /// is supposed to explain. Storing what moved and dividing for display cannot drift.
    /// </para>
    /// <para>
    /// It also survives a change of costing method. A FIFO issue can span three cost layers at
    /// three different prices and has no single unit cost at all; it does have a value.
    /// </para>
    /// </summary>
    public Money? CostValue { get; private set; }

    /// <summary>
    /// The value spread over the quantity, for anybody who wants to read a per-unit figure.
    /// Rounded, and derived — never the number the balance sheet is built from.
    /// </summary>
    public Money? UnitCost =>
        CostValue is null || Quantity.Value == 0m
            ? null
            : CostValue.Divide(Math.Abs(Quantity.Value));

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

    /// <summary>
    /// What a quantity coming back is worth, from what the same document line took off the shelf.
    /// <para>
    /// The figure a customer return is booked back in at. It is deliberately not a unit cost
    /// multiplied up: <see cref="Money"/> rounds to the currency's decimal places, so dividing to
    /// find a per-unit figure and multiplying it back would round twice, and a return of three
    /// out of ten would not be three tenths of what the ten were worth. One multiplication by a
    /// fraction rounds once, which is the same reasoning that puts a value rather than a unit
    /// cost in <see cref="CostValue"/>.
    /// </para>
    /// <para>
    /// Proportional, because a line dispatched in two shipments has two movements at two costs
    /// and a customer bringing three of ten back does not say which shipment they came from.
    /// There is no more precise answer available, and inventing one would be a guess dressed up
    /// as a fact.
    /// </para>
    /// <para>
    /// Only movements carrying a value count, on both sides of the fraction. Treating a costless
    /// issue as one costing nothing would halve the figure every time stock that had never been
    /// through a priced receipt was mixed in, and write the difference off without saying so.
    /// </para>
    /// <para>
    /// Null when nothing that left had a value at all — a shelf that has never been priced. That
    /// is a real state, and it is not the same as a cost of zero, which would say the goods were
    /// free.
    /// </para>
    /// </summary>
    /// <param name="movements">The ledger rows for the line. Inbound ones are ignored.</param>
    /// <param name="returnedQuantity">How much is coming back.</param>
    public static Money? ValueOfReturn(IEnumerable<StockMovement> movements, decimal returnedQuantity)
    {
        ArgumentNullException.ThrowIfNull(movements);

        Money? issued = null;
        decimal units = 0m;

        foreach (StockMovement movement in movements)
        {
            if (movement.IsInbound || movement.CostValue is not { } value)
            {
                continue;
            }

            issued = issued is null ? value : issued + value;
            units += Math.Abs(movement.Quantity.Value);
        }

        return issued is null || units == 0m ? null : issued.Multiply(returnedQuantity / units);
    }

    /// <summary>Attaches the bin the stock came from or went to.</summary>
    public StockMovement InBin(BinId binId)
    {
        BinId = binId;
        return this;
    }

    /// <summary>
    /// Attaches what the movement was worth.
    /// <para>
    /// Internal, because the value has to come from the balance it was taken out of or added to.
    /// A caller free to stamp any figure here could write a ledger that does not add up to the
    /// stock value, and the ledger being reconstructible is the only reason it is worth keeping.
    /// </para>
    /// </summary>
    internal StockMovement AtValue(Money costValue)
    {
        CostValue = costValue;
        return this;
    }
}

/// <summary>
/// What left a warehouse on a transfer: the ledger row, and the value that went with it.
/// <para>
/// The value is returned rather than read off the movement afterwards because the receiving end
/// needs it as a number to add, and a caller that dug it out of the movement would be free to
/// pass a different one.
/// </para>
/// </summary>
/// <param name="Movement">The ledger row recording the departure.</param>
/// <param name="Value">
/// What the goods were worth on the sending shelf, or null when that shelf had no value — stock
/// that has never been through a priced receipt.
/// </param>
public sealed record TransferredStock(StockMovement Movement, Money? Value);
