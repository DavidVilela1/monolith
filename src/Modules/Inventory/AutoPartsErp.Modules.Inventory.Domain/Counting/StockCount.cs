using AutoPartsErp.Modules.Inventory.Domain.Counting.Events;
using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Inventory.Domain.Counting;

/// <summary>
/// A physical count of stock in a warehouse: what the system thought was there, what somebody
/// found, and the difference between the two.
/// <para>
/// Until this existed, correcting stock was one command. A person typed a number, the balance
/// became that number, and a movement recorded a sentence explaining it. That is the mechanism by
/// which stock quietly disappears from a distributor: there is no sheet, so there is nothing to
/// review; no snapshot, so nobody can tell whether the difference was found or created; and no
/// separation between counting and accepting, so the person who miscounted is the person who
/// signs it off.
/// </para>
/// <para>
/// The three things this adds, and each is one of those gaps: a <b>sheet</b> that lists what was
/// in scope before anybody walked the aisle, a <b>snapshot</b> of the system quantity taken when
/// the sheet was opened, and a <b>posting step</b> separate from counting. The ad-hoc adjustment
/// still exists and should — a part dropped on the floor this morning does not need a count
/// sheet — but it is now the exception rather than the only way.
/// </para>
/// <para>
/// What this deliberately does not do is freeze the warehouse. Stock keeps moving while a count
/// runs, because in a parts distributor it always does. What it does instead is notice: posting
/// compares the live balance against the snapshot, and a line whose stock moved since it was
/// counted is flagged rather than silently overwritten. The count is an assertion about a shelf
/// at a moment, and if the moment has passed somebody should decide whether it still holds.
/// </para>
/// </summary>
public sealed class StockCount : AggregateRoot<StockCountId>, IAuditable, ITenantScoped
{
    /// <summary>Longest permitted count number.</summary>
    public const int MaxNumberLength = 30;

    /// <summary>Longest permitted free-text note.</summary>
    public const int MaxNotesLength = 500;

    private readonly List<StockCountLine> _lines = [];

    private StockCount(
        StockCountId id,
        string number,
        WarehouseId warehouseId,
        DateOnly countedOn,
        string? notes)
        : base(id)
    {
        Number = number;
        WarehouseId = warehouseId;
        CountedOn = countedOn;
        Notes = notes;
        Status = StockCountStatus.Open;
        CreatedBy = string.Empty;
    }

    /// <summary>Required by EF Core materialization.</summary>
#pragma warning disable CS8618
    private StockCount()
    {
    }
#pragma warning restore CS8618

    /// <summary>The sheet's number, e.g. "SC-2026-00014".</summary>
    public string Number { get; private set; } = string.Empty;

    /// <summary>The warehouse being counted. A sheet never spans two.</summary>
    public WarehouseId WarehouseId { get; private set; }

    /// <summary>The day the count is for.</summary>
    public DateOnly CountedOn { get; private set; }

    /// <summary>Where the sheet stands.</summary>
    public StockCountStatus Status { get; private set; }

    /// <summary>Anything worth recording about the count.</summary>
    public string? Notes { get; private set; }

    /// <summary>Who said the counting was finished.</summary>
    public string? SubmittedBy { get; private set; }

    /// <summary>When they said it.</summary>
    public DateTimeOffset? SubmittedAtUtc { get; private set; }

    /// <summary>Who accepted the differences and applied them to stock.</summary>
    public string? PostedBy { get; private set; }

    /// <summary>When they did.</summary>
    public DateTimeOffset? PostedAtUtc { get; private set; }

    /// <summary>Why it was abandoned.</summary>
    public string? CancellationReason { get; private set; }

    /// <summary>The lines on the sheet.</summary>
    public IReadOnlyCollection<StockCountLine> Lines => _lines.AsReadOnly();

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

    /// <summary>True while lines may still be counted.</summary>
    public bool IsOpen => Status == StockCountStatus.Open;

