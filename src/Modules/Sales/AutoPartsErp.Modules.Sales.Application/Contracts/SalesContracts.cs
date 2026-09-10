namespace AutoPartsErp.Modules.Sales.Application.Contracts;

/// <summary>One row in a sales order list.</summary>
/// <param name="Id">The order.</param>
/// <param name="OrderNumber">Its human-readable number.</param>
/// <param name="Kind">CounterSale or Order.</param>
/// <param name="CustomerId">Who it is for.</param>
/// <param name="CustomerCode">Their short code, as it was when the order was taken.</param>
/// <param name="CustomerName">Their name, as it was when the order was taken.</param>
/// <param name="Status">Draft, Confirmed, PartiallyDispatched, Dispatched or Cancelled.</param>
/// <param name="ConfirmedOn">The day it was agreed.</param>
/// <param name="RequiredBy">When the customer wants it.</param>
/// <param name="NetTotal">The value before VAT.</param>
/// <param name="VatTotal">The VAT.</param>
/// <param name="GrossTotal">What they will be invoiced.</param>
/// <param name="CurrencyCode">Currency of all three.</param>
/// <param name="LineCount">How many lines it has.</param>
/// <param name="IsLate">True when the required-by date has passed with goods still owed.</param>
public sealed record SalesOrderSummary(
    Guid Id,
    string OrderNumber,
    string Kind,
    Guid CustomerId,
    string CustomerCode,
    string CustomerName,
    string Status,
    DateOnly? ConfirmedOn,
    DateOnly? RequiredBy,
    decimal NetTotal,
    decimal VatTotal,
    decimal GrossTotal,
    string CurrencyCode,
    int LineCount,
    bool IsLate);

/// <summary>The full picture of one sales order.</summary>
public sealed record SalesOrderDetail
{
    /// <summary>The order.</summary>
    public required Guid Id { get; init; }

    /// <summary>Its human-readable number.</summary>
    public required string OrderNumber { get; init; }

    /// <summary>CounterSale or Order.</summary>
    public required string Kind { get; init; }

    /// <summary>Who it is for.</summary>
    public required Guid CustomerId { get; init; }

    /// <summary>Their short code, as it was when the order was taken.</summary>
    public required string CustomerCode { get; init; }

    /// <summary>Their name, as it was when the order was taken.</summary>
    public required string CustomerName { get; init; }

    /// <summary>Where the goods come from.</summary>
    public required Guid FromWarehouseId { get; init; }

    /// <summary>Draft, Confirmed, PartiallyDispatched, Dispatched or Cancelled.</summary>
    public required string Status { get; init; }

    /// <summary>Currency the order is priced in.</summary>
    public required string CurrencyCode { get; init; }

    /// <summary>The day it was agreed.</summary>
    public DateOnly? ConfirmedOn { get; init; }

    /// <summary>When the customer wants it.</summary>
    public DateOnly? RequiredBy { get; init; }

    /// <summary>Their own order number.</summary>
    public string? CustomerReference { get; init; }

    /// <summary>Anything recorded against it.</summary>
    public string? Notes { get; init; }

    /// <summary>Why it was cancelled.</summary>
    public string? ClosureReason { get; init; }

    /// <summary>The value before VAT.</summary>
    public required decimal NetTotal { get; init; }

    /// <summary>The VAT.</summary>
    public required decimal VatTotal { get; init; }

    /// <summary>What the customer will be invoiced.</summary>
    public required decimal GrossTotal { get; init; }

    /// <summary>True while the order can still be changed.</summary>
    public required bool IsEditable { get; init; }

    /// <summary>True when goods may go out against it.</summary>
    public required bool CanDispatch { get; init; }

    /// <summary>True when this order was confirmed past the customer's credit limit.</summary>
    public bool CreditLimitOverridden { get; init; }

    /// <summary>What the lines that can be costed make, before VAT.</summary>
    public decimal Margin { get; init; }

    /// <summary>
    /// That margin as a percentage of what those lines sell for. Null when nothing on the order
    /// can be costed.
    /// </summary>
    public decimal? MarginPercent { get; init; }

    /// <summary>
    /// True when something on the order has no cost, so the margin above covers part of it.
    /// <para>
    /// A screen showing a margin without this is showing a figure nobody can reproduce.
    /// </para>
    /// </summary>
    public bool HasUncostedLines { get; init; }

