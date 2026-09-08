using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Sales.Domain.Orders;

/// <summary>
/// One part on a sales order: what was sold, at what price, less what discount, plus what VAT.
/// <para>
/// The four money figures are computed in a fixed order — extend, discount, net, VAT — and each
/// step rounds to the currency's precision as it goes. That order is not an implementation
/// detail: it is what a Portuguese invoice has to show line by line, and computing it any other
/// way produces totals that are out by a cent and an accountant who does not trust the system.
/// </para>
/// </summary>
public sealed class SalesOrderLine : Entity<SalesOrderLineId>, IAuditable, ITenantScoped
{
    /// <summary>Longest permitted SKU snapshot.</summary>
    public const int MaxSkuLength = 40;

    /// <summary>Longest permitted description.</summary>
    public const int MaxDescriptionLength = 200;

    /// <summary>Longest permitted price-source label.</summary>
    public const int MaxPriceSourceLength = 30;

    private SalesOrderLine(
        SalesOrderLineId id,
        PartRef partId,
        string sku,
        string description,
        Quantity quantity,
        Money unitPrice,
        decimal discountPercent,
        decimal vatRatePercent,
        string? priceSource)
        : base(id)
    {
        PartId = partId;
        Sku = sku;
        Description = description;
        Quantity = quantity;
        UnitPrice = unitPrice;
        DiscountPercent = discountPercent;
        VatRatePercent = vatRatePercent;
        PriceSource = priceSource;
        DispatchedQuantity = Quantity.Zero(quantity.Unit);
        InvoicedQuantity = Quantity.Zero(quantity.Unit);
        CreatedBy = string.Empty;
    }

    /// <summary>Required by EF Core materialization.</summary>
#pragma warning disable CS8618
    private SalesOrderLine()
    {
    }
#pragma warning restore CS8618

    /// <summary>The part being sold.</summary>
    public PartRef PartId { get; private set; }

    /// <summary>The SKU as it was when the order was taken.</summary>
    public string Sku { get; private set; } = string.Empty;

    /// <summary>The description as it was when the order was taken.</summary>
    public string Description { get; private set; } = string.Empty;

    /// <summary>How much was sold.</summary>
    public Quantity Quantity { get; private set; } = null!;

    /// <summary>How much has gone out so far.</summary>
    public Quantity DispatchedQuantity { get; private set; } = null!;

    /// <summary>The list price per unit, before discount.</summary>
    public Money UnitPrice { get; private set; } = null!;

    /// <summary>The discount given, as a percentage of the extended value.</summary>
    public decimal DiscountPercent { get; private set; }

    /// <summary>The VAT rate applied, snapshotted so a reprint matches the original.</summary>
    public decimal VatRatePercent { get; private set; }

    /// <summary>
    /// Where the price came from: the code of the price list that quoted it, or null when
    /// somebody typed it.
    /// <para>
    /// Three weeks after the invoice, "why did we charge that?" is a question somebody asks out
    /// loud, and the honest answers are "the trade list said so" and "Miguel overrode it". Both
    /// are fine; not knowing which is not. Re-deriving it later means re-running rules that have
    /// since moved, so the document records it at the moment it is decided.
    /// </para>
    /// </summary>
    public string? PriceSource { get; private set; }

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

    /// <summary>What is still to go out.</summary>
    public Quantity OutstandingQuantity => Quantity - DispatchedQuantity;

    /// <summary>
    /// How much of this line has been billed.
    /// <para>
    /// Counted against what was dispatched, never against what was ordered. Invoicing goods that
    /// have not shipped is a promise, and a promise with a document number on it is a problem: the
    /// number goes to the tax authority, the VAT falls due, and the only way back is a credit note
    /// for goods that never moved.
    /// </para>
    /// <para>
    /// It moves when a document is <i>issued</i>, never when one is drafted. A draft can be
    /// abandoned, and a line that counted abandoned drafts against itself would become unbillable
    /// without anybody ever having been charged - the same rule, for the same reason, as the
    /// credited quantity on an invoice line.
    /// </para>
    /// </summary>
    public Quantity InvoicedQuantity { get; private set; } = null!;

    /// <summary>What has gone out and not yet been charged for.</summary>
    public Quantity BillableQuantity => DispatchedQuantity - InvoicedQuantity;

    /// <summary>True once everything dispatched on this line has been charged for.</summary>
    public bool IsFullyInvoiced => InvoicedQuantity >= DispatchedQuantity;

    /// <summary>True while something has gone out that nobody has charged for yet.</summary>
    public bool IsBillable => BillableQuantity.Value > 0m;

    /// <summary>True once everything sold has gone out.</summary>
    public bool IsFullyDispatched => DispatchedQuantity >= Quantity;

    /// <summary>True while something is still owed to the customer.</summary>
    public bool IsOutstanding => !IsFullyDispatched;

    /// <summary>Unit price times quantity, before discount.</summary>
    public Money ExtendedPrice => UnitPrice * Quantity.Value;

    /// <summary>The discount given, in money.</summary>
    public Money DiscountAmount => ExtendedPrice.Percentage(DiscountPercent);

    /// <summary>What the customer pays for the line, before VAT.</summary>
    public Money NetTotal => ExtendedPrice - DiscountAmount;

    /// <summary>The VAT on the line.</summary>
    public Money VatAmount => NetTotal.Percentage(VatRatePercent);

    /// <summary>What the line adds to the invoice.</summary>
    public Money GrossTotal => NetTotal + VatAmount;

