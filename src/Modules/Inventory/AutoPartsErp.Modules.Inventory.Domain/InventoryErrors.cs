using System.Globalization;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Inventory.Domain;

/// <summary>Every failure the Inventory module can report, in one place.</summary>
public static class InventoryErrors
{
    /// <summary>Failures relating to a <see cref="Warehouses.Warehouse"/>.</summary>
    public static class Warehouse
    {
        /// <summary>The warehouse does not exist.</summary>
        public static Error NotFound(string identifier) =>
            Error.NotFound("inventory.warehouse.not_found", $"No warehouse matches '{identifier}'.");

        /// <summary>A warehouse with this code already exists.</summary>
        public static Error CodeAlreadyExists(string code) =>
            Error.Conflict("inventory.warehouse.code_exists", $"Warehouse code '{code}' is already in use.");

        /// <summary>A code is required.</summary>
        public static readonly Error CodeRequired =
            Error.Validation("inventory.warehouse.code_required", "A warehouse code is required.");

        /// <summary>The code is too long.</summary>
        public static readonly Error CodeTooLong =
            Error.Validation("inventory.warehouse.code_too_long", "A warehouse code may be at most 20 characters.");

        /// <summary>A name is required.</summary>
        public static readonly Error NameRequired =
            Error.Validation("inventory.warehouse.name_required", "A warehouse name is required.");

        /// <summary>The name is too long.</summary>
        public static readonly Error NameTooLong =
            Error.Validation("inventory.warehouse.name_too_long", "A warehouse name may be at most 120 characters.");

        /// <summary>The warehouse is closed to movements.</summary>
        public static readonly Error Inactive =
            Error.DomainRule("inventory.warehouse.inactive", "That warehouse is closed to stock movements.");
    }

    /// <summary>Failures relating to a <see cref="AutoPartsErp.Modules.Inventory.Domain.Stock.StockItem"/>.</summary>
    public static class Stock
    {
        /// <summary>No stock record exists for this part in this warehouse.</summary>
        public static Error NotFound(string part, string warehouse) =>
            Error.NotFound(
                "inventory.stock.not_found",
                $"No stock record for part '{part}' in warehouse '{warehouse}'.");

        /// <summary>A stock record already exists for this combination.</summary>
        public static Error AlreadyExists(string part, string warehouse) =>
            Error.Conflict(
                "inventory.stock.already_exists",
                $"Part '{part}' already has a stock record in warehouse '{warehouse}'.");

