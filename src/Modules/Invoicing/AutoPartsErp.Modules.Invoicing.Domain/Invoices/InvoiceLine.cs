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
        VatRate vatRate,
        InvoiceLineId? creditsLineId,
        SalesOrderLineRef? salesOrderLineId)
        : base(id)
    {
        CreditsLineId = creditsLineId;
        SalesOrderLineId = salesOrderLineId;
        Number = number;
        PartId = partId;
        Sku = sku;
        Description = description;
        Quantity = quantity;
        UnitPrice = unitPrice;
        DiscountPercent = discountPercent;
        VatRate = vatRate;
        CreditedQuantity = Quantity.Zero(quantity.Unit);
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

    /// <summary>
    /// The line of the original invoice that this one credits. Null on anything but a credit note.
    /// <para>
    /// Carried rather than matched on the part, because nothing stops a document listing the same
    /// part twice — a customer who bought ten now and four later, on one invoice — and a credit
    /// against "the brake pads" would then have two lines it could mean.
    /// </para>
    /// </summary>
    public InvoiceLineId? CreditsLineId { get; private set; }

    /// <summary>
    /// The sales order line this charges for, when the document was drawn from an order.
    /// <para>
    /// Null on a counter sale keyed straight into Invoicing, and null on a credit note — a credit
    /// note reverses an invoice, and what it does to the order behind that invoice is a question
    /// this system does not answer yet.
    /// </para>
    /// </summary>
    public SalesOrderLineRef? SalesOrderLineId { get; private set; }

    /// <summary>
    /// How much of this line has already been credited back.
    /// <para>
    /// Lives on the original invoice's line rather than being counted from the credit notes that
    /// point at it, and that is a deliberate departure from deriving what can be derived. The
    /// alternative is a query across documents every time somebody drafts a credit note, and the
    /// answer has to be right under two people drafting at once — this is a number on an
    /// aggregate protected by its own concurrency token, which is the only version of it that
    /// cannot be raced.
    /// </para>
    /// </summary>
    public Quantity CreditedQuantity { get; private set; } = null!;

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

    /// <summary>How much of this line can still be credited.</summary>
    public Quantity CreditableQuantity => Quantity - CreditedQuantity;

    /// <summary>True once the whole line has been credited back.</summary>
    public bool IsFullyCredited => CreditedQuantity >= Quantity;

    /// <summary>Creates a line. Called by <see cref="Invoice"/>, not directly.</summary>
    /// <param name="number">Its position on the document, from 1.</param>
    /// <param name="partId">The part sold.</param>
    /// <param name="sku">Its SKU.</param>
    /// <param name="description">Its description.</param>
    /// <param name="quantity">How much was sold.</param>
    /// <param name="unitPrice">The price per unit, before discount.</param>
    /// <param name="discountPercent">The discount given, 0 to 100.</param>
    /// <param name="vatRate">The VAT rate applied.</param>
    /// <param name="creditsLineId">The original line this one credits, on a credit note.</param>
    /// <param name="salesOrderLineId">
    /// The sales order line this charges for, when the document was drawn from an order. It is
    /// what lets Sales be told how much of each of its lines has been billed.
    /// </param>
    internal static Result<InvoiceLine> Create(
        int number,
        PartRef partId,
        string? sku,
        string? description,
        Quantity quantity,
        Money unitPrice,
        decimal discountPercent,
        VatRate vatRate,
        InvoiceLineId? creditsLineId = null,
        SalesOrderLineRef? salesOrderLineId = null)
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
            vatRate,
            creditsLineId,
            salesOrderLineId);
    }

    /// <summary>
    /// Records that some of this line has been credited back.
    /// <para>
    /// Called when a credit note is issued, never when one is drafted. A draft can be abandoned,
    /// and a line that counted abandoned drafts against itself would become uncreditable without
    /// anybody ever having been given money back.
    /// </para>
    /// </summary>
    /// <param name="credited">How much is being credited, in the unit the line was sold in.</param>
    internal Result Credit(Quantity credited)
    {
        ArgumentNullException.ThrowIfNull(credited);

        if (credited.Unit != Quantity.Unit)
        {
            return InvoicingErrors.Credit.UnitMismatch;
        }

        if (credited.Value <= 0m)
        {
            return InvoicingErrors.Credit.QuantityNotPositive;
        }

        Quantity remaining = CreditableQuantity;

        if (credited > remaining)
        {
            return InvoicingErrors.Credit.ExceedsInvoiced(Sku, remaining.Value);
        }

        CreditedQuantity += credited;

        return Result.Success();
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
