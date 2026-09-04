using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Invoicing.Domain.Invoices;

/// <summary>
/// One line of an invoice: what was sold, at what price, less what discount, plus what VAT.
/// <para>
/// A snapshot, and more completely a snapshot than anything else in this system. A sales order
/// line is a working document that can still be corrected; an invoice line is a legal record of
/// what a customer was told they owed on a particular day. Nothing on it is ever recomputed from
/// a source that might have moved — not the description, not the price, not the VAT rate.
/// </para>
/// <para>
/// The arithmetic runs in the fixed order every Portuguese invoice shows line by line: extend,
/// discount, net, VAT — rounding to the currency's precision at each step. Doing it any other way
/// gives totals a cent out from what the page says, which is the kind of thing that costs a
/// morning to explain and never quite gets believed.
/// </para>
/// </summary>
public sealed class InvoiceLine : Entity<InvoiceLineId>, ITenantScoped
{
    /// <summary>Longest permitted SKU.</summary>
    public const int MaxSkuLength = 40;

    /// <summary>Longest permitted description.</summary>
    public const int MaxDescriptionLength = 200;

    private InvoiceLine(
        InvoiceLineId id,
        int number,
        PartRef partId,
        string sku,
        string description,
        Quantity quantity,
        Money unitPrice,
        decimal discountPercent,
        VatRate vatRate)
        : base(id)
    {
        Number = number;
        PartId = partId;
        Sku = sku;
        Description = description;
        Quantity = quantity;
        UnitPrice = unitPrice;
        DiscountPercent = discountPercent;
        VatRate = vatRate;
    }

    /// <summary>Required by EF Core materialization.</summary>
#pragma warning disable CS8618
    private InvoiceLine()
    {
    }
#pragma warning restore CS8618

    /// <summary>
    /// The line's position on the document, from 1.
    /// <para>
    /// Stored rather than derived from the order of a collection. A SAF-T export carries
    /// <c>LineNumber</c> and a reprint has to match the original exactly, and neither can depend
    /// on the order rows happen to come back from a database.
    /// </para>
    /// </summary>
    public int Number { get; private set; }

    /// <summary>The part sold.</summary>
    public PartRef PartId { get; private set; }

    /// <summary>Its SKU, as it was on the day.</summary>
    public string Sku { get; private set; } = string.Empty;

    /// <summary>Its description, as it was on the day.</summary>
    public string Description { get; private set; } = string.Empty;

    /// <summary>How much was sold.</summary>
    public Quantity Quantity { get; private set; } = null!;

    /// <summary>The price per unit, before discount.</summary>
    public Money UnitPrice { get; private set; } = null!;

    /// <summary>The discount given, as a percentage.</summary>
    public decimal DiscountPercent { get; private set; }

    /// <summary>The VAT rate applied, with its exemption reason where there is one.</summary>
    public VatRate VatRate { get; private set; } = null!;

    /// <inheritdoc />
    public Guid TenantId { get; set; }

    /// <summary>Unit price times quantity, before discount.</summary>
    public Money ExtendedPrice => UnitPrice * Quantity.Value;

    /// <summary>The discount, in money.</summary>
    public Money DiscountAmount => ExtendedPrice.Percentage(DiscountPercent);

    /// <summary>What the line is worth before VAT.</summary>
    public Money NetAmount => ExtendedPrice - DiscountAmount;

    /// <summary>The VAT on the line.</summary>
    public Money VatAmount => NetAmount.Percentage(VatRate.Percent);

    /// <summary>What the line adds to the document total.</summary>
    public Money GrossAmount => NetAmount + VatAmount;

    /// <summary>Creates a line. Called by <see cref="Invoice"/>, not directly.</summary>
    /// <param name="number">Its position on the document, from 1.</param>
    /// <param name="partId">The part sold.</param>
    /// <param name="sku">Its SKU.</param>
    /// <param name="description">Its description.</param>
    /// <param name="quantity">How much was sold.</param>
    /// <param name="unitPrice">The price per unit, before discount.</param>
    /// <param name="discountPercent">The discount given, 0 to 100.</param>
    /// <param name="vatRate">The VAT rate applied.</param>
    internal static Result<InvoiceLine> Create(
        int number,
        PartRef partId,
        string? sku,
        string? description,
        Quantity quantity,
        Money unitPrice,
        decimal discountPercent,
        VatRate vatRate)
    {
        ArgumentNullException.ThrowIfNull(quantity);
        ArgumentNullException.ThrowIfNull(unitPrice);
        ArgumentNullException.ThrowIfNull(vatRate);

        if (number < 1)
        {
            return InvoicingErrors.Line.NumberNotPositive;
        }

        if (partId.IsEmpty)
        {
            return InvoicingErrors.Line.PartRequired;
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            return InvoicingErrors.Line.DescriptionRequired;
        }

        if (quantity.Value <= 0m)
        {
            return InvoicingErrors.Line.QuantityNotPositive;
        }

        if (unitPrice.IsNegative)
        {
            return InvoicingErrors.Line.PriceNegative;
        }

        if (discountPercent is < 0m or > 100m)
        {
            return InvoicingErrors.Line.DiscountOutOfRange;
        }

        return new InvoiceLine(
            InvoiceLineId.New(),
            number,
            partId,
            Trim(sku, MaxSkuLength),
            Trim(description, MaxDescriptionLength),
            quantity,
            unitPrice,
            discountPercent,
            vatRate);
    }

    private static string Trim(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string trimmed = value.Trim();

        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