        /// <summary>There is not enough on the shelf.</summary>
        public static Error InsufficientOnHand(decimal onHand, decimal requested, string unit) =>
            Error.DomainRule(
                "inventory.stock.insufficient_on_hand",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Only {onHand} {unit} on hand; {requested} {unit} were requested."));

        /// <summary>There is stock, but it is already promised to someone else.</summary>
        public static Error InsufficientAvailable(decimal available, decimal requested, string unit) =>
            Error.DomainRule(
                "inventory.stock.insufficient_available",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Only {available} {unit} available against a request for {requested} {unit}. The rest is on hand but already reserved."));

        /// <summary>A count came in below what is already reserved.</summary>
        public static Error CountBelowReserved(decimal counted, decimal reserved, string unit) =>
            Error.DomainRule(
                "inventory.stock.count_below_reserved",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"A count of {counted} {unit} is below the {reserved} {unit} already reserved. Release the affected reservations first, so somebody decides which orders are short."));

        /// <summary>A part is required.</summary>
        public static readonly Error PartRequired =
            Error.Validation("inventory.stock.part_required", "A part is required.");

        /// <summary>A warehouse is required.</summary>
        public static readonly Error WarehouseRequired =
            Error.Validation("inventory.stock.warehouse_required", "A warehouse is required.");

        /// <summary>Movement quantities must be positive.</summary>
        public static readonly Error QuantityMustBePositive =
            Error.Validation(
                "inventory.stock.quantity_not_positive",
                "A movement quantity must be greater than zero. Use the opposite movement type to reverse a change.");

        /// <summary>A physical count cannot be negative.</summary>
        public static readonly Error CountCannotBeNegative =
            Error.Validation("inventory.stock.count_negative", "A counted quantity cannot be negative.");

        /// <summary>The adjustment matches the current balance.</summary>
        public static readonly Error AdjustmentChangesNothing =
            Error.Validation(
                "inventory.stock.adjustment_no_change",
                "The counted quantity matches the current balance, so there is nothing to adjust.");

        /// <summary>
        /// A receipt arrived priced in a currency the stock is not valued in.
        /// <para>
        /// Refused rather than converted, because there is no exchange rate anywhere in this
        /// system and converting with an invented one puts a wrong number on the balance sheet
        /// that nobody can trace back to a decision. A company that buys in another currency
        /// needs a rate source and a policy about which day's rate applies — that is a feature,
        /// not a default.
        /// </para>
        /// </summary>
        public static Error CostCurrencyMismatch(string received, string valued) =>
            Error.DomainRule(
                "inventory.stock.cost_currency_mismatch",
                $"The receipt is priced in {received} and this stock is valued in {valued}. "
                + "There is no exchange rate in this system, so booking it in would mean "
                + "inventing one.");

        /// <summary>An expected delivery has to name the order that is bringing it.</summary>
        public static readonly Error PurchaseOrderRequired =
            Error.Validation(
                "inventory.stock.purchase_order_required",
                "An expected delivery has to name the purchase order and line behind it. "
                + "An on-order quantity nobody can trace back to an order is a number, not a fact.");

        /// <summary>The reservation does not exist on this record.</summary>
        public static readonly Error ReservationNotFound =
            Error.NotFound("inventory.stock.reservation_not_found", "That reservation is not held against this stock.");

        /// <summary>The reservation has already been released, expired or fulfilled.</summary>
        public static readonly Error ReservationNotActive =
            Error.DomainRule(
                "inventory.stock.reservation_not_active",
                "That reservation is no longer active, so it holds no stock back.");

        /// <summary>A reservation cannot expire in the past.</summary>
        public static readonly Error ReservationExpiryInPast =
            Error.Validation(
                "inventory.stock.reservation_expiry_past",
                "A reservation cannot be created with an expiry that has already passed.");

        /// <summary>Half a replenishment policy is worse than none.</summary>
        public static readonly Error IncompleteReplenishmentPolicy =
            Error.Validation(
                "inventory.stock.replenishment_incomplete",
                "Set both a reorder point and a reorder quantity, or neither.");

        /// <summary>The replenishment numbers do not make sense.</summary>
        public static readonly Error InvalidReplenishmentPolicy =
            Error.Validation(
                "inventory.stock.replenishment_invalid",
                "A reorder point cannot be negative and a reorder quantity must be greater than zero.");
    }

    /// <summary>Failures relating to a <see cref="Counting.StockCount"/>.</summary>
    public static class Count
    {
        /// <summary>The sheet does not exist.</summary>
        public static Error NotFound(string identifier) =>
            Error.NotFound("inventory.count.not_found", $"No stock count matches '{identifier}'.");

        /// <summary>A sheet number is required.</summary>
        public static readonly Error NumberRequired =
            Error.Validation("inventory.count.number_required", "A count number is required.");

        /// <summary>The sheet is no longer accepting counts.</summary>
        public static readonly Error NotOpen =
            Error.DomainRule(
                "inventory.count.not_open",
                "That count sheet is not open. Reopen it if the figures need changing.");

        /// <summary>The sheet is not waiting to be accepted.</summary>
        public static readonly Error NotSubmitted =
            Error.DomainRule(
                "inventory.count.not_submitted",
                "That count sheet has not been submitted for review.");

