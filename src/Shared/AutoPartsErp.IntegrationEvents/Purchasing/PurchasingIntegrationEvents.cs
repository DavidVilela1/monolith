using AutoPartsErp.SharedKernel.Messaging;

namespace AutoPartsErp.IntegrationEvents.Purchasing;

/// <summary>
/// A purchase order was sent to a supplier. Inventory can show the quantity as on order, so the
/// counter stops seeing "none in stock" for a part that is already three days into a two-week
/// lead time; Finance can accrue the commitment.
/// </summary>
/// <param name="PurchaseOrderId">The order.</param>
/// <param name="OrderNumber">Its human-readable number, e.g. "PO-2026-00042".</param>
/// <param name="SupplierId">Who it went to.</param>
/// <param name="WarehouseId">Where the goods are expected.</param>
/// <param name="Total">The order value.</param>
/// <param name="CurrencyCode">Currency of the total.</param>
/// <param name="ExpectedOn">When the goods are expected, when the supplier has confirmed a date.</param>
/// <param name="Lines">What was ordered, so Inventory can expect it.</param>
/// <param name="TenantId">The owning tenant.</param>
public sealed record PurchaseOrderSubmittedIntegrationEvent(
    Guid PurchaseOrderId,
    string OrderNumber,
    Guid SupplierId,
    Guid WarehouseId,
    decimal Total,
    string CurrencyCode,
    DateOnly? ExpectedOn,
    IReadOnlyList<OrderedPartLine> Lines,
    Guid TenantId) : IntegrationEvent;

/// <summary>
/// One line of a purchase order, as it travels between modules.
/// <para>
/// The line identity travels with it and is the whole point: a receipt names the line it arrived
/// against, so Inventory can take that arrival off the expectation it satisfies rather than off a
/// shared running total. Arithmetic on a shared total is how an on-order figure ends up negative
/// and nobody can say when it started.
/// </para>
/// </summary>
/// <param name="PurchaseOrderLineId">The line.</param>
/// <param name="PartId">The part.</param>
/// <param name="Quantity">
/// How much this line is about: what was ordered on a submission, what is still outstanding on a
/// cancellation or a short close.
/// </param>
/// <param name="UnitCode">The unit the quantity is expressed in, e.g. EA, SET, L.</param>
public sealed record OrderedPartLine(
    Guid PurchaseOrderLineId,
    Guid PartId,
    decimal Quantity,
    string UnitCode);

/// <summary>
/// Goods arrived against a purchase order line.
/// <para>
/// This is the event that puts stock on a shelf. Purchasing records what the delivery note said;
/// Inventory decides what that means for the balance. Purchasing does not — and must not — write
/// to the inventory schema itself, which is why the receipt travels as a fact rather than as a
/// call into another module's repository.
/// </para>
/// <para>
/// The unit code travels with it for the same reason Catalog sends one on activation: a consumer
/// that has to call back to the publisher to interpret the payload is not decoupled from it.
/// </para>
/// </summary>
/// <param name="PurchaseOrderId">The order received against.</param>
/// <param name="OrderNumber">Its human-readable number, used as the stock movement reference.</param>
/// <param name="PurchaseOrderLineId">The line received against.</param>
/// <param name="PartId">The part received.</param>
/// <param name="WarehouseId">Where it landed.</param>
/// <param name="Quantity">How much arrived on this receipt, not the running total.</param>
/// <param name="UnitCode">The unit the quantity is expressed in, e.g. EA, SET, L.</param>
/// <param name="UnitPrice">What we are paying per unit, for stock valuation.</param>
/// <param name="CurrencyCode">Currency of the unit price.</param>
/// <param name="TenantId">The owning tenant.</param>
public sealed record GoodsReceivedIntegrationEvent(
    Guid PurchaseOrderId,
    string OrderNumber,
    Guid PurchaseOrderLineId,
    Guid PartId,
    Guid WarehouseId,
    decimal Quantity,
    string UnitCode,
    decimal UnitPrice,
    string CurrencyCode,
    Guid TenantId) : IntegrationEvent;