    /// <summary>True once nothing further will happen to the sheet.</summary>
    public bool IsClosed => Status is StockCountStatus.Posted or StockCountStatus.Cancelled;

    /// <summary>How many lines somebody has actually counted.</summary>
    public int CountedLines => _lines.Count(line => line.IsCounted);

    /// <summary>How many lines nobody has been to yet.</summary>
    public int UncountedLines => _lines.Count - CountedLines;

    /// <summary>True when at least one counted line disagrees with the system.</summary>
    public bool HasVariances => _lines.Exists(line => line.HasVariance);

    /// <summary>
    /// Opens a sheet against a warehouse.
    /// </summary>
    /// <param name="number">The sheet's number, taken from the module's counter.</param>
    /// <param name="warehouseId">The warehouse being counted.</param>
    /// <param name="countedOn">The day the count is for.</param>
    /// <param name="notes">Anything worth recording.</param>
    public static Result<StockCount> Open(
        string number,
        WarehouseId warehouseId,
        DateOnly countedOn,
        string? notes = null)
    {
        if (string.IsNullOrWhiteSpace(number))
        {
            return InventoryErrors.Count.NumberRequired;
        }

        if (warehouseId.IsEmpty)
        {
            return InventoryErrors.Stock.WarehouseRequired;
        }

        var count = new StockCount(
            StockCountId.New(),
            number.Trim().ToUpperInvariant(),
            warehouseId,
            countedOn,
            Clean(notes, MaxNotesLength));

        count.Raise(new StockCountOpenedDomainEvent(count.Id, count.Number, warehouseId));

        return count;
    }

    /// <summary>
    /// Puts a part on the sheet, with what the system believes is on the shelf right now.
    /// <para>
    /// The system quantity is captured here and never recalculated. That snapshot is the whole
    /// value of the sheet as a document: without it, a difference discovered at posting time
    /// cannot be told apart from stock that legitimately moved while the count was running, and
    /// "the count was wrong" and "somebody sold four of them on Tuesday" are very different
    /// conversations.
    /// </para>
    /// </summary>
    /// <param name="partId">The part to count.</param>
    /// <param name="sku">Its SKU, so the sheet is readable on paper.</param>
    /// <param name="description">Its description, for the same reason.</param>
    /// <param name="systemQuantity">What the system says is there, now.</param>
    public Result<StockCountLineId> AddLine(
        PartRef partId,
        string sku,
        string description,
        Quantity systemQuantity)
    {
        ArgumentNullException.ThrowIfNull(systemQuantity);

        if (!IsOpen)
        {
            return Result.Failure<StockCountLineId>(InventoryErrors.Count.NotOpen);
        }

        if (partId.IsEmpty)
        {
            return Result.Failure<StockCountLineId>(InventoryErrors.Stock.PartRequired);
        }

        if (_lines.Exists(line => line.PartId == partId))
        {
            return Result.Failure<StockCountLineId>(
                InventoryErrors.Count.PartAlreadyOnSheet(sku));
        }

        var line = StockCountLine.Create(
            _lines.Count + 1, partId, sku, description, systemQuantity.Copy());

        _lines.Add(line);

        return line.Id;
    }

    /// <summary>
    /// Records what somebody found on the shelf.
    /// <para>
    /// Zero is a perfectly good answer and means the shelf was empty. It is not the same as a
    /// line nobody has been to, which is why the counted quantity is nullable and why an
    /// uncounted line is skipped at posting rather than posted as nothing. Conflating the two
    /// would write off every part on the sheet that the counter ran out of time for.
    /// </para>
    /// </summary>
    /// <param name="lineId">The line counted.</param>
    /// <param name="countedQuantity">What was found. May be zero, never negative.</param>
    /// <param name="countedBy">Who counted it.</param>
    /// <param name="now">The current instant.</param>
    public Result RecordCount(
        StockCountLineId lineId,
        Quantity countedQuantity,
        string countedBy,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(countedQuantity);

        if (!IsOpen)
        {
            return InventoryErrors.Count.NotOpen;
        }

        StockCountLine? line = _lines.Find(item => item.Id == lineId);

        if (line is null)
        {
            return InventoryErrors.Count.LineNotFound(lineId.ToString());
        }

        return line.Record(countedQuantity, countedBy, now);
    }

