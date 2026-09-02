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

    private SalesOrderLine(
        SalesOrderLineId id,
        PartRef partId,
        string sku,
        string description,
        Quantity quantity,
        Money unitPrice,
        decimal discountPercent,
        decimal vatRatePercent)
        : base(id)
    {
        PartId = partId;
        Sku = sku;
        Description = description;
        Quantity = quantity;
        UnitPrice = unitPrice;
        DiscountPercent = discountPercent;
        VatRatePercent = vatRatePercent;
        DispatchedQuantity = Quantity.Zero(quantity.Unit);
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
    internal static Result<SalesOrderLine> Create(
        PartRef partId,
        string? sku,
        string? description,
        Quantity quantity,
        Money unitPrice,
        decimal discountPercent,
        decimal vatRatePercent)
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
            vatRatePercent);
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

    /// <summary>Changes the price or the discount.</summary>
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
