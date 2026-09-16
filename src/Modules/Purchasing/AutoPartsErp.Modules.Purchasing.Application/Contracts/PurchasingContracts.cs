namespace AutoPartsErp.Modules.Purchasing.Application.Contracts;

/// <summary>One row in a purchase order list.</summary>
/// <param name="Id">The order.</param>
/// <param name="OrderNumber">Its human-readable number.</param>
/// <param name="SupplierId">Who it is for.</param>
/// <param name="SupplierCode">Their short code, as it was when the order was raised.</param>
/// <param name="Status">Draft, Submitted, Confirmed, PartiallyReceived, Received, Cancelled or ClosedShort.</param>
/// <param name="OrderedOn">The day it went out.</param>
/// <param name="ExpectedOn">The day the goods are expected.</param>
/// <param name="Total">The order value.</param>
/// <param name="OutstandingValue">The value still to be delivered.</param>
/// <param name="CurrencyCode">Currency of both amounts.</param>
/// <param name="LineCount">How many lines it has.</param>
/// <param name="IsOverdue">True when the expected date has passed and something is still outstanding.</param>
public sealed record PurchaseOrderSummary(
    Guid Id,
    string OrderNumber,
    Guid SupplierId,
    string SupplierCode,
    string Status,
    DateOnly? OrderedOn,
    DateOnly? ExpectedOn,
    decimal Total,
    decimal OutstandingValue,
    string CurrencyCode,
    int LineCount,
    bool IsOverdue);

/// <summary>The full picture of one purchase order.</summary>
public sealed record PurchaseOrderDetail
{
    /// <summary>The order.</summary>
    public required Guid Id { get; init; }

    /// <summary>Its human-readable number.</summary>
    public required string OrderNumber { get; init; }

    /// <summary>Who it is for.</summary>
    public required Guid SupplierId { get; init; }

    /// <summary>Their short code, as it was when the order was raised.</summary>
    public required string SupplierCode { get; init; }

    /// <summary>Where the goods are to be delivered.</summary>
    public required Guid DeliverToWarehouseId { get; init; }

    /// <summary>Draft, Submitted, Confirmed, PartiallyReceived, Received, Cancelled or ClosedShort.</summary>
    public required string Status { get; init; }

    /// <summary>Currency the order is priced in.</summary>
    public required string CurrencyCode { get; init; }

    /// <summary>The day it went out.</summary>
    public DateOnly? OrderedOn { get; init; }

    /// <summary>The day the goods are expected.</summary>
    public DateOnly? ExpectedOn { get; init; }

    /// <summary>Their own order number.</summary>
    public string? SupplierReference { get; init; }

    /// <summary>Anything the buyer recorded against it.</summary>
    public string? Notes { get; init; }

    /// <summary>Why it was cancelled or closed short.</summary>
    public string? ClosureReason { get; init; }

    /// <summary>The order value.</summary>
    public required decimal Total { get; init; }

    /// <summary>The value still to be delivered.</summary>
    public required decimal OutstandingValue { get; init; }

    /// <summary>True while the order can still be changed.</summary>
    public required bool IsEditable { get; init; }

    /// <summary>True when goods may be booked in against it.</summary>
    public required bool CanReceive { get; init; }

    /// <summary>Its lines.</summary>
    public required IReadOnlyList<PurchaseOrderLineDto> Lines { get; init; }
}

/// <summary>One line on a purchase order.</summary>
/// <param name="Id">The line.</param>
/// <param name="PartId">The part being bought.</param>
/// <param name="Sku">Its SKU, as it was when the order was raised.</param>
/// <param name="Description">Its description, as it was when the order was raised.</param>
/// <param name="Quantity">How much was ordered.</param>
/// <param name="ReceivedQuantity">How much has arrived so far.</param>
/// <param name="OutstandingQuantity">What is still to come.</param>
/// <param name="UnitCode">The unit all three quantities are in.</param>
/// <param name="UnitPrice">The agreed price per unit.</param>
/// <param name="LineTotal">Unit price times quantity ordered.</param>
/// <param name="IsFullyReceived">True once everything ordered has arrived.</param>
public sealed record PurchaseOrderLineDto(
    Guid Id,
    Guid PartId,
    string Sku,
    string Description,
    decimal Quantity,
    decimal ReceivedQuantity,
    decimal OutstandingQuantity,
    string UnitCode,
    decimal UnitPrice,
    decimal LineTotal,
    bool IsFullyReceived);

