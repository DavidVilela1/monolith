using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoPartsErp.Modules.Inventory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StockTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "stock_transfers",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    from_warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    dispatched_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    dispatched_by = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    closure_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    modified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_transfers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stock_transfer_lines",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_number = table.Column<int>(type: "integer", nullable: false),
                    part_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    quantity_unit = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    dispatched_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    dispatched_unit = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    received_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    received_unit = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    lost_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    lost_unit = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    value_in_transit = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    value_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    stock_transfer_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_transfer_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_stock_transfer_lines_stock_transfers_stock_transfer_id",
                        column: x => x.stock_transfer_id,
                        principalSchema: "inventory",
                        principalTable: "stock_transfers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_lines_part",
                schema: "inventory",
                table: "stock_transfer_lines",
                column: "part_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_lines_stock_transfer_id",
                schema: "inventory",
                table: "stock_transfer_lines",
                column: "stock_transfer_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfers_tenant_from_status",
                schema: "inventory",
                table: "stock_transfers",
                columns: new[] { "tenant_id", "from_warehouse_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfers_tenant_in_transit",
                schema: "inventory",
                table: "stock_transfers",
                columns: new[] { "tenant_id", "to_warehouse_id", "dispatched_at_utc" },
                filter: "status IN ('InTransit', 'PartiallyReceived')");

            migrationBuilder.CreateIndex(
                name: "ux_stock_transfers_tenant_number",
                schema: "inventory",
                table: "stock_transfers",
                columns: new[] { "tenant_id", "number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stock_transfer_lines",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stock_transfers",
                schema: "inventory");
        }
    }
}