        /// <summary>The sheet has already been posted or abandoned.</summary>
        public static readonly Error AlreadyClosed =
            Error.DomainRule(
                "inventory.count.already_closed",
                "That count sheet is closed. A posted count is corrected by counting again, not "
                + "by editing the sheet that recorded it.");

        /// <summary>A sheet with no lines counts nothing.</summary>
        public static readonly Error NoLines =
            Error.DomainRule("inventory.count.no_lines", "There is nothing on that count sheet.");

        /// <summary>Nobody counted anything.</summary>
        public static readonly Error NothingCounted =
            Error.DomainRule(
                "inventory.count.nothing_counted",
                "No line on that sheet has been counted. Submitting it would ask somebody to "
                + "accept differences nobody looked for.");

        /// <summary>The line is not on this sheet.</summary>
        public static Error LineNotFound(string identifier) =>
            Error.NotFound(
                "inventory.count.line_not_found", $"Line '{identifier}' is not on that count sheet.");

        /// <summary>The part is already on the sheet.</summary>
        public static Error PartAlreadyOnSheet(string sku) =>
            Error.Conflict(
                "inventory.count.part_already_on_sheet",
                $"'{sku}' is already on that sheet. Counting one part twice on one sheet gives "
                + "two answers and no way to choose between them.");

        /// <summary>The counted figure is in a different unit from the balance.</summary>
        public static Error UnitMismatch(string sku, string unit) =>
            Error.Validation(
                "inventory.count.unit_mismatch",
                $"'{sku}' is stocked in '{unit}'. A count in any other unit would be a different "
                + "number about a different thing.");

        /// <summary>A cancelled sheet has to say why.</summary>
        public static readonly Error CancelReasonRequired =
            Error.Validation(
                "inventory.count.cancel_reason_required",
                "Say why the count was abandoned. Somebody will ask what happened to it.");

        /// <summary>A part on the sheet has no balance to correct.</summary>
        public static Error NoStockRecord(string sku) =>
            Error.NotFound(
                "inventory.count.no_stock_record",
                $"'{sku}' has no stock record in that warehouse any more, so there is nothing to "
                + "correct. The sheet was opened before it was removed.");
    }

    /// <summary>Failures relating to a <see cref="Transfers.StockTransfer"/>.</summary>
    public static class Transfer
    {
        /// <summary>The transfer does not exist.</summary>
        public static Error NotFound(string identifier) =>
            Error.NotFound("inventory.transfer.not_found", $"No stock transfer matches '{identifier}'.");

        /// <summary>A transfer number is required.</summary>
        public static readonly Error NumberRequired =
            Error.Validation("inventory.transfer.number_required", "A transfer number is required.");

        /// <summary>Both ends of the transfer are the same place.</summary>
        public static readonly Error SameWarehouse =
            Error.Validation(
                "inventory.transfer.same_warehouse",
                "A transfer has to go somewhere else. Sending stock to the shelf it is already "
                + "on nets to nothing and leaves two ledger rows explaining it.");

        /// <summary>The transfer has already left.</summary>
        public static readonly Error NotDraft =
            Error.DomainRule(
                "inventory.transfer.not_draft",
                "That transfer has already been dispatched. What is on the van cannot be edited.");

        /// <summary>The transfer has nothing on it.</summary>
        public static readonly Error NoLines =
            Error.DomainRule("inventory.transfer.no_lines", "There is nothing on that transfer.");

        /// <summary>The line is not on this transfer.</summary>
        public static Error LineNotFound(string identifier) =>
            Error.NotFound(
                "inventory.transfer.line_not_found", $"Line '{identifier}' is not on that transfer.");

        /// <summary>The part is already on the transfer.</summary>
        public static Error PartAlreadyOnTransfer(string sku) =>
            Error.Conflict(
                "inventory.transfer.part_already_on_transfer",
                $"'{sku}' is already on that transfer. Change the quantity on the line it is on.");

