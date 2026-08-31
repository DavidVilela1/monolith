using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoPartsErp.Modules.Inventory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialInventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "inventory");

            migrationBuilder.CreateTable(
                name: "stock_items",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    part_id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    on_hand = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    on_hand_unit = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    reserved = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    reserved_unit = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    on_order = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    on_order_unit = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    reorder_point = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    reorder_point_unit = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    reorder_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    reorder_quantity_unit = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    default_bin_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_counted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    modified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_items", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stock_movements",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    part_id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    balance_after = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    balance_after_unit = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    reference_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    reference_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    reference_note = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    bin_id = table.Column<Guid>(type: "uuid", nullable: true),
                    unit_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    unit_cost_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    modified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_movements", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "storage_bins",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    pick_sequence = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    modified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_storage_bins", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "warehouses",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    allows_negative_stock = table.Column<bool>(type: "boolean", nullable: false),
                    requires_bin_tracking = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    modified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_warehouses", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stock_reservations",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    reference_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    reference_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    reference_note = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    stock_item_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_reservations", x => x.id);
                    table.ForeignKey(
                        name: "fk_stock_reservations_stock_items_stock_item_id",
                        column: x => x.stock_item_id,
                        principalSchema: "inventory",
                        principalTable: "stock_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_stock_items_tenant_warehouse",
                schema: "inventory",
                table: "stock_items",
                columns: new[] { "tenant_id", "warehouse_id" });

            migrationBuilder.CreateIndex(
                name: "ux_stock_items_tenant_part_warehouse",
                schema: "inventory",
                table: "stock_items",
                columns: new[] { "tenant_id", "part_id", "warehouse_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_part_warehouse_date",
                schema: "inventory",
                table: "stock_movements",
                columns: new[] { "tenant_id", "part_id", "warehouse_id", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_reference_number",
                schema: "inventory",
                table: "stock_movements",
                column: "reference_number");

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_tenant_date",
                schema: "inventory",
                table: "stock_movements",
                columns: new[] { "tenant_id", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_reservations_status_expiry",
                schema: "inventory",
                table: "stock_reservations",
                columns: new[] { "status", "expires_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_reservations_stock_item_id",
                schema: "inventory",
                table: "stock_reservations",
                column: "stock_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_storage_bins_pick_route",
                schema: "inventory",
                table: "storage_bins",
                columns: new[] { "warehouse_id", "pick_sequence" });

            migrationBuilder.CreateIndex(
                name: "ux_storage_bins_warehouse_code",
                schema: "inventory",
                table: "storage_bins",
                columns: new[] { "tenant_id", "warehouse_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_warehouses_tenant_code",
                schema: "inventory",
                table: "warehouses",
                columns: new[] { "tenant_id", "code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stock_movements",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stock_reservations",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "storage_bins",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "warehouses",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stock_items",
                schema: "inventory");
        }
    }
}
