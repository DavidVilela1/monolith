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
public sealed record SalesOrderLineDto(
    Guid Id,
    Guid PartId,
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
    string? PriceSource);

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
