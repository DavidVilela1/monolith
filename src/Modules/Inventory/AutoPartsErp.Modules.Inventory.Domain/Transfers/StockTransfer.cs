using AutoPartsErp.Modules.Inventory.Domain.Transfers.Events;
using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Inventory.Domain.Transfers;

/// <summary>
/// Stock moving from one of the company's warehouses to another, and the days in between.
/// <para>
/// The reason this is a document and not two movements in one transaction: the goods are on a van
/// for two days. A transfer that issued from the branch and received into the depot in one breath
/// would put the stock on the depot's shelf on Monday, where a salesperson could promise it to a
/// customer collecting that afternoon. And if the van never arrives, the loss surfaces at the
/// depot weeks later as an unexplained count variance.
/// </para>
/// <para>
/// So it has two steps and a middle. Dispatching takes the stock off the sending shelf; receiving
/// puts what actually turned up on the receiving one; and what is in between belongs to neither.
/// </para>
/// <para>
/// <b>Transit is not a shelf.</b> There is no in-transit warehouse, deliberately: one would show
/// up in availability, in the replenishment list and on count sheets, and every one of those would
/// have to learn to exclude it. What is on the van lives on this document instead, as a quantity
/// and a value per line, and "what is moving right now" is a query against transfers.
/// </para>
/// <para>
/// <b>Value travels with the goods.</b> Moving stock between two of the company's own shelves must
/// not change what the company owns. Whatever value comes off the sender lands on this document,
/// and whatever is received lands on the receiver — to the cent. A version that re-derived the
/// cost at the far end would make every van journey a small silent revaluation.
/// </para>
/// </summary>
public sealed class StockTransfer : AggregateRoot<StockTransferId>, IAuditable, ITenantScoped
{
    /// <summary>Longest permitted transfer number.</summary>
    public const int MaxNumberLength = 30;

    /// <summary>Longest permitted free-text note or reason.</summary>
    public const int MaxNotesLength = 500;

    private readonly List<StockTransferLine> _lines = [];

    private StockTransfer(
        StockTransferId id,
        string number,
        WarehouseId fromWarehouseId,
        WarehouseId toWarehouseId,
        string? notes)
        : base(id)
    {
        Number = number;
        FromWarehouseId = fromWarehouseId;
        ToWarehouseId = toWarehouseId;
        Notes = notes;
        Status = StockTransferStatus.Draft;
        CreatedBy = string.Empty;
    }

    /// <summary>Required by EF Core materialization.</summary>
#pragma warning disable CS8618
    private StockTransfer()
    {
    }
#pragma warning restore CS8618

    /// <summary>The transfer's number, e.g. "TR-2026-00031".</summary>
    public string Number { get; private set; } = string.Empty;

    /// <summary>Where the stock is leaving.</summary>
    public WarehouseId FromWarehouseId { get; private set; }

    /// <summary>Where it is going.</summary>
    public WarehouseId ToWarehouseId { get; private set; }

    /// <summary>Where the transfer stands.</summary>
    public StockTransferStatus Status { get; private set; }

    /// <summary>When the van left.</summary>
    public DateTimeOffset? DispatchedAtUtc { get; private set; }

    /// <summary>Who sent it.</summary>
    public string? DispatchedBy { get; private set; }

    /// <summary>When the last of it was booked in.</summary>
    public DateTimeOffset? CompletedAtUtc { get; private set; }

    /// <summary>Anything worth recording about the movement.</summary>
    public string? Notes { get; private set; }

    /// <summary>Why it was cancelled, or why the shortfall was accepted.</summary>
    public string? ClosureReason { get; private set; }

    /// <summary>What is being moved.</summary>
    public IReadOnlyCollection<StockTransferLine> Lines => _lines.AsReadOnly();

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

    /// <summary>True while lines may still be added or changed.</summary>
    public bool IsDraft => Status == StockTransferStatus.Draft;

    /// <summary>True once nothing further will happen to it.</summary>
    public bool IsClosed => Status
        is StockTransferStatus.Received
        or StockTransferStatus.ClosedShort
        or StockTransferStatus.Cancelled;

    /// <summary>True while goods may still be booked in against it.</summary>
    public bool CanReceive => Status
        is StockTransferStatus.InTransit
        or StockTransferStatus.PartiallyReceived;

    /// <summary>True while something that left has not arrived.</summary>
    public bool HasStockInTransit => _lines.Exists(line => line.InTransitQuantity.Value > 0m);