    /// <summary>
    /// Says the counting is finished and the sheet is ready to be reviewed.
    /// <para>
    /// A separate step from posting, and that separation is the point of the whole aggregate. A
    /// count that finds €4,000 of stock missing is a decision, not a data entry, and the person
    /// who walked the aisle should not be the last person to see the number.
    /// </para>
    /// </summary>
    /// <param name="submittedBy">Who finished counting.</param>
    /// <param name="now">The current instant.</param>
    public Result Submit(string submittedBy, DateTimeOffset now)
    {
        if (!IsOpen)
        {
            return InventoryErrors.Count.NotOpen;
        }

        if (_lines.Count == 0)
        {
            return InventoryErrors.Count.NoLines;
        }

        if (CountedLines == 0)
        {
            return InventoryErrors.Count.NothingCounted;
        }

        Status = StockCountStatus.Submitted;
        SubmittedBy = Clean(submittedBy, 120);
        SubmittedAtUtc = now;

        Raise(new StockCountSubmittedDomainEvent(
            Id, Number, WarehouseId, CountedLines, UncountedLines));

        return Result.Success();
    }

    /// <summary>
    /// Sends a submitted sheet back for more counting.
    /// <para>
    /// The alternative to approving a number nobody believes. Without it the reviewer's only
    /// options are to accept a bad count or cancel the sheet and lose the work.
    /// </para>
    /// </summary>
    public Result Reopen()
    {
        if (Status != StockCountStatus.Submitted)
        {
            return InventoryErrors.Count.NotSubmitted;
        }

        Status = StockCountStatus.Open;
        SubmittedBy = null;
        SubmittedAtUtc = null;

        return Result.Success();
    }

    /// <summary>
    /// Marks the sheet as applied to stock.
    /// <para>
    /// Called by the application layer <em>after</em> it has applied every counted line to its
    /// balance, in the same transaction. The aggregate does not apply them itself because it
    /// cannot: the balances are other aggregates, in numbers this sheet does not hold, and a
    /// count of a whole warehouse touches thousands of them.
    /// </para>
    /// </summary>
    /// <param name="postedBy">Who accepted the differences.</param>
    /// <param name="now">The current instant.</param>
    public Result Post(string postedBy, DateTimeOffset now)
    {
        if (Status != StockCountStatus.Submitted)
        {
            return InventoryErrors.Count.NotSubmitted;
        }

        Status = StockCountStatus.Posted;
        PostedBy = Clean(postedBy, 120);
        PostedAtUtc = now;

        Raise(new StockCountPostedDomainEvent(Id, Number, WarehouseId, CountedLines));

        return Result.Success();
    }

    /// <summary>Abandons the sheet. Nothing it recorded touches stock.</summary>
    /// <param name="reason">Why. Somebody will ask what happened to sheet 14.</param>
    public Result Cancel(string? reason)
    {
        if (IsClosed)
        {
            return InventoryErrors.Count.AlreadyClosed;
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return InventoryErrors.Count.CancelReasonRequired;
        }

        Status = StockCountStatus.Cancelled;
        CancellationReason = Clean(reason, MaxNotesLength);

        return Result.Success();
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

/// <summary>Where a count sheet stands.</summary>
public enum StockCountStatus
{
    /// <summary>Unspecified. Never persisted.</summary>
    Unknown = 0,

    /// <summary>Lines may still be added and counted.</summary>
    Open = 1,

    /// <summary>Counting is finished and somebody has to accept the differences.</summary>
    Submitted = 2,

    /// <summary>The differences were applied to stock. Terminal.</summary>
    Posted = 3,

    /// <summary>Abandoned without touching stock. Terminal.</summary>
    Cancelled = 4,
}
