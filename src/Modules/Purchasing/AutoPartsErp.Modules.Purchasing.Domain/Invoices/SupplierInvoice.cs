using AutoPartsErp.Modules.Purchasing.Domain.Invoices.Events;
using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Purchasing.Domain.Invoices;

/// <summary>
/// What a supplier is charging for a delivery, written by the system rather than by a person.
/// <para>
/// <b>This is their document, not ours.</b> It never touches the certified series in Invoicing and
/// never gets a number of ours: what is stored here is what <i>they</i> sent, alongside what the
/// system expected them to send. Numbering it would be forging it.
/// </para>
/// <para>
/// It is drafted from receipts. The warehouse counts a pallet — which stays a person's job,
/// because a person is the only thing that can tell a full box from an empty one — and everything
/// counted that day against that supplier lands on one draft, priced from the agreement and
/// rebated at the rate the period had reached. Nobody types a line.
/// </para>
/// <para>
/// When the supplier's paper arrives, the only things entered are their number, their date and
/// their total. The document then either agrees with what was counted and agreed, or it does not,
/// and the difference is put in front of somebody. Automatic entry is not automatic acceptance:
/// the point of computing the figure independently is to have something to disagree with.
/// </para>
/// </summary>
public sealed class SupplierInvoice : AggregateRoot<SupplierInvoiceId>, IAuditable, ISoftDeletable, ITenantScoped
{
    /// <summary>Longest permitted supplier document number.</summary>
    public const int MaxDocumentNumberLength = 60;

    /// <summary>Longest permitted note or reason.</summary>
    public const int MaxReasonLength = 500;

    private readonly List<SupplierInvoiceLine> _lines = [];

    private SupplierInvoice(
        SupplierInvoiceId id,
        SupplierRef supplierId,
        string supplierCode,
        Currency currency,
        DateOnly receivedOn,
        decimal rappelRatePercent)
        : base(id)
    {
        SupplierId = supplierId;
        SupplierCode = supplierCode;
        CurrencyCode = currency.Code;
        ReceivedOn = receivedOn;
        RappelRatePercent = rappelRatePercent;
        Status = SupplierInvoiceStatus.Drafted;
        CreatedBy = string.Empty;
    }

    /// <summary>Required by EF Core materialization.</summary>
#pragma warning disable CS8618
    private SupplierInvoice()
    {
    }
#pragma warning restore CS8618

    /// <summary>The supplier.</summary>
    public SupplierRef SupplierId { get; private set; }

    /// <summary>Their short code, snapshotted onto the document.</summary>
    public string SupplierCode { get; private set; } = string.Empty;

    /// <summary>The currency the document is in.</summary>
    public string CurrencyCode { get; private set; } = Currency.Default.Code;

    /// <summary>
    /// The day the goods were counted, which is what groups receipts onto one draft.
    /// <para>
    /// A supplier invoices a delivery, and a delivery is a van on a day. Grouping by order instead
    /// would split one van across three documents whenever the warehouse ordered twice; grouping by
    /// nothing at all would leave a draft accumulating for a month.
    /// </para>
    /// </summary>
    public DateOnly ReceivedOn { get; private set; }

    /// <summary>Where the document is in its life.</summary>
    public SupplierInvoiceStatus Status { get; private set; }

    /// <summary>
    /// The rebate rate taken off this document, stamped when the draft was opened.
    /// <para>
    /// Stamped rather than looked up, and never recomputed. The rate depends on what the period
    /// had bought when the delivery arrived, and a figure that quietly followed the year's running
    /// total would make a document raised in March re-explain itself with November's numbers every
    /// time somebody opened it.
    /// </para>
    /// <para>
    /// Zero when the rebate is settled by credit note instead. The company pays the full figure
    /// and is credited later, and taking it off here as well is how the same discount gets taken
    /// twice.
    /// </para>
    /// </summary>
    public decimal RappelRatePercent { get; private set; }

    /// <summary>Their document number, once their paper has arrived.</summary>
    public string? SupplierDocumentNumber { get; private set; }

    /// <summary>The date on their document.</summary>
    public DateOnly? DocumentDate { get; private set; }

    /// <summary>
    /// What their document says the whole thing comes to, including VAT.
    /// <para>
    /// Stored beside what the system worked out rather than instead of it. Keeping both is what
    /// lets somebody answer "why are we paying 12,40 more than we counted?" a month later, and
    /// overwriting ours with theirs would be the system agreeing with the supplier and then losing
    /// the evidence that it ever thought otherwise.
    /// </para>
    /// </summary>
    public Money? StatedGrossTotal { get; private set; }

    /// <summary>Why the document is in dispute, or why it was cancelled.</summary>
    public string? Reason { get; private set; }

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