    /// <summary>Its lines.</summary>
    public required IReadOnlyList<SalesOrderLineDto> Lines { get; init; }
}

/// <summary>One line on a sales order.</summary>
/// <param name="Id">The line.</param>
/// <param name="PartId">The part being sold.</param>
/// <param name="Sku">Its SKU, as it was when the order was taken.</param>
/// <param name="Description">Its description, as it was when the order was taken.</param>
/// <param name="Quantity">How much was sold.</param>
/// <param name="DispatchedQuantity">How much has gone out.</param>
/// <param name="OutstandingQuantity">What is still owed.</param>
/// <param name="UnitCode">The unit all three quantities are in.</param>
/// <param name="UnitPrice">The list price per unit, before discount.</param>
/// <param name="DiscountPercent">The discount given.</param>
/// <param name="NetTotal">What the customer pays for the line, before VAT.</param>
/// <param name="VatRatePercent">The VAT rate applied.</param>
/// <param name="VatAmount">The VAT on the line.</param>
/// <param name="GrossTotal">What the line adds to the invoice.</param>
/// <param name="IsFullyDispatched">True once everything sold has gone out.</param>
/// <param name="PriceSource">
/// The price list the price came from, or null when it was typed by hand. This is the answer to
/// "why did we charge that?" three weeks later.
/// </param>
/// <param name="Kind">Goods, or CoreDeposit for the sum held against a returnable old unit.</param>
/// <param name="CoreForLineId">The goods line a deposit belongs to. Null on a goods line.</param>
/// <param name="UnitCost">
/// What the shelf was worth per unit when the line was priced. Null when Inventory could not say,
/// which is not a cost of zero.
/// </param>
/// <param name="Margin">What the line makes before VAT. Null when there is no cost to compare.</param>
/// <param name="MarginPercent">That margin as a percentage of what the customer pays, before VAT.</param>
public sealed record SalesOrderLineDto(
    Guid Id,
    Guid PartId,
    string Kind,
    Guid? CoreForLineId,
    string Sku,
    string Description,
    decimal Quantity,
    decimal DispatchedQuantity,
    decimal OutstandingQuantity,
    string UnitCode,
    decimal UnitPrice,
    decimal DiscountPercent,
    decimal NetTotal,
    decimal VatRatePercent,
    decimal VatAmount,
    decimal GrossTotal,
    bool IsFullyDispatched,
    string? PriceSource,
    decimal? UnitCost,
    decimal? Margin,
    decimal? MarginPercent);

/// <summary>One row in a list of customer returns.</summary>
/// <param name="Id">The return.</param>
/// <param name="ReturnNumber">Its number.</param>
/// <param name="SalesOrderId">The order the goods went out on.</param>
/// <param name="OrderNumber">Its number.</param>
/// <param name="CustomerId">Who sent them back.</param>
/// <param name="CustomerCode">Their short code.</param>
/// <param name="CustomerName">Their name.</param>
/// <param name="Status">Draft, Received, Credited or Cancelled.</param>
/// <param name="Reason">Why the goods came back.</param>
/// <param name="ReceivedOn">The day they turned up.</param>
/// <param name="CreditNoteNumber">The credit note that gave the money back, once there is one.</param>
/// <param name="GrossTotal">What the customer is owed for them.</param>
/// <param name="CurrencyCode">Currency of that figure.</param>
/// <param name="LineCount">How many lines it has.</param>
public sealed record CustomerReturnSummary(
    Guid Id,
    string ReturnNumber,
    Guid SalesOrderId,
    string OrderNumber,
    Guid CustomerId,
    string CustomerCode,
    string CustomerName,
    string Status,
    string Reason,
    DateOnly? ReceivedOn,
    string? CreditNoteNumber,
    decimal GrossTotal,
    string CurrencyCode,
    int LineCount);

/// <summary>The full picture of one customer return.</summary>
public sealed record CustomerReturnDetail
{
    /// <summary>The return.</summary>
    public required Guid Id { get; init; }

    /// <summary>Its number.</summary>
    public required string ReturnNumber { get; init; }

    /// <summary>The order the goods went out on.</summary>
    public required Guid SalesOrderId { get; init; }

    /// <summary>Its number.</summary>
    public required string OrderNumber { get; init; }

    /// <summary>Who sent them back.</summary>
    public required Guid CustomerId { get; init; }