/// <summary>A part that has run low somewhere and probably needs buying.</summary>
/// <param name="Id">The suggestion.</param>
/// <param name="PartId">The part that ran low.</param>
/// <param name="WarehouseId">Where it ran low.</param>
/// <param name="QuantityAvailable">What is on the shelf and not already spoken for.</param>
/// <param name="QuantityOnOrder">What is already on a purchase order and has not arrived.</param>
/// <param name="ReorderPoint">The level that triggered it.</param>
/// <param name="SuggestedQuantity">How much to order.</param>
/// <param name="Shortfall">How far below the trigger level it has fallen.</param>
/// <param name="Status">Open, Ordered or Dismissed.</param>
/// <param name="RaisedAtUtc">When it first appeared.</param>
/// <param name="LastSeenAtUtc">When it was last confirmed to still be true.</param>
/// <param name="PurchaseOrderId">The order it was rolled into, once it was.</param>
/// <param name="DismissedReason">Why the buyer decided not to act on it.</param>
public sealed record ReplenishmentSuggestionDto(
    Guid Id,
    Guid PartId,
    Guid WarehouseId,
    decimal QuantityAvailable,
    decimal QuantityOnOrder,
    decimal ReorderPoint,
    decimal SuggestedQuantity,
    decimal Shortfall,
    string Status,
    DateTimeOffset RaisedAtUtc,
    DateTimeOffset LastSeenAtUtc,
    Guid? PurchaseOrderId,
    string? DismissedReason);

/// <summary>
/// A supplier's invoice as the conferência list shows it.
/// </summary>
/// <param name="Id">The invoice.</param>
/// <param name="SupplierId">The supplier.</param>
/// <param name="SupplierCode">Their code.</param>
/// <param name="SupplierDocumentNumber">Their number for it, once it is known.</param>
/// <param name="DocumentDate">The date on their document, once it is known.</param>
/// <param name="ReceivedOn">The day the goods it covers arrived.</param>
/// <param name="Status">Drafted, Matched, Disputed or Cancelled.</param>
/// <param name="NetTotal">What this system worked out, net of the rebate.</param>
/// <param name="GrossTotal">The same with VAT.</param>
/// <param name="StatedGrossTotal">What the supplier said it was, once somebody has typed it.</param>
/// <param name="Difference">
/// Their figure less ours, or null while nobody has typed theirs. The column the whole screen
/// exists for: everything at zero is a day nobody has to do anything about.
/// </param>
/// <param name="CurrencyCode">The currency.</param>
/// <param name="LineCount">How many lines it has.</param>
/// <param name="Reason">Why it is disputed, or why it was cancelled.</param>
public sealed record SupplierInvoiceSummary(
    Guid Id,
    Guid SupplierId,
    string SupplierCode,
    string? SupplierDocumentNumber,
    DateOnly? DocumentDate,
    DateOnly ReceivedOn,
    string Status,
    decimal NetTotal,
    decimal GrossTotal,
    decimal? StatedGrossTotal,
    decimal? Difference,
    string CurrencyCode,
    int LineCount,
    string? Reason);

/// <summary>One line of a supplier's invoice.</summary>
/// <param name="Id">The line. What the price-correction route needs and nothing else returned.</param>
/// <param name="PurchaseOrderId">The order the goods came against.</param>
/// <param name="PurchaseOrderLineId">The line of it.</param>
/// <param name="PartId">The part.</param>
/// <param name="Sku">Its SKU, snapshotted.</param>
/// <param name="Description">Its description, snapshotted.</param>
/// <param name="Quantity">How much was charged for.</param>
/// <param name="UnitCode">The unit that quantity is in.</param>
/// <param name="UnitPrice">What it was booked at.</param>
/// <param name="LineTotal">The two multiplied.</param>
/// <param name="VatRatePercent">The rate on this line.</param>
/// <param name="HasAgreedPrice">
/// True when the price came from a supplier price the company had agreed, false when it fell back
/// to the purchase order's. The second is the one worth a person's eye.
/// </param>
public sealed record SupplierInvoiceLineDto(
    Guid Id,
    Guid PurchaseOrderId,
    Guid PurchaseOrderLineId,
    Guid PartId,
    string Sku,
    string Description,
    decimal Quantity,
    string UnitCode,
    decimal UnitPrice,
    decimal LineTotal,
    decimal VatRatePercent,
    bool HasAgreedPrice);

