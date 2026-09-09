using AutoPartsErp.SharedKernel.Primitives;

namespace AutoPartsErp.Modules.Sales.Domain.Returns.Events;

/// <summary>
/// Raised when the goods on a return are physically back.
/// <para>
/// The document-level fact, carrying what the credit will come to. Nothing acts on it yet —
/// crediting the customer is the next step and belongs to Invoicing — but it is the event a
/// credit note will be drawn from, and it is raised now so the day the goods arrived is the day
/// the record says they arrived.
/// </para>
/// </summary>
/// <param name="CustomerReturnId">The return.</param>
/// <param name="ReturnNumber">Its number.</param>
/// <param name="SalesOrderId">The order the goods went out on.</param>
/// <param name="OrderNumber">Its number.</param>
/// <param name="CustomerId">Who sent them back.</param>
/// <param name="GrossTotal">What the customer is owed for them.</param>
/// <param name="CurrencyCode">Currency of that figure.</param>
/// <param name="ReceivedOn">The day they turned up.</param>
public sealed record CustomerReturnReceivedDomainEvent(
    CustomerReturnId CustomerReturnId,
    string ReturnNumber,
    SalesOrderId SalesOrderId,
    string OrderNumber,
    CustomerRef CustomerId,
    decimal GrossTotal,
    string CurrencyCode,
    DateOnly ReceivedOn) : DomainEvent;

/// <summary>
/// Raised per saleable line when a return is received, so the stock goes back on the shelf.
/// <para>
/// The mirror of the dispatch event, and deliberately shaped like it: the same order number, the
/// same line, the same part and warehouse. What it does not carry is a value, because Sales does
/// not know one — what these goods cost is a fact in Inventory's ledger, and the module that owns
/// costing is the one that should answer what they are worth coming back.
/// </para>
/// <para>
/// Only saleable lines raise it. A scrapped return is credited and written off, and there is
/// nothing for a stock balance to do about a part that went in the bin.
/// </para>
/// </summary>
/// <param name="CustomerReturnId">The return.</param>
/// <param name="ReturnNumber">Its number, which becomes the stock movement reference.</param>
/// <param name="SalesOrderId">The order the goods went out on.</param>
/// <param name="OrderNumber">Its number, which is how the original issue is found in the ledger.</param>
/// <param name="SalesOrderLineId">The line they went out on.</param>
/// <param name="PartId">The part coming back.</param>
/// <param name="WarehouseId">Where it is going.</param>
/// <param name="Quantity">How much.</param>
/// <param name="UnitCode">The unit that quantity is in.</param>
public sealed record GoodsReturnedDomainEvent(
    CustomerReturnId CustomerReturnId,
    string ReturnNumber,
    SalesOrderId SalesOrderId,
    string OrderNumber,
    SalesOrderLineId SalesOrderLineId,
    PartRef PartId,
    WarehouseRef WarehouseId,
    decimal Quantity,
    string UnitCode) : DomainEvent;