    /// <summary>Their short code.</summary>
    public required string CustomerCode { get; init; }

    /// <summary>Their name.</summary>
    public required string CustomerName { get; init; }

    /// <summary>Where the goods are coming back to.</summary>
    public required Guid ToWarehouseId { get; init; }

    /// <summary>Draft, Received, Credited or Cancelled.</summary>
    public required string Status { get; init; }

    /// <summary>Why the goods came back.</summary>
    public required string Reason { get; init; }

    /// <summary>The day they turned up.</summary>
    public DateOnly? ReceivedOn { get; init; }

    /// <summary>The credit note that gave the money back, once one has been issued.</summary>
    public string? CreditNoteNumber { get; init; }

    /// <summary>The day it was issued.</summary>
    public DateOnly? CreditedOn { get; init; }

    /// <summary>Why it was called off.</summary>
    public string? ClosureReason { get; init; }

    /// <summary>What the customer is owed, before VAT.</summary>
    public required decimal NetTotal { get; init; }

    /// <summary>The VAT to be credited.</summary>
    public required decimal VatTotal { get; init; }

    /// <summary>What the credit note will come to.</summary>
    public required decimal GrossTotal { get; init; }

    /// <summary>Currency of all three.</summary>
    public required string CurrencyCode { get; init; }

    /// <summary>True while lines may still be added or taken off.</summary>
    public required bool IsDraft { get; init; }

    /// <summary>Its lines.</summary>
    public required IReadOnlyList<CustomerReturnLineDto> Lines { get; init; }
}

/// <summary>One part coming back.</summary>
/// <param name="Id">The line.</param>
/// <param name="SalesOrderLineId">The line of the original order it came from.</param>
/// <param name="PartId">The part.</param>
/// <param name="Sku">Its SKU, as it was on the order.</param>
/// <param name="Description">Its description, as it was on the order.</param>
/// <param name="Quantity">How much is coming back.</param>
/// <param name="UnitCode">The unit that quantity is in.</param>
/// <param name="UnitPrice">What they paid per unit.</param>
/// <param name="DiscountPercent">The discount they had.</param>
/// <param name="NetTotal">What they are credited for the line, before VAT.</param>
/// <param name="VatRatePercent">The VAT rate on the original line.</param>
/// <param name="VatAmount">The VAT on the line.</param>
/// <param name="GrossTotal">What the line adds to the credit.</param>
/// <param name="Disposition">BackToStock or Scrap.</param>
/// <param name="ConditionNote">What state it arrived in.</param>
public sealed record CustomerReturnLineDto(
    Guid Id,
    Guid SalesOrderLineId,
    Guid PartId,
    string Sku,
    string Description,
    decimal Quantity,
    string UnitCode,
    decimal UnitPrice,
    decimal DiscountPercent,
    decimal NetTotal,
    decimal VatRatePercent,
    decimal VatAmount,
    decimal GrossTotal,
    string Disposition,
    string? ConditionNote);

/// <summary>What Sales knows about a customer.</summary>
/// <param name="Id">The customer, which is also their partner id.</param>
/// <param name="Code">Their short code.</param>
/// <param name="LegalName">Their registered name.</param>
/// <param name="Status">Active, OnHold or Closed.</param>
/// <param name="HoldReason">Why they are on hold, when they are.</param>
/// <param name="CreditLimit">How much they may owe at once.</param>
/// <param name="Committed">The value of confirmed orders not yet dispatched.</param>
/// <param name="AvailableCredit">What is left of the limit.</param>
/// <param name="CurrencyCode">Currency of all three amounts.</param>
/// <param name="PaymentDueInDays">Days to pay. Zero means on delivery.</param>
/// <param name="PaymentEndOfMonth">True when the days run from month end.</param>
/// <param name="PriceListCode">Which price list applies.</param>
/// <param name="CanTakeOrders">True when they may place new orders.</param>
/// <param name="IsCashOnly">True when they pay before the goods leave.</param>
public sealed record CustomerAccountDto(
    Guid Id,
    string Code,
    string LegalName,
    string Status,
    string? HoldReason,
    decimal CreditLimit,
    decimal Committed,
    decimal AvailableCredit,
    string CurrencyCode,
    int PaymentDueInDays,
    bool PaymentEndOfMonth,
    string? PriceListCode,
    bool CanTakeOrders,
    bool IsCashOnly);
