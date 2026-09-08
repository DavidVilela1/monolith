using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoPartsErp.Modules.Inventory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StockCounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "stock_counts",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    counted_on = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    submitted_by = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    submitted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    posted_by = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    posted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    modified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_counts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stock_count_lines",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_number = table.Column<int>(type: "integer", nullable: false),
                    part_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    system_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    system_unit = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    counted_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    counted_unit = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    counted_by = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    counted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    stock_count_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_count_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_stock_count_lines_stock_counts_stock_count_id",
                        column: x => x.stock_count_id,
                        principalSchema: "inventory",
                        principalTable: "stock_counts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_stock_count_lines_part",
                schema: "inventory",
                table: "stock_count_lines",
                column: "part_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_count_lines_stock_count_id",
                schema: "inventory",
                table: "stock_count_lines",
                column: "stock_count_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_counts_tenant_live",
                schema: "inventory",
                table: "stock_counts",
                columns: new[] { "tenant_id", "warehouse_id", "counted_on" },
                filter: "status IN ('Open', 'Submitted')");

            migrationBuilder.CreateIndex(
                name: "ux_stock_counts_tenant_number",
                schema: "inventory",
                table: "stock_counts",
                columns: new[] { "tenant_id", "number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stock_count_lines",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stock_counts",
                schema: "inventory");
        }
    }
}