        /// <summary>A line was left out of the dispatch.</summary>
        public static Error LineNotDispatched(string sku) =>
            Error.DomainRule(
                "inventory.transfer.line_not_dispatched",
                $"'{sku}' has no dispatch figure. A transfer leaves whole or not at all, so a "
                + "line the sending warehouse could not fill has to come off it first.");

        /// <summary>Nothing is on its way.</summary>
        public static readonly Error NotInTransit =
            Error.DomainRule(
                "inventory.transfer.not_in_transit",
                "That transfer has nothing on its way. It has either not left yet or is finished.");

        /// <summary>Everything that left has been accounted for.</summary>
        public static readonly Error NothingInTransit =
            Error.DomainRule(
                "inventory.transfer.nothing_in_transit",
                "Everything that left has arrived or been written off. There is no shortfall to accept.");

        /// <summary>More arrived than ever left.</summary>
        public static Error OverReceived(string sku, decimal inTransit) =>
            Error.DomainRule(
                "inventory.transfer.over_received",
                $"Only {inTransit} of '{sku}' is still on its way. Booking in more than left the "
                + "other warehouse would create stock out of nothing.");

        /// <summary>The arrival is in a different unit from the balance.</summary>
        public static Error UnitMismatch(string sku, string unit) =>
            Error.Validation(
                "inventory.transfer.unit_mismatch",
                $"'{sku}' moves in '{unit}'. An arrival in any other unit is a different number "
                + "about a different thing.");

        /// <summary>Goods on a van cannot be uninvented.</summary>
        public static readonly Error CannotCancelInTransit =
            Error.DomainRule(
                "inventory.transfer.cannot_cancel_in_transit",
                "The goods have already left. Either they arrive and are received, or they do not "
                + "and the shortfall is written off - there is nothing left to cancel.");

        /// <summary>The transfer is finished.</summary>
        public static readonly Error AlreadyClosed =
            Error.DomainRule("inventory.transfer.already_closed", "That transfer is closed.");

        /// <summary>Closing a transfer needs an explanation.</summary>
        public static readonly Error CloseReasonRequired =
            Error.Validation(
                "inventory.transfer.close_reason_required",
                "Say why. Stock that left one warehouse and never reached another is a loss, and "
                + "in six months this sentence is the only thing that will explain it.");
    }

    /// <summary>Failures relating to a <see cref="AutoPartsErp.Modules.Inventory.Domain.Stock.StockMovement"/>.</summary>
    public static class Movement
    {
        /// <summary>A reference type is required.</summary>
        public static readonly Error ReferenceTypeRequired =
            Error.Validation(
                "inventory.movement.reference_type_required",
                "Every stock movement must say what kind of document caused it.");

        /// <summary>A reference number is required.</summary>
        public static readonly Error ReferenceNumberRequired =
            Error.Validation(
                "inventory.movement.reference_number_required",
                "Every stock movement must reference a document number.");

        /// <summary>The reference number is too long.</summary>
        public static readonly Error ReferenceNumberTooLong =
            Error.Validation(
                "inventory.movement.reference_number_too_long",
                "A document number may be at most 40 characters.");
    }

    /// <summary>Failures relating to a <see cref="Warehouses.StorageBin"/>.</summary>
    public static class Bin
    {
        /// <summary>The bin does not exist.</summary>
        public static Error NotFound(string identifier) =>
            Error.NotFound("inventory.bin.not_found", $"No storage bin matches '{identifier}'.");

        /// <summary>A bin with this code already exists in the warehouse.</summary>
        public static Error CodeAlreadyExists(string code) =>
            Error.Conflict("inventory.bin.code_exists", $"Bin '{code}' already exists in this warehouse.");

        /// <summary>A code is required.</summary>
        public static readonly Error CodeRequired =
            Error.Validation("inventory.bin.code_required", "A bin code is required.");

        /// <summary>The code is too long.</summary>
        public static readonly Error CodeTooLong =
            Error.Validation("inventory.bin.code_too_long", "A bin code may be at most 30 characters.");
    }
}
