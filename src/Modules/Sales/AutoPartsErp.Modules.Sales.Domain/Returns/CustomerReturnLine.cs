using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Sales.Domain.Returns;

/// <summary>
/// One part coming back, at the price it went out at.
/// <para>
/// The price, the discount and the VAT rate are copied from the original order line rather than
/// looked up. The customer is being given back what they paid, and what they paid is a fact about
/// a document that has already been issued — re-deriving it would mean re-running price rules
/// that have since moved and crediting a figure nobody was ever charged.
/// </para>
/// </summary>
public sealed class CustomerReturnLine : Entity<CustomerReturnLineId>, IAuditable, ITenantScoped
{
    /// <summary>Longest permitted SKU snapshot.</summary>
    public const int MaxSkuLength = 40;

    /// <summary>Longest permitted description.</summary>
    public const int MaxDescriptionLength = 200;

    /// <summary>Longest permitted note about the state the goods arrived in.</summary>
    public const int MaxConditionNoteLength = 300;

    private CustomerReturnLine(
        CustomerReturnLineId id,
        SalesOrderLineId salesOrderLineId,
        PartRef partId,
        string sku,
        string description,
        Quantity quantity,
        Money unitPrice,
        decimal discountPercent,
        decimal vatRatePercent,
        ReturnDisposition disposition,
        string? conditionNote)
        : base(id)
    {
        SalesOrderLineId = salesOrderLineId;
        PartId = partId;
        Sku = sku;
        Description = description;
        Quantity = quantity;
        UnitPrice = unitPrice;
        DiscountPercent = discountPercent;
        VatRatePercent = vatRatePercent;
        Disposition = disposition;
        ConditionNote = conditionNote;
        CreatedBy = string.Empty;
    }

    /// <summary>Required by EF Core materialization.</summary>
#pragma warning disable CS8618
    private CustomerReturnLine()
    {
    }
#pragma warning restore CS8618

    /// <summary>The line of the original order these goods went out on.</summary>
    public SalesOrderLineId SalesOrderLineId { get; private set; }

    /// <summary>The part coming back.</summary>
    public PartRef PartId { get; private set; }

    /// <summary>Its SKU, as it was on the order.</summary>
    public string Sku { get; private set; } = string.Empty;

    /// <summary>Its description, as it was on the order.</summary>
    public string Description { get; private set; } = string.Empty;

    /// <summary>How much is coming back.</summary>
    public Quantity Quantity { get; private set; } = null!;

    /// <summary>What they paid per unit.</summary>
    public Money UnitPrice { get; private set; } = null!;

    /// <summary>The discount they had on the original line.</summary>
    public decimal DiscountPercent { get; private set; }

    /// <summary>The VAT rate on the original line.</summary>
    public decimal VatRatePercent { get; private set; }

    /// <summary>Whether it goes back on the shelf or is written off.</summary>
    public ReturnDisposition Disposition { get; private set; }

    /// <summary>What state it arrived in, for whoever decides what happens to it next.</summary>
    public string? ConditionNote { get; private set; }

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

    /// <summary>Unit price times quantity, before discount.</summary>
    public Money ExtendedPrice => UnitPrice * Quantity.Value;

    /// <summary>The discount, in money.</summary>
    public Money DiscountAmount => ExtendedPrice.Percentage(DiscountPercent);

    /// <summary>What the customer is credited for the line, before VAT.</summary>
    public Money NetTotal => ExtendedPrice - DiscountAmount;

    /// <summary>The VAT on the line.</summary>
    public Money VatAmount => NetTotal.Percentage(VatRatePercent);

    /// <summary>What the line adds to the credit.</summary>
    public Money GrossTotal => NetTotal + VatAmount;

    /// <summary>True when these goods rejoin the balance.</summary>
    public bool GoesBackToStock => Disposition == ReturnDisposition.BackToStock;

    /// <summary>Creates a line. Called by <see cref="CustomerReturn.AddLine"/>, not directly.</summary>
    internal static Result<CustomerReturnLine> Create(
        SalesOrderLineId salesOrderLineId,
        PartRef partId,
        string? sku,
        string? description,
        Quantity quantity,
        Money unitPrice,
        decimal discountPercent,
        decimal vatRatePercent,
        ReturnDisposition disposition,
        string? conditionNote)
    {
        ArgumentNullException.ThrowIfNull(quantity);
        ArgumentNullException.ThrowIfNull(unitPrice);

        if (salesOrderLineId.IsEmpty)
        {
            return SalesErrors.Return.OrderLineRequired;
        }

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

        // Refused rather than defaulted. "Unknown" would mean Inventory has to guess whether a
        // part is on a shelf, and the safe guess and the useful guess are different ones.
        if (disposition == ReturnDisposition.Unknown)
        {
            return SalesErrors.Return.DispositionRequired;
        }

        return new CustomerReturnLine(
            CustomerReturnLineId.New(),
            salesOrderLineId,
            partId,
            Trim(sku, MaxSkuLength),
            Trim(description, MaxDescriptionLength),
            quantity,
            unitPrice,
            discountPercent,
            vatRatePercent,
            disposition,
            Trim(conditionNote, MaxConditionNoteLength) is { Length: > 0 } note ? note : null);
    }

    private static string Trim(string? value, int maxLength)
    {
        string trimmed = (value ?? string.Empty).Trim();

        return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }
}
