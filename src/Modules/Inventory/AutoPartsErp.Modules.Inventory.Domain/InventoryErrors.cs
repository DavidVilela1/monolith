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

        /// <summary>Expected quantities cannot be negative.</summary>
        public static readonly Error OnOrderCannotBeNegative =
            Error.Validation("inventory.stock.on_order_negative", "An on-order quantity cannot be negative.");

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
