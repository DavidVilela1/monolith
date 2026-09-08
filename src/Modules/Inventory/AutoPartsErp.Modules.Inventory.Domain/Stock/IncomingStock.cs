using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Inventory.Domain.Stock;

/// <summary>
/// Stock that has been ordered from a supplier and has not arrived.
/// <para>
/// The mirror image of <see cref="StockReservation"/>. A reservation is an outbound commitment
/// against stock that is physically present; this is an inbound commitment for stock that is not
/// present yet. They are modelled the same way for the same reason: a commitment that exists only
/// as a number in a column cannot be explained, audited, or corrected — it can only be trusted or
/// not, and after the first drift nobody trusts it.
/// </para>
/// <para>
/// This is why <c>OnOrder</c> is no longer a figure somebody sets. It is the sum of these rows,
/// and every change to it names the order line that caused it. "Why does it say fourteen coming?"
/// has an answer with a purchase order number in it, which is the only kind of answer worth
/// having when a buyer is standing there disagreeing with the screen.
/// </para>
/// </summary>
public sealed class IncomingStock : Entity<IncomingStockId>
{
    /// <summary>Longest permitted order number.</summary>
    public const int MaxOrderNumberLength = 30;

    private IncomingStock(
        IncomingStockId id,
        PurchaseOrderRef purchaseOrderId,
        PurchaseOrderLineRef purchaseOrderLineId,
        string orderNumber,
        Quantity quantity,
        DateOnly? expectedOn)
        : base(id)
    {
        PurchaseOrderId = purchaseOrderId;
        PurchaseOrderLineId = purchaseOrderLineId;
        OrderNumber = orderNumber;
        Quantity = quantity;
        ReceivedQuantity = Quantity.Zero(quantity.Unit);
        ExpectedOn = expectedOn;
        Status = IncomingStockStatus.Expected;
    }

    /// <summary>Required by EF Core materialization.</summary>
#pragma warning disable CS8618
    private IncomingStock()
    {
    }
#pragma warning restore CS8618

    /// <summary>The order the goods are coming on.</summary>
    public PurchaseOrderRef PurchaseOrderId { get; private set; }

    /// <summary>The line of that order.</summary>
    public PurchaseOrderLineRef PurchaseOrderLineId { get; private set; }

    /// <summary>The order's human-readable number, so the counter can quote it.</summary>
    public string OrderNumber { get; private set; } = string.Empty;

    /// <summary>How much was ordered on this line.</summary>
    public Quantity Quantity { get; private set; } = null!;

    /// <summary>How much of it has arrived.</summary>
    public Quantity ReceivedQuantity { get; private set; } = null!;

    /// <summary>When the supplier says it will arrive, when they have said.</summary>
    public DateOnly? ExpectedOn { get; private set; }

    /// <summary>Where the expectation stands.</summary>
    public IncomingStockStatus Status { get; private set; }

    /// <summary>How much is still expected on this line.</summary>
    public Quantity Outstanding => Quantity.Subtract(ReceivedQuantity);

    /// <summary>True while this line is still contributing to the on-order figure.</summary>
    public bool IsOutstanding =>
        Status == IncomingStockStatus.Expected && Outstanding.Value > 0m;

    /// <summary>Records an expectation against a submitted purchase order line.</summary>
    internal static IncomingStock Create(
        PurchaseOrderRef purchaseOrderId,
        PurchaseOrderLineRef purchaseOrderLineId,
        string orderNumber,
        Quantity quantity,
        DateOnly? expectedOn) =>
        new(
            IncomingStockId.New(),
            purchaseOrderId,
            purchaseOrderLineId,
            orderNumber,
            quantity,
            expectedOn);

    /// <summary>
    /// Books an arrival against this line and answers how much of it this line actually absorbed.
    /// <para>
    /// A delivery larger than what was ordered absorbs only what was outstanding. Suppliers do
    /// over-deliver, and the extra is real stock that belongs on the shelf — the receipt itself
    /// books it — but it was never on order, so it cannot come off a figure that never counted it.
    /// Letting it would drive on-order negative, which is not a state that means anything.
    /// </para>
    /// </summary>
    internal Quantity Receive(Quantity quantity)
    {
        ArgumentNullException.ThrowIfNull(quantity);

        if (!IsOutstanding)
        {
            return Quantity.Zero(Quantity.Unit);
        }

        Quantity outstanding = Outstanding;
        Quantity absorbed = quantity > outstanding ? outstanding : quantity;

        ReceivedQuantity = ReceivedQuantity.Add(absorbed);

        if (Outstanding.Value <= 0m)
        {
            Status = IncomingStockStatus.Received;
        }

        return absorbed;
    }

    /// <summary>
    /// Stops expecting the rest of this line, and answers how much stopped being expected.
    /// <para>
    /// Used for both a cancelled order and one closed short. The two are different conversations
    /// with the supplier and the same fact for the shelf: the balance is not coming.
    /// </para>
    /// </summary>
    internal Quantity Cancel()
    {
        if (!IsOutstanding)
        {
            return Quantity.Zero(Quantity.Unit);
        }

        Quantity dropped = Outstanding;
        Status = IncomingStockStatus.Cancelled;

        return dropped;
    }
}

/// <summary>Where an expected delivery stands.</summary>
public enum IncomingStockStatus
{
    /// <summary>Unspecified. Never persisted.</summary>
    Unknown = 0,

    /// <summary>Ordered and not yet here.</summary>
    Expected = 1,

    /// <summary>Everything ordered on the line has arrived.</summary>
    Received = 2,

    /// <summary>The order was cancelled or closed short; the balance is not coming.</summary>
    Cancelled = 3,
}