    /// <summary>Opens a draft transfer between two warehouses.</summary>
    /// <param name="number">Its number, taken from the module's counter.</param>
    /// <param name="fromWarehouseId">Where the stock is leaving.</param>
    /// <param name="toWarehouseId">Where it is going.</param>
    /// <param name="notes">Anything worth recording.</param>
    public static Result<StockTransfer> Draft(
        string number,
        WarehouseId fromWarehouseId,
        WarehouseId toWarehouseId,
        string? notes = null)
    {
        if (string.IsNullOrWhiteSpace(number))
        {
            return InventoryErrors.Transfer.NumberRequired;
        }

        if (fromWarehouseId.IsEmpty || toWarehouseId.IsEmpty)
        {
            return InventoryErrors.Stock.WarehouseRequired;
        }

        // A transfer to itself is not a transfer. It would issue and receive the same stock in the
        // same place, which nets to nothing and leaves two ledger rows explaining it.
        if (fromWarehouseId == toWarehouseId)
        {
            return InventoryErrors.Transfer.SameWarehouse;
        }

        var transfer = new StockTransfer(
            StockTransferId.New(),
            number.Trim().ToUpperInvariant(),
            fromWarehouseId,
            toWarehouseId,
            Clean(notes, MaxNotesLength));

        return transfer;
    }

    /// <summary>Puts a part on the transfer.</summary>
    /// <param name="partId">The part to move.</param>
    /// <param name="sku">Its SKU, so the note is readable on paper.</param>
    /// <param name="description">Its description, for the same reason.</param>
    /// <param name="quantity">How much to send. Must be positive.</param>
    public Result<StockTransferLineId> AddLine(
        PartRef partId,
        string sku,
        string description,
        Quantity quantity)
    {
        ArgumentNullException.ThrowIfNull(quantity);

        if (!IsDraft)
        {
            return Result.Failure<StockTransferLineId>(InventoryErrors.Transfer.NotDraft);
        }

        if (partId.IsEmpty)
        {
            return Result.Failure<StockTransferLineId>(InventoryErrors.Stock.PartRequired);
        }

        if (quantity.Value <= 0m)
        {
            return Result.Failure<StockTransferLineId>(InventoryErrors.Stock.QuantityMustBePositive);
        }

        if (_lines.Exists(line => line.PartId == partId))
        {
            return Result.Failure<StockTransferLineId>(
                InventoryErrors.Transfer.PartAlreadyOnTransfer(sku));
        }

        var line = StockTransferLine.Create(
            _lines.Count + 1, partId, sku, description, quantity.Copy());

        _lines.Add(line);

        return line.Id;
    }

    /// <summary>Takes a line off a draft transfer.</summary>
    public Result RemoveLine(StockTransferLineId lineId)
    {
        if (!IsDraft)
        {
            return InventoryErrors.Transfer.NotDraft;
        }

        StockTransferLine? line = _lines.Find(item => item.Id == lineId);

        if (line is null)
        {
            return InventoryErrors.Transfer.LineNotFound(lineId.ToString());
        }

        _lines.Remove(line);

        // Renumbered so a printed note has no gaps in it. The identifiers do not move.
        for (int index = 0; index < _lines.Count; index++)
        {
            _lines[index].Renumber(index + 1);
        }

        return Result.Success();
    }

    /// <summary>
    /// Records that the goods have left, with the value that went with each line.
    /// <para>
    /// Called by the application layer after it has taken the stock off the sending balances, in
    /// the same transaction. The aggregate cannot do it itself: the balances are other aggregates
    /// in another part of the module, and this document is the paperwork around them.
    /// </para>
    /// </summary>
    /// <param name="values">
    /// What each line was worth on the way out. A line whose sending shelf had no value maps to
    /// null, which is different from zero — see <see cref="StockTransferLine.ValueInTransit"/>.
    /// </param>
    /// <param name="dispatchedBy">Who sent it.</param>
    /// <param name="now">The current instant.</param>
    public Result Dispatch(
        IReadOnlyDictionary<StockTransferLineId, Money?> values,
        string dispatchedBy,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (!IsDraft)
        {
            return InventoryErrors.Transfer.NotDraft;
        }

        if (_lines.Count == 0)
        {
            return InventoryErrors.Transfer.NoLines;
        }

        foreach (StockTransferLine line in _lines)
        {
            if (!values.ContainsKey(line.Id))
            {
                return InventoryErrors.Transfer.LineNotDispatched(line.Sku);
            }
        }

        foreach (StockTransferLine line in _lines)
        {
            line.Dispatch(values[line.Id]);
        }

        Status = StockTransferStatus.InTransit;
        DispatchedAtUtc = now;
        DispatchedBy = Clean(dispatchedBy, 120);

        Raise(new StockTransferDispatchedDomainEvent(
            Id, Number, FromWarehouseId, ToWarehouseId, _lines.Count));

        return Result.Success();
    }