/// <summary>A supplier's invoice with everything on it.</summary>
/// <param name="Id">The invoice.</param>
/// <param name="SupplierId">The supplier.</param>
/// <param name="SupplierCode">Their code.</param>
/// <param name="SupplierDocumentNumber">Their number for it.</param>
/// <param name="DocumentDate">The date on their document.</param>
/// <param name="ReceivedOn">The day the goods arrived.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="LinesTotal">The lines added up, before the rebate.</param>
/// <param name="RappelRatePercent">The rate the period had reached when the goods arrived.</param>
/// <param name="RappelAmount">What that rate took off.</param>
/// <param name="NetTotal">The lines less the rebate.</param>
/// <param name="VatTotal">The tax, worked out per rate with the rebate spread across the bands.</param>
/// <param name="GrossTotal">Net plus tax.</param>
/// <param name="StatedGrossTotal">What the supplier said.</param>
/// <param name="Difference">Theirs less ours, or null while theirs is unknown.</param>
/// <param name="CurrencyCode">The currency.</param>
/// <param name="Reason">Why it is disputed, or why it was cancelled.</param>
/// <param name="IsOpen">True while it can still take lines and be reconciled.</param>
/// <param name="Lines">The lines.</param>
public sealed record SupplierInvoiceDetail(
    Guid Id,
    Guid SupplierId,
    string SupplierCode,
    string? SupplierDocumentNumber,
    DateOnly? DocumentDate,
    DateOnly ReceivedOn,
    string Status,
    decimal LinesTotal,
    decimal RappelRatePercent,
    decimal RappelAmount,
    decimal NetTotal,
    decimal VatTotal,
    decimal GrossTotal,
    decimal? StatedGrossTotal,
    decimal? Difference,
    string CurrencyCode,
    string? Reason,
    bool IsOpen,
    IReadOnlyList<SupplierInvoiceLineDto> Lines);

/// <summary>One step of a rebate scale.</summary>
/// <param name="From">What the period has to have bought to reach it.</param>
/// <param name="Percent">What it pays from then on, on everything.</param>
public sealed record RappelStepDto(decimal From, decimal Percent);

/// <summary>What was agreed with a supplier.</summary>
/// <param name="Id">The agreement.</param>
/// <param name="SupplierId">The supplier.</param>
/// <param name="SupplierCode">Their code.</param>
/// <param name="EffectiveFrom">The first day it applies.</param>
/// <param name="EffectiveTo">The last, when it has ended.</param>
/// <param name="IsLive">True when today falls inside it.</param>
/// <param name="RappelBasis">None, OnInvoice or PeriodCreditNote.</param>
/// <param name="RappelPeriod">None, Monthly, Quarterly or Annual.</param>
/// <param name="Steps">The scale, lowest threshold first.</param>
/// <param name="CurrencyCode">The currency the thresholds are in.</param>
/// <param name="Note">Whatever somebody wrote about it.</param>
public sealed record SupplierAgreementDto(
    Guid Id,
    Guid SupplierId,
    string SupplierCode,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    bool IsLive,
    string RappelBasis,
    string RappelPeriod,
    IReadOnlyList<RappelStepDto> Steps,
    string CurrencyCode,
    string? Note);

/// <summary>What a supplier charges for a part, from a day.</summary>
/// <param name="Id">The price.</param>
/// <param name="SupplierId">The supplier.</param>
/// <param name="PartId">The part.</param>
/// <param name="SupplierPartNumber">Their number for it, when they use one.</param>
/// <param name="UnitPrice">What they charge.</param>
/// <param name="CurrencyCode">The currency.</param>
/// <param name="EffectiveFrom">The first day it applies.</param>
/// <param name="IsCurrent">
/// True when this is the price in force today for that supplier and part. A rise is a new row, so
/// a list without this reads as several prices for one thing.
/// </param>
/// <param name="Note">Whatever somebody wrote about it.</param>
public sealed record SupplierPriceDto(
    Guid Id,
    Guid SupplierId,
    Guid PartId,
    string? SupplierPartNumber,
    decimal UnitPrice,
    string CurrencyCode,
    DateOnly EffectiveFrom,
    bool IsCurrent,
    string? Note);
