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
        string? priceSource,
        SalesOrderLineKind kind = SalesOrderLineKind.Goods,
        SalesOrderLineId? coreForLineId = null)
        : base(id)
    {
        Kind = kind;
        CoreForLineId = coreForLineId;
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
        ReturnedQuantity = Quantity.Zero(quantity.Unit);
        CreatedBy = string.Empty;
    }

    /// <summary>Required by EF Core materialization.</summary>
#pragma warning disable CS8618
    private SalesOrderLine()
    {
    }
#pragma warning restore CS8618

    /// <summary>
    /// Whether this line is goods or the deposit charged against the old unit.
    /// <para>
    /// A kind rather than a flag on the goods line, because the deposit is a separate thing the
    /// customer pays and gets back: it prints as its own line, it is credited on its own, and a
    /// starter motor bought without returning the old one is a customer who paid for both. A
    /// field on the goods line would have to be untangled from the price on every document.
    /// </para>
    /// </summary>
    public SalesOrderLineKind Kind { get; private set; }

    /// <summary>
    /// The goods line this deposit belongs to. Null on a goods line.
    /// <para>
    /// It is what makes the deposit follow the part: dispatching the starter motor charges the
    /// deposit, and neither is decided separately. Without it the deposit would be a line nobody
    /// ever dispatched and therefore a line nobody could ever invoice.
    /// </para>
    /// </summary>
    public SalesOrderLineId? CoreForLineId { get; private set; }

    /// <summary>True when this line is the deposit rather than the part.</summary>
    public bool IsCoreDeposit => Kind == SalesOrderLineKind.CoreDeposit;

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
    /// What one unit was worth on the shelf when this line was priced.
    /// <para>
    /// A snapshot, like the price and the SKU beside it, and it is not the cost of the sale. What
    /// a dispatch actually takes off the balance is stamped on the ledger row at the moment it
    /// happens, and the two differ whenever the shelf moves in between. This is the figure the
    /// decision was made against — "was that price worth taking?" — and it stays what it was so
    /// the answer to that question keeps reading the same way next month.
    /// </para>
    /// <para>
    /// Null when Inventory could not say: no stock record in that warehouse, an empty shelf, or a
    /// part that has never been through a priced receipt. Null is not zero. A line whose cost is
    /// unknown has no margin, and reporting one of a hundred per cent would be worse than
    /// reporting none.
    /// </para>
    /// </summary>
    public Money? UnitCost { get; private set; }

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

    /// <summary>
    /// How much of this line the customer has sent back.
    /// <para>
    /// Counted against what was dispatched, never against what was ordered — goods that never
    /// left cannot come back. It is not subtracted from the dispatched figure either: what went
    /// out went out, and a line that quietly un-dispatched itself would make the order look
    /// outstanding again and put it back on the picking list.
    /// </para>
    /// </summary>
    public Quantity ReturnedQuantity { get; private set; } = null!;

    /// <summary>How much of what went out is still with the customer.</summary>
    public Quantity ReturnableQuantity => DispatchedQuantity - ReturnedQuantity;

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

    /// <summary>
    /// True when this line can be shown a margin at all.
    /// <para>
    /// A deposit is excluded as well as an uncosted line. A core deposit is money held against an
    /// old unit, not goods sold: it has revenue and no cost, and counting it would make every
    /// starter motor look like the most profitable thing in the branch.
    /// </para>
    /// </summary>
    public bool HasMargin => UnitCost is not null && !IsCoreDeposit;

    /// <summary>What the goods on this line cost, at the figure the price was decided against.</summary>
    public Money? CostOfSale => HasMargin ? UnitCost!.Multiply(Quantity.Value) : null;

    /// <summary>What the line makes, before VAT. Null when there is no cost to compare against.</summary>
    public Money? Margin => CostOfSale is { } cost ? NetTotal - cost : null;

    /// <summary>
    /// The margin as a percentage of what the customer pays, before VAT.
    /// <para>
    /// Of revenue, not of cost. A part bought at 10 and sold at 15 is a third of the selling price
    /// and half the buying price, and both are called "fifty per cent" by somebody — so which one
    /// this is has to be stated rather than assumed. Revenue is the one a distributor's accounts
    /// are built on.
    /// </para>
    /// <para>
    /// Null when there is no cost, and also when the line is free: a giveaway has no margin
    /// percentage, and dividing by nothing to produce one would be inventing a number.
    /// </para>
    /// </summary>
    public decimal? MarginPercent =>
        Margin is { } margin && NetTotal.Amount != 0m
            ? decimal.Round(margin.Amount / NetTotal.Amount * 100m, 2, MidpointRounding.ToEven)
            : null;

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

    /// <summary>
    /// Creates the deposit line that belongs to a goods line.
    /// <para>
    /// No discount and no price source. A deposit is not a price somebody negotiated — it is a
    /// sum held against the old unit and given back unchanged, and discounting it would mean
    /// giving back more than was taken.
    /// </para>
    /// </summary>
    /// <param name="goods">The line the deposit is charged against.</param>
    /// <param name="unitDeposit">The deposit per unit, as the catalogue holds it.</param>
    internal static Result<SalesOrderLine> CreateCoreDeposit(SalesOrderLine goods, Money unitDeposit)
    {
        ArgumentNullException.ThrowIfNull(goods);
        ArgumentNullException.ThrowIfNull(unitDeposit);

        if (unitDeposit.Currency != goods.UnitPrice.Currency)
        {
            return SalesErrors.Line.CurrencyMismatch;
        }

        if (!unitDeposit.IsPositive)
        {
            return SalesErrors.Line.CoreDepositNotPositive;
        }

        return new SalesOrderLine(
            SalesOrderLineId.New(),
            goods.PartId,
            goods.Sku,
            $"Core deposit - {goods.Description}",
            goods.Quantity,
            unitDeposit,
            discountPercent: 0m,

            // The same rate as the part it is charged against. A deposit taken at one rate and
            // given back at another would leave the company holding the difference, and which
            // rate applies is a question about the supply, not about the deposit.
            goods.VatRatePercent,
            priceSource: null,
            SalesOrderLineKind.CoreDeposit,
            goods.Id);
    }

    /// <summary>Changes how much is being sold.</summary>
    internal Result ChangeQuantity(Quantity quantity)
    {
        ArgumentNullException.ThrowIfNull(quantity);

        // Not directly. The deposit is however many units of the part are being sold, and a
        // deposit for three against two starter motors is a customer owed money nobody took.
        if (IsCoreDeposit)
        {
            return SalesErrors.Line.CoreDepositFollowsItsPart;
        }

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

    /// <summary>Puts the deposit back in step with the part it belongs to.</summary>
    internal void MatchQuantityTo(Quantity quantity)
    {
        Quantity = quantity;
    }

    /// <summary>
    /// Records what the shelf was worth when this line was priced.
    /// <para>
    /// Set once, at creation, and never again — not when the price is overridden and not when the
    /// quantity changes. It is what the margin decision was made against, and a figure that
    /// quietly followed the shelf would make last month's decisions read as though somebody had
    /// made them on today's numbers.
    /// </para>
    /// </summary>
    internal void RecordUnitCost(Money? unitCost)
    {
        UnitCost = unitCost;
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

    /// <summary>
    /// Records goods coming back against this line.
    /// <para>
    /// The only rule here is arithmetic: no more can come back than went out and has not already
    /// come back. Whether they should have been taken back at all — the box is open, it was sold
    /// nine months ago, it is not the part we sold them — is a conversation at a counter, not an
    /// invariant, and a system that refused those would be overruled by somebody typing an
    /// adjustment instead.
    /// </para>
    /// </summary>
    internal Result RecordReturn(Quantity returned)
    {
        ArgumentNullException.ThrowIfNull(returned);

        if (returned.Unit != Quantity.Unit)
        {
            return SalesErrors.Line.UnitMismatch;
        }

        if (returned.Value <= 0m)
        {
            return SalesErrors.Line.QuantityNotPositive;
        }

        if (returned > ReturnableQuantity)
        {
            return SalesErrors.Return.ExceedsDispatched(
                ReturnableQuantity.Value, returned.Value, Quantity.Unit.Code);
        }

        ReturnedQuantity = ReturnedQuantity.Add(returned);

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

/// <summary>What a line on a sales order is.</summary>
public enum SalesOrderLineKind
{
    /// <summary>Unspecified. Never persisted.</summary>
    Unknown = 0,

    /// <summary>A part being sold. The only kind that moves stock.</summary>
    Goods = 1,

    /// <summary>
    /// The deposit held against a returnable old unit.
    /// <para>
    /// Charged with the goods and given back when the old unit comes in. It is money, not stock:
    /// nothing is reserved for it, nothing is picked, and the ledger never hears about it.
    /// </para>
    /// </summary>
    CoreDeposit = 2,
}