    /// <inheritdoc />
    public bool IsDeleted { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? DeletedAtUtc { get; set; }

    /// <inheritdoc />
    public string? DeletedBy { get; set; }

    /// <summary>Its lines, one per receipt that landed on it.</summary>
    public IReadOnlyList<SupplierInvoiceLine> Lines => _lines;

    /// <summary>The currency the document is in.</summary>
    public Currency Currency => Currency.FromCode(CurrencyCode);

    /// <summary>True while receipts can still land on this draft.</summary>
    public bool IsOpen => Status == SupplierInvoiceStatus.Drafted;

    /// <summary>True once nothing more will happen to it.</summary>
    public bool IsClosed => Status is SupplierInvoiceStatus.Matched or SupplierInvoiceStatus.Cancelled;

    /// <summary>What the lines come to before the rebate and before VAT.</summary>
    public Money LinesTotal => Sum(line => line.LineTotal);

    /// <summary>
    /// The rebate in money, computed on the lines rather than accumulated per line.
    /// <para>
    /// Rounded once, at the end. Four per cent taken off each of eleven lines and added up is not
    /// the same figure as four per cent of the eleven together, and the supplier's document will
    /// have done it the second way.
    /// </para>
    /// </summary>
    public Money RappelAmount => LinesTotal.Percentage(RappelRatePercent);

    /// <summary>What is owed before VAT: the lines, less the rebate.</summary>
    public Money NetTotal => LinesTotal - RappelAmount;

    /// <summary>
    /// The VAT, computed per rate on the net after the rebate.
    /// <para>
    /// After, not before, and that ordering is not a preference. A rebate reduces the taxable
    /// amount, so VAT charged on the pre-rebate figure is VAT the company would be deducting
    /// without having paid it.
    /// </para>
    /// </summary>
    public Money VatTotal
    {
        get
        {
            Money lines = LinesTotal;

            if (lines.Amount == 0m)
            {
                return Money.Zero(Currency);
            }

            Money vat = Money.Zero(Currency);

            // Per rate, because a delivery can carry parts at 23 and books at 6, and one blended
            // percentage would be a figure that matches neither the supplier's document nor the
            // return the company has to file.
            foreach (IGrouping<decimal, SupplierInvoiceLine> band in _lines.GroupBy(line => line.VatRatePercent))
            {
                Money bandTotal = band.Aggregate(
                    Money.Zero(Currency), (running, line) => running + line.LineTotal);

                // The rebate is spread across the bands in proportion to what each one is worth,
                // so a document with two rates still adds up to the same net as one with one.
                Money bandRebate = RappelAmount.Amount == 0m
                    ? Money.Zero(Currency)
                    : Money.Of(
                        decimal.Round(
                            RappelAmount.Amount * bandTotal.Amount / lines.Amount,
                            Currency.DecimalPlaces,
                            MidpointRounding.ToEven),
                        Currency);

                vat += (bandTotal - bandRebate).Percentage(band.Key);
            }

            return vat;
        }
    }

    /// <summary>What the company owes for this delivery.</summary>
    public Money GrossTotal => NetTotal + VatTotal;

    /// <summary>
    /// What their document says, less what the system worked out. Null until their paper arrives.
    /// <para>
    /// Positive means they are charging more than was counted and agreed.
    /// </para>
    /// </summary>
    public Money? Difference => StatedGrossTotal is null ? null : StatedGrossTotal - GrossTotal;

    /// <summary>
    /// Opens a draft for one supplier's delivery on one day.
    /// </summary>
    /// <param name="supplierId">The supplier.</param>
    /// <param name="supplierCode">Their short code.</param>
    /// <param name="currency">The currency the agreement is in.</param>
    /// <param name="receivedOn">The day the goods were counted.</param>
    /// <param name="rappelRatePercent">
    /// The rebate to take off this document, from the agreement. Zero when there is none, and zero
    /// when the rebate arrives as a credit note instead.
    /// </param>
    public static Result<SupplierInvoice> DraftFor(
        SupplierRef supplierId,
        string? supplierCode,
        Currency currency,
        DateOnly receivedOn,
        decimal rappelRatePercent = 0m)
    {
        ArgumentNullException.ThrowIfNull(currency);

        if (supplierId.IsEmpty)
        {
            return PurchasingErrors.Agreement.SupplierRequired;
        }

        if (string.IsNullOrWhiteSpace(supplierCode))
        {
            return PurchasingErrors.Agreement.SupplierCodeRequired;
        }

        if (rappelRatePercent is < 0m or >= 100m)
        {
            return PurchasingErrors.Rappel.RateOutOfRange;
        }

        var invoice = new SupplierInvoice(
            SupplierInvoiceId.New(),
            supplierId,
            supplierCode.Trim().ToUpperInvariant(),
            currency,
            receivedOn,
            rappelRatePercent);

        invoice.Raise(new SupplierInvoiceDraftedDomainEvent(
            invoice.Id, supplierId, invoice.SupplierCode, receivedOn, rappelRatePercent));

        return invoice;
    }

    /// <summary>
    /// Puts what the warehouse counted onto the draft, at the agreed price.
    /// </summary>
    /// <param name="purchaseOrderId">The order the goods were ordered on.</param>
    /// <param name="purchaseOrderLineId">The line they arrived against.</param>
    /// <param name="partId">The part.</param>
    /// <param name="sku">Its SKU.</param>
    /// <param name="description">Its description.</param>
    /// <param name="quantity">How much was counted.</param>
    /// <param name="unitPrice">What one unit costs, from the agreement.</param>
    /// <param name="vatRatePercent">The VAT rate, 0 to 100.</param>
    /// <param name="priceSource">The agreed price the figure came from, when one did.</param>
    public Result<SupplierInvoiceLineId> AddReceivedLine(
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

        if (!IsOpen)
        {
            return PurchasingErrors.Invoice.NotOpen;
        }

        if (unitPrice.Currency != Currency)
        {
            return PurchasingErrors.Invoice.CurrencyMismatch;
        }

        Result<SupplierInvoiceLine> line = SupplierInvoiceLine.Create(
            purchaseOrderId, purchaseOrderLineId, partId, sku, description, quantity, unitPrice,
            vatRatePercent, priceSource);

        if (line.IsFailure)
        {
            return Result.Failure<SupplierInvoiceLineId>(line.Error);
        }

        _lines.Add(line.Value);

        return line.Value.Id;
    }

    /// <summary>
    /// Records what the supplier's paper says, and decides whether it agrees.
    /// <para>
    /// The tolerance is not slack, it is arithmetic. Both sides round to the cent, and a document
    /// of forty lines at two VAT rates will differ by one or two of them however carefully either
    /// side computes it. A tolerance of zero would put every delivery in front of a person to
    /// approve a difference of a cent, which is how people learn to approve everything without
    /// looking.
    /// </para>
    /// </summary>
    /// <param name="documentNumber">Their document number, as printed.</param>
    /// <param name="documentDate">The date on their document.</param>
    /// <param name="statedGrossTotal">What their document says the whole thing comes to.</param>
    /// <param name="tolerance">
    /// How far apart the two figures may be and still count as agreeing. Cents, not euros.
    /// </param>
    public Result Reconcile(
        string? documentNumber,
        DateOnly documentDate,
        Money statedGrossTotal,
        Money tolerance)
    {
        ArgumentNullException.ThrowIfNull(statedGrossTotal);
        ArgumentNullException.ThrowIfNull(tolerance);

        if (!IsOpen)
        {
            return PurchasingErrors.Invoice.NotOpen;
        }

        if (_lines.Count == 0)
        {
            return PurchasingErrors.Invoice.NoLines;
        }

        if (string.IsNullOrWhiteSpace(documentNumber))
        {
            return PurchasingErrors.Invoice.DocumentNumberRequired;
        }

        if (documentNumber.Trim().Length > MaxDocumentNumberLength)
        {
            return PurchasingErrors.Invoice.DocumentNumberTooLong;
        }

        if (statedGrossTotal.Currency != Currency || tolerance.Currency != Currency)
        {
            return PurchasingErrors.Invoice.CurrencyMismatch;
        }

        if (documentDate < ReceivedOn)
        {
            return PurchasingErrors.Invoice.DocumentBeforeDelivery;
        }

        SupplierDocumentNumber = documentNumber.Trim();
        DocumentDate = documentDate;
        StatedGrossTotal = statedGrossTotal;

        Money difference = Difference!;

        if (Math.Abs(difference.Amount) > Math.Abs(tolerance.Amount))
        {
            Status = SupplierInvoiceStatus.Disputed;

            Raise(new SupplierInvoiceDisputedDomainEvent(
                Id, SupplierId, SupplierDocumentNumber, GrossTotal, statedGrossTotal, difference));

            return Result.Success();
        }

        Settle();

        return Result.Success();
    }

    /// <summary>
    /// Accepts the supplier's figure on a disputed document, and says who decided and why.
    /// <para>
    /// A deliberate act with a name on it, not a button that makes a warning go away. Somebody is
    /// agreeing to pay more than was counted at prices that were agreed, and in a year's time the
    /// only thing that will explain it is the sentence they wrote here.
    /// </para>
    /// <para>
    /// It does not touch the agreed price. A supplier overcharging on one delivery is a
    /// conversation about that delivery; a supplier who has genuinely raised their prices is a new
    /// agreed price with a date on it, recorded on purpose — otherwise one unchallenged invoice
    /// quietly becomes the new contract.
    /// </para>
    /// </summary>
    /// <param name="reason">Why the difference is being accepted.</param>
    public Result AcceptSupplierFigure(string? reason)
    {
        if (Status != SupplierInvoiceStatus.Disputed)
        {
            return PurchasingErrors.Invoice.NotDisputed;
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return PurchasingErrors.Invoice.AcceptReasonRequired;
        }

        if (reason.Trim().Length > MaxReasonLength)
        {
            return PurchasingErrors.Invoice.ReasonTooLong;
        }

        Reason = reason.Trim();

        Settle();

        return Result.Success();
    }

    /// <summary>
    /// Sends the document back to draft so a receipt can be corrected, keeping what was disputed.
    /// </summary>
    /// <param name="reason">What is being corrected.</param>
    public Result Reopen(string? reason)
    {
        if (Status != SupplierInvoiceStatus.Disputed)
        {
            return PurchasingErrors.Invoice.NotDisputed;
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return PurchasingErrors.Invoice.ReopenReasonRequired;
        }

        Reason = reason.Trim();
        Status = SupplierInvoiceStatus.Drafted;

        return Result.Success();
    }

    /// <summary>
    /// Corrects one line to what the supplier's document actually charges.
    /// <para>
    /// For the case where their price is right and the agreement is stale. Reachable only while
    /// the document is back in draft, so correcting a line is always something somebody did after
    /// looking at a difference rather than before anyone noticed one.
    /// </para>
    /// </summary>
    /// <param name="lineId">The line.</param>
    /// <param name="unitPrice">What their document says one unit costs.</param>
    public Result AcceptLinePrice(SupplierInvoiceLineId lineId, Money unitPrice)
    {
        ArgumentNullException.ThrowIfNull(unitPrice);

        if (!IsOpen)
        {
            return PurchasingErrors.Invoice.NotOpen;
        }

        SupplierInvoiceLine? line = _lines.Find(candidate => candidate.Id == lineId);

        return line is null
            ? PurchasingErrors.Invoice.LineNotFound(lineId.ToString())
            : line.AcceptPrice(unitPrice);
    }

    /// <summary>
    /// Cancels the document. For a draft that was opened by mistake, never for one already
    /// settled — a settled document is money somebody owes, and it goes away by being credited.
    /// </summary>
    /// <param name="reason">Why.</param>
    public Result Cancel(string? reason)
    {
        if (Status == SupplierInvoiceStatus.Matched)
        {
            return PurchasingErrors.Invoice.AlreadySettled;
        }

        if (Status == SupplierInvoiceStatus.Cancelled)
        {
            return PurchasingErrors.Invoice.AlreadyCancelled;
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return PurchasingErrors.Invoice.CancelReasonRequired;
        }

        Reason = reason.Trim();
        Status = SupplierInvoiceStatus.Cancelled;

        Raise(new SupplierInvoiceCancelledDomainEvent(Id, SupplierId, Reason));

        return Result.Success();
    }

    private void Settle()
    {
        Status = SupplierInvoiceStatus.Matched;

        // The figure that leaves is the one the company will pay, which is the supplier's — a
        // payable opened for what the system computed would never match the money going out of
        // the bank, and reconciling those two later is the job this whole document exists to
        // avoid.
        Raise(new SupplierInvoiceSettledDomainEvent(
            Id,
            SupplierId,
            SupplierCode,
            SupplierDocumentNumber!,
            DocumentDate!.Value,
            NetTotal.Amount,
            VatTotal.Amount,
            StatedGrossTotal!.Amount,
            CurrencyCode));
    }

    private Money Sum(Func<SupplierInvoiceLine, Money> selector) =>
        _lines.Aggregate(Money.Zero(Currency), (running, line) => running + selector(line));
}

/// <summary>Where a supplier's document is in its life.</summary>
public enum SupplierInvoiceStatus
{
    /// <summary>Not set.</summary>
    Unknown = 0,

    /// <summary>Being built from receipts. Their paper has not arrived yet.</summary>
    Drafted = 1,

    /// <summary>Their paper agrees with what was counted, or somebody accepted that it does not.</summary>
    Matched = 2,

    /// <summary>Their paper says something different and nobody has decided what to do about it.</summary>
    Disputed = 3,

    /// <summary>Opened by mistake and withdrawn before anything was owed against it.</summary>
    Cancelled = 4,
}
