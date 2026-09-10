using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Purchasing.Domain.Invoices;

/// <summary>
/// One line of what a supplier is charging, drawn from what the warehouse actually counted.
/// <para>
/// The quantity comes from the receipt and the price from the agreement. Neither is typed, and
/// that is the whole point: the two numbers on a purchase document that are worth arguing about
/// are the ones both sides already agreed on months apart, and a person retyping them is a person
/// who will eventually retype one of them wrong.
/// </para>
/// </summary>
public sealed class SupplierInvoiceLine : Entity<SupplierInvoiceLineId>, IAuditable, ITenantScoped
{
    /// <summary>Longest permitted description.</summary>
    public const int MaxDescriptionLength = 200;

    private SupplierInvoiceLine(
        SupplierInvoiceLineId id,
        PurchaseOrderId purchaseOrderId,
        PurchaseOrderLineId purchaseOrderLineId,
        PartRef partId,
        string sku,
        string description,
        Quantity quantity,
        Money unitPrice,
        decimal vatRatePercent)
        : base(id)
    {
        PurchaseOrderId = purchaseOrderId;
        PurchaseOrderLineId = purchaseOrderLineId;
        PartId = partId;
        Sku = sku;
        Description = description;
        Quantity = quantity;
        UnitPrice = unitPrice;
        VatRatePercent = vatRatePercent;
        CreatedBy = string.Empty;
    }

    /// <summary>Required by EF Core materialization.</summary>
#pragma warning disable CS8618
    private SupplierInvoiceLine()
    {
    }
#pragma warning restore CS8618

    /// <summary>The invoice this line belongs to.</summary>
    public SupplierInvoiceId SupplierInvoiceId { get; private set; }

    /// <summary>The order the goods were ordered on.</summary>
    public PurchaseOrderId PurchaseOrderId { get; private set; }

    /// <summary>
    /// The order line the goods arrived against.
    /// <para>
    /// What makes the three-way match possible at all: this is the thread from what was ordered,
    /// through what was counted off the van, to what is being charged. Without it, reconciling a
    /// supplier's statement means matching on description, and two brake pad sets with different
    /// part numbers have the same description.
    /// </para>
    /// </summary>
    public PurchaseOrderLineId PurchaseOrderLineId { get; private set; }

    /// <summary>The part.</summary>
    public PartRef PartId { get; private set; }

    /// <summary>Its SKU, snapshotted onto the document.</summary>
    public string Sku { get; private set; } = string.Empty;

    /// <summary>Its description, snapshotted onto the document.</summary>
    public string Description { get; private set; } = string.Empty;

    /// <summary>How much the warehouse counted.</summary>
    public Quantity Quantity { get; private set; } = null!;

    /// <summary>What one unit costs, from the agreement, before any rebate.</summary>
    public Money UnitPrice { get; private set; } = null!;

    /// <summary>The VAT rate on the line.</summary>
    public decimal VatRatePercent { get; private set; }

    /// <summary>Where the price came from, or null when nothing priced it and it had to be typed.</summary>
    public SupplierPriceId? PriceSource { get; private set; }

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

    /// <summary>What the line is worth before any rebate and before VAT.</summary>
    public Money LineTotal => UnitPrice * Quantity.Value;

    /// <summary>Creates a line. Called by <see cref="SupplierInvoice.AddReceivedLine"/>, not directly.</summary>
    /// <param name="purchaseOrderId">The order the goods were ordered on.</param>
    /// <param name="purchaseOrderLineId">The line they arrived against.</param>
    /// <param name="partId">The part.</param>
    /// <param name="sku">Its SKU.</param>
    /// <param name="description">Its description.</param>
    /// <param name="quantity">How much the warehouse counted.</param>
    /// <param name="unitPrice">What one unit costs, from the agreement.</param>
    /// <param name="vatRatePercent">The VAT rate, 0 to 100.</param>
    /// <param name="priceSource">The agreed price the figure came from, when one did.</param>
    internal static Result<SupplierInvoiceLine> Create(
        PurchaseOrderId purchaseOrderId,
        PurchaseOrderLineId purchaseOrderLineId,
        PartRef partId,
        string? sku,
        string? description,
        Quantity quantity,
        Money unitPrice,
        decimal vatRatePercent,
        SupplierPriceId? priceSource = null)
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

        if (!unitPrice.IsPositive)
        {
            return PurchasingErrors.Agreement.PriceNotPositive;
        }

        if (vatRatePercent is < 0m or > 100m)
        {
            return PurchasingErrors.Invoice.VatRateOutOfRange;
        }

        var line = new SupplierInvoiceLine(
            SupplierInvoiceLineId.New(),
            purchaseOrderId,
            purchaseOrderLineId,
            partId,
            (sku ?? string.Empty).Trim(),
            Clean(description),
            quantity,
            unitPrice,
            vatRatePercent);

        line.PriceSource = priceSource;

        return line;
    }

    /// <summary>
    /// Corrects the price on a line, because the supplier's document disagreed with the agreement
    /// and somebody decided the supplier was right.
    /// <para>
    /// The agreed price is not touched. A supplier charging more than was agreed on one delivery
    /// is a conversation about that delivery; a supplier who has genuinely put their prices up is
    /// a new agreed price with a date on it, recorded deliberately. Letting this write back would
    /// mean one unchallenged invoice quietly becoming the new contract.
    /// </para>
    /// </summary>
    /// <param name="unitPrice">What their document says one unit costs.</param>
    internal Result AcceptPrice(Money unitPrice)
    {
        ArgumentNullException.ThrowIfNull(unitPrice);

        if (unitPrice.Currency != UnitPrice.Currency)
        {
            return PurchasingErrors.Agreement.PriceCurrencyMismatch;
        }

        if (!unitPrice.IsPositive)
        {
            return PurchasingErrors.Agreement.PriceNotPositive;
        }

        UnitPrice = unitPrice;

        // The line no longer says what it was priced from, because it is no longer priced from
        // anything: somebody looked at their paper and accepted the figure on it.
        PriceSource = null;

        return Result.Success();
    }

    private static string Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string trimmed = value.Trim();

        return trimmed.Length <= MaxDescriptionLength
            ? trimmed
            : trimmed[..MaxDescriptionLength];
    }
}