    /// <summary>Creates a line. Called by <see cref="SalesOrder.AddLine"/>, not directly.</summary>
    /// <param name="partId">The part being sold.</param>
    /// <param name="sku">Its SKU, snapshotted onto the document.</param>
    /// <param name="description">Its description, snapshotted onto the document.</param>
    /// <param name="quantity">How much to sell.</param>
    /// <param name="unitPrice">The list price per unit.</param>
    /// <param name="discountPercent">The discount given, 0 to 100.</param>
    /// <param name="vatRatePercent">The VAT rate, 0 to 100.</param>
    /// <param name="priceSource">
    /// The code of the price list the price came from, or null when it was typed by hand.
    /// </param>
    internal static Result<SalesOrderLine> Create(
        PartRef partId,
        string? sku,
        string? description,
        Quantity quantity,
        Money unitPrice,
        decimal discountPercent,
        decimal vatRatePercent,
        string? priceSource = null)
    {
        ArgumentNullException.ThrowIfNull(quantity);
        ArgumentNullException.ThrowIfNull(unitPrice);

        if (partId.IsEmpty)
        {
            return SalesErrors.Line.PartRequired;
        }

        if (quantity.Value <= 0m)
        {
            return SalesErrors.Line.QuantityNotPositive;
        }

        if (unitPrice.IsNegative)
        {
            return SalesErrors.Line.PriceNegative;
        }

        if (discountPercent is < 0m or > 100m)
        {
            return SalesErrors.Line.DiscountOutOfRange;
        }

        if (vatRatePercent is < 0m or > 100m)
        {
            return SalesErrors.Line.VatRateOutOfRange;
        }

        return new SalesOrderLine(
            SalesOrderLineId.New(),
            partId,
            Trim(sku, MaxSkuLength),
            Trim(description, MaxDescriptionLength),
            quantity,
            unitPrice,
            discountPercent,
            vatRatePercent,
            Trim(priceSource, MaxPriceSourceLength) is { Length: > 0 } source ? source : null);
    }

    /// <summary>Changes how much is being sold.</summary>
    internal Result ChangeQuantity(Quantity quantity)
    {
        ArgumentNullException.ThrowIfNull(quantity);

        if (quantity.Unit != Quantity.Unit)
        {
            return SalesErrors.Line.UnitMismatch;
        }

        if (quantity.Value <= 0m)
        {
            return SalesErrors.Line.QuantityNotPositive;
        }

        if (quantity < DispatchedQuantity)
        {
            return SalesErrors.Line.QuantityBelowDispatched;
        }

        Quantity = quantity;

        return Result.Success();
    }

    /// <summary>
    /// Changes the price or the discount, and forgets where the old price came from.
    /// <para>
    /// Clearing <see cref="PriceSource"/> is the whole point of the method having a comment. This
    /// is only reachable from somebody overriding a price by hand, and a line that still claimed
    /// TRADE26 after Miguel typed a different number would be a document answering "why did we
    /// charge that?" with a lie — the worst of the three possible answers, because it is the one
    /// nobody thinks to check.
    /// </para>
    /// </summary>
    internal Result ChangePricing(Money unitPrice, decimal discountPercent)
    {
        ArgumentNullException.ThrowIfNull(unitPrice);

        if (unitPrice.Currency != UnitPrice.Currency)
        {
            return SalesErrors.Line.CurrencyMismatch;
        }

        if (unitPrice.IsNegative)
        {
            return SalesErrors.Line.PriceNegative;
        }

        if (discountPercent is < 0m or > 100m)
        {
            return SalesErrors.Line.DiscountOutOfRange;
        }

        UnitPrice = unitPrice;
        DiscountPercent = discountPercent;
        PriceSource = null;

        return Result.Success();
    }

    /// <summary>Records goods leaving against this line.</summary>
    internal Result Dispatch(Quantity dispatched)
    {
        ArgumentNullException.ThrowIfNull(dispatched);

        if (dispatched.Unit != Quantity.Unit)
        {
            return SalesErrors.Line.UnitMismatch;
        }

        if (dispatched.Value <= 0m)
        {
            return SalesErrors.Line.DispatchNotPositive;
        }

        if (IsFullyDispatched)
        {
            return SalesErrors.Line.AlreadyDispatched;
        }

        Quantity outstanding = OutstandingQuantity;
        if (dispatched > outstanding)
        {
            return SalesErrors.Line.OverDispatch(outstanding.Value);
        }

        DispatchedQuantity += dispatched;

        return Result.Success();
    }

    /// <summary>Records that some of what went out has been charged for.</summary>
    /// <param name="billed">How much was billed. Positive, and no more than is billable.</param>
    internal Result Bill(Quantity billed)
    {
        ArgumentNullException.ThrowIfNull(billed);

        if (billed.Unit != Quantity.Unit)
        {
            return SalesErrors.Line.UnitMismatch;
        }

        if (billed.Value <= 0m)
        {
            return SalesErrors.Line.BilledNotPositive;
        }

        Quantity billable = BillableQuantity;

        if (billed > billable)
        {
            return SalesErrors.Line.OverBilled(Sku, billable.Value);
        }

        InvoicedQuantity += billed;

        return Result.Success();
    }

    /// <summary>
    /// Puts a billed quantity back, because the document that charged for it was voided.
    /// </summary>
    /// <param name="billed">How much to put back.</param>
    internal Result Unbill(Quantity billed)
    {
        ArgumentNullException.ThrowIfNull(billed);

        if (billed.Unit != Quantity.Unit)
        {
            return SalesErrors.Line.UnitMismatch;
        }

        if (billed > InvoicedQuantity)
        {
            return SalesErrors.Line.OverBilled(Sku, InvoicedQuantity.Value);
        }

        InvoicedQuantity -= billed;

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
