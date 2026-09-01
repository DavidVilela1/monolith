using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Purchasing.Domain.Orders;

/// <summary>
/// One part on a purchase order: what was asked for, at what price, and how much of it has
/// turned up so far.
/// <para>
/// A line is an entity rather than a value object because it has a life of its own — goods are
/// received against <i>this</i> line, over several deliveries, and each receipt has to find its
/// way back to the right row. Two lines for the same part at the same price are still two
/// different things once one of them is half delivered.
/// </para>
/// <para>
/// It is reached only through its <see cref="PurchaseOrder"/>. There is no line repository, and
/// no way to change a line without going through the order that owns it, which is what keeps
/// the order status and the line quantities from drifting apart.
/// </para>
/// </summary>
public sealed class PurchaseOrderLine : Entity<PurchaseOrderLineId>, IAuditable, ITenantScoped
{
    /// <summary>Longest permitted SKU snapshot.</summary>
    public const int MaxSkuLength = 40;

    /// <summary>Longest permitted description.</summary>
    public const int MaxDescriptionLength = 200;

    private PurchaseOrderLine(
        PurchaseOrderLineId id,
        PartRef partId,
        string sku,
        string description,
        Quantity quantity,
        Money unitPrice)
        : base(id)
    {
        PartId = partId;
        Sku = sku;
        Description = description;
        Quantity = quantity;
        UnitPrice = unitPrice;
        ReceivedQuantity = Quantity.Zero(quantity.Unit);
        CreatedBy = string.Empty;
    }

    /// <summary>Required by EF Core materialization.</summary>
#pragma warning disable CS8618
    private PurchaseOrderLine()
    {
    }
#pragma warning restore CS8618

    // No PurchaseOrderId property: the line is reached only through its order, and the foreign
    // key lives in the mapping as a shadow property. A line that carried its owner's id would
    // be a second place for that relationship to be wrong.

    /// <summary>The part being bought.</summary>
    public PartRef PartId { get; private set; }

    /// <summary>
    /// The SKU as it was when the order was raised.
    /// <para>
    /// Copied rather than looked up, on purpose. A purchase order is a document that was sent to
    /// somebody: reprinting it two years later must show what they actually received, not what
    /// the catalogue says today. The same reasoning applies to the description and the price.
    /// </para>
    /// </summary>
    public string Sku { get; private set; } = string.Empty;

    /// <summary>The description as it was when the order was raised.</summary>
    public string Description { get; private set; } = string.Empty;

    /// <summary>How much was ordered.</summary>
    public Quantity Quantity { get; private set; } = null!;

    /// <summary>How much has arrived so far, across every delivery against this line.</summary>
    public Quantity ReceivedQuantity { get; private set; } = null!;

    /// <summary>The agreed price per unit.</summary>
    public Money UnitPrice { get; private set; } = null!;

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

    /// <summary>What is still to come.</summary>
    public Quantity OutstandingQuantity => Quantity - ReceivedQuantity;

    /// <summary>True once everything ordered has arrived.</summary>
    public bool IsFullyReceived => ReceivedQuantity >= Quantity;

    /// <summary>True while something is still expected.</summary>
    public bool IsOutstanding => !IsFullyReceived;

    /// <summary>The value of the line: unit price times quantity ordered.</summary>
    public Money LineTotal => UnitPrice * Quantity.Value;

    /// <summary>The value still to be delivered.</summary>
    public Money OutstandingValue => UnitPrice * OutstandingQuantity.Value;

    /// <summary>Creates a line. Called by <see cref="PurchaseOrder.AddLine"/>, not directly.</summary>
    /// <param name="partId">The part being bought.</param>
    /// <param name="sku">The part's SKU, snapshotted onto the document.</param>
    /// <param name="description">The part's description, snapshotted onto the document.</param>
    /// <param name="quantity">How much to order.</param>
    /// <param name="unitPrice">The agreed price per unit.</param>
    internal static Result<PurchaseOrderLine> Create(
        PartRef partId,
        string? sku,
        string? description,
        Quantity quantity,
        Money unitPrice)
    {
        ArgumentNullException.ThrowIfNull(quantity);
        ArgumentNullException.ThrowIfNull(unitPrice);

        if (partId.IsEmpty)
        {
            return PurchasingErrors.Line.PartRequired;
        }

        if (quantity.Value <= 0m)
        {
            return PurchasingErrors.Line.QuantityNotPositive;
        }

        if (unitPrice.IsNegative)
        {
            return PurchasingErrors.Line.PriceNegative;
        }

        return new PurchaseOrderLine(
            PurchaseOrderLineId.New(),
            partId,
            Trim(sku, MaxSkuLength),
            Trim(description, MaxDescriptionLength),
            quantity,
            unitPrice);
    }

    /// <summary>Changes how much is being ordered.</summary>
    internal Result ChangeQuantity(Quantity quantity)
    {
        ArgumentNullException.ThrowIfNull(quantity);

        if (quantity.Unit != Quantity.Unit)
        {
            return PurchasingErrors.Line.UnitMismatch;
        }

        if (quantity.Value <= 0m)
        {
            return PurchasingErrors.Line.QuantityNotPositive;
        }

        if (quantity < ReceivedQuantity)
        {
            return PurchasingErrors.Line.QuantityBelowReceived;
        }

        Quantity = quantity;

        return Result.Success();
    }

    /// <summary>Changes the agreed price.</summary>
    internal Result ChangeUnitPrice(Money unitPrice)
    {
        ArgumentNullException.ThrowIfNull(unitPrice);

        if (unitPrice.Currency != UnitPrice.Currency)
        {
            return PurchasingErrors.Line.CurrencyMismatch;
        }

        if (unitPrice.IsNegative)
        {
            return PurchasingErrors.Line.PriceNegative;
        }

        UnitPrice = unitPrice;

        return Result.Success();
    }

    /// <summary>
    /// Books in a delivery against this line.
    /// <para>
    /// Over-receipt is refused rather than absorbed. A supplier who sends 120 of something you
    /// ordered 100 of has changed the deal, and the person who has to pay the invoice should be
    /// the one who decides whether to accept it — not a silently permissive quantity check.
    /// </para>
    /// </summary>
    /// <param name="received">How much arrived on this delivery.</param>
    internal Result Receive(Quantity received)
    {
        ArgumentNullException.ThrowIfNull(received);

        if (received.Unit != Quantity.Unit)
        {
            return PurchasingErrors.Line.UnitMismatch;
        }

        if (received.Value <= 0m)
        {
            return PurchasingErrors.Line.ReceiptNotPositive;
        }

        if (IsFullyReceived)
        {
            return PurchasingErrors.Line.AlreadyFullyReceived;
        }

        Quantity outstanding = OutstandingQuantity;
        if (received > outstanding)
        {
            return PurchasingErrors.Line.OverReceipt(outstanding.Value);
        }

        ReceivedQuantity += received;

        return Result.Success();
    }

    private static string Trim(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string trimmed = value.Trim();

        return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }
}