    /// <summary>
    /// Books in what turned up, and answers what value came with it.
    /// <para>
    /// Partial by design and repeatable: a transfer can arrive on two vans, and the second one
    /// can be a week later. What is still outstanding stays on the document until it arrives or
    /// somebody accepts that it never will.
    /// </para>
    /// </summary>
    /// <param name="lineId">The line arriving.</param>
    /// <param name="quantity">How much of it. Must be positive and still in transit.</param>
    /// <param name="now">The current instant.</param>
    /// <returns>The proportional share of the transit value that arrived.</returns>
    public Result<Money?> Receive(StockTransferLineId lineId, Quantity quantity, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(quantity);

        if (!CanReceive)
        {
            return Result.Failure<Money?>(InventoryErrors.Transfer.NotInTransit);
        }

        StockTransferLine? line = _lines.Find(item => item.Id == lineId);

        if (line is null)
        {
            return Result.Failure<Money?>(InventoryErrors.Transfer.LineNotFound(lineId.ToString()));
        }

        Result<Money?> arrived = line.Receive(quantity);

        if (arrived.IsFailure)
        {
            return arrived;
        }

        UpdateStatus(now);

        return arrived;
    }

    /// <summary>
    /// Accepts that the balance is never arriving, and writes off what is still in transit.
    /// <para>
    /// The van was robbed, a pallet was dropped, or four boxes simply never turned up. The stock
    /// left the sending warehouse and its value left with it, so the company is already short;
    /// what this adds is a document saying so, with a reason on it, instead of a transfer that
    /// sits open for ever pretending the goods are still moving.
    /// </para>
    /// <para>
    /// No stock movement is written, and that is deliberate rather than an omission. Nothing moves
    /// at either end: the sending shelf gave the goods up at dispatch and the receiving shelf never
    /// had them. The loss is the gap between the <c>TransferOut</c> and the <c>TransferIn</c>, and
    /// this document is what explains it — the same role a count sheet plays for an adjustment.
    /// A general ledger will want a shrinkage posting; there is no general ledger yet.
    /// </para>
    /// </summary>
    /// <param name="reason">Why the shortfall was accepted. Somebody will ask.</param>
    /// <param name="now">The current instant.</param>
    public Result CloseShort(string? reason, DateTimeOffset now)
    {
        if (!CanReceive)
        {
            return InventoryErrors.Transfer.NotInTransit;
        }

        if (!HasStockInTransit)
        {
            return InventoryErrors.Transfer.NothingInTransit;
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return InventoryErrors.Transfer.CloseReasonRequired;
        }

        Money? lostValue = null;

        foreach (StockTransferLine line in _lines)
        {
            Money? lost = line.WriteOffInTransit();

            if (lost is not null)
            {
                lostValue = lostValue is null ? lost : lostValue.Add(lost);
            }
        }

        Status = StockTransferStatus.ClosedShort;
        ClosureReason = Clean(reason, MaxNotesLength);
        CompletedAtUtc = now;

        Raise(new StockTransferClosedShortDomainEvent(
            Id,
            Number,
            FromWarehouseId,
            ToWarehouseId,
            ClosureReason!,
            lostValue?.Amount ?? 0m,
            lostValue?.Currency.Code ?? Currency.Default.Code));

        return Result.Success();
    }

    /// <summary>
    /// Calls the transfer off before anything has left.
    /// <para>
    /// Only while it is a draft. Once goods are on a van there is nothing to cancel — either they
    /// arrive and are received, or they do not and the shortfall is written off.
    /// </para>
    /// </summary>
    /// <param name="reason">Why.</param>
    public Result Cancel(string? reason)
    {
        if (!IsDraft)
        {
            return Status == StockTransferStatus.Cancelled
                ? InventoryErrors.Transfer.AlreadyClosed
                : InventoryErrors.Transfer.CannotCancelInTransit;
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return InventoryErrors.Transfer.CloseReasonRequired;
        }

        Status = StockTransferStatus.Cancelled;
        ClosureReason = Clean(reason, MaxNotesLength);

        return Result.Success();
    }

    private void UpdateStatus(DateTimeOffset now)
    {
        if (HasStockInTransit)
        {
            Status = StockTransferStatus.PartiallyReceived;
            return;
        }

        Status = StockTransferStatus.Received;
        CompletedAtUtc = now;

        Raise(new StockTransferReceivedDomainEvent(Id, Number, FromWarehouseId, ToWarehouseId));
    }

    private static string? Clean(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string trimmed = value.Trim();

        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}

/// <summary>Where a transfer stands.</summary>
public enum StockTransferStatus
{
    /// <summary>Unspecified. Never persisted.</summary>
    Unknown = 0,

    /// <summary>Being built. Nothing has left.</summary>
    Draft = 1,

    /// <summary>The goods have left and none of them have arrived.</summary>
    InTransit = 2,

    /// <summary>Some of it has arrived and some is still on its way.</summary>
    PartiallyReceived = 3,

    /// <summary>Everything that left has arrived. Terminal.</summary>
    Received = 4,

    /// <summary>The balance never arrived and somebody accepted it. Terminal.</summary>
    ClosedShort = 5,

    /// <summary>Called off before anything left. Terminal.</summary>
    Cancelled = 6,
}