/// <summary>
/// A purchase order was cancelled before it was fully received. Inventory drops the on-order
/// quantity; anything that was counting on those goods needs to know they are not coming.
/// </summary>
/// <param name="PurchaseOrderId">The order.</param>
/// <param name="OrderNumber">Its human-readable number.</param>
/// <param name="WarehouseId">Where the goods were expected, and now are not.</param>
/// <param name="Reason">Why it was cancelled.</param>
/// <param name="Lines">What stops being expected.</param>
/// <param name="TenantId">The owning tenant.</param>
public sealed record PurchaseOrderCancelledIntegrationEvent(
    Guid PurchaseOrderId,
    string OrderNumber,
    Guid WarehouseId,
    string Reason,
    IReadOnlyList<OrderedPartLine> Lines,
    Guid TenantId) : IntegrationEvent;

/// <summary>
/// An order was closed with lines still outstanding: the supplier sent 96 of the 100 and both
/// sides agreed to leave it there.
/// <para>
/// A different conversation with the supplier from a cancellation and the same fact for the
/// shelf — the balance is not coming. Without this event the four that never arrived stay on the
/// expected figure for ever, and every reorder check afterwards believes they are on their way.
/// </para>
/// </summary>
/// <param name="PurchaseOrderId">The order.</param>
/// <param name="OrderNumber">Its human-readable number.</param>
/// <param name="WarehouseId">Where the balance was expected.</param>
/// <param name="Reason">Why the shortfall was accepted.</param>
/// <param name="Lines">The balance that stops being expected.</param>
/// <param name="TenantId">The owning tenant.</param>
public sealed record PurchaseOrderClosedShortIntegrationEvent(
    Guid PurchaseOrderId,
    string OrderNumber,
    Guid WarehouseId,
    string Reason,
    IReadOnlyList<OrderedPartLine> Lines,
    Guid TenantId) : IntegrationEvent;

/// <summary>
/// A supplier's invoice was settled, and it charged something the purchase order had not predicted.
/// <para>
/// Published so Inventory can correct what the shelf is worth. The goods were booked in at the
/// price the order was placed at; the supplier billed a different figure, and until this is
/// applied the balance sheet disagrees with the money about to leave the bank.
/// </para>
/// <para>
/// Only lines whose invoiced price differs from the order's are carried. Most deliveries are
/// invoiced at the price they were ordered at, and an event listing every line of every document
/// would be almost entirely rows asking the other module to do nothing.
/// </para>
/// </summary>
/// <param name="SupplierInvoiceId">The document that settled.</param>
/// <param name="SupplierDocumentNumber">Their document number, which is what the ledger row points at.</param>
/// <param name="DocumentDate">The date on their document.</param>
/// <param name="Lines">The lines that were charged at a different price.</param>
/// <param name="TenantId">The tenant.</param>
public sealed record SupplierInvoicePricedIntegrationEvent(
    Guid SupplierInvoiceId,
    string SupplierDocumentNumber,
    DateOnly DocumentDate,
    IReadOnlyList<InvoicedLine> Lines,
    Guid TenantId) : IntegrationEvent;

/// <summary>
/// One line of a settled supplier invoice, flattened for another module.
/// </summary>
/// <param name="OrderNumber">
/// The purchase order number. With <paramref name="PurchaseOrderLineId"/> it is how the receipt
/// this line paid for is found again in the other module's own ledger.
/// </param>
/// <param name="PurchaseOrderLineId">The order line the goods arrived against.</param>
/// <param name="PartId">The part.</param>
/// <param name="WarehouseId">The warehouse the goods went into.</param>
/// <param name="Quantity">How much was charged for.</param>
/// <param name="InvoicedUnitPrice">What the supplier charged per unit, before any rebate.</param>
/// <param name="CurrencyCode">The currency.</param>
public sealed record InvoicedLine(
    string OrderNumber,
    Guid PurchaseOrderLineId,
    Guid PartId,
    Guid WarehouseId,
    decimal Quantity,
    decimal InvoicedUnitPrice,
    string CurrencyCode);
