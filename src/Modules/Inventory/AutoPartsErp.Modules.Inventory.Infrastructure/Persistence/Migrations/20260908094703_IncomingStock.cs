using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoPartsErp.Modules.Inventory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IncomingStock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "stock_incoming",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    purchase_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purchase_order_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    received_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    received_unit = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    expected_on = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    stock_item_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_incoming", x => x.id);
                    table.ForeignKey(
                        name: "fk_stock_incoming_stock_items_stock_item_id",
                        column: x => x.stock_item_id,
                        principalSchema: "inventory",
                        principalTable: "stock_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_stock_incoming_order_status",
                schema: "inventory",
                table: "stock_incoming",
                columns: new[] { "purchase_order_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_incoming_stock_item_id",
                schema: "inventory",
                table: "stock_incoming",
                column: "stock_item_id");

            migrationBuilder.CreateIndex(
                name: "ux_stock_incoming_purchase_order_line",
                schema: "inventory",
                table: "stock_incoming",
                column: "purchase_order_line_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stock_incoming",
                schema: "inventory");
        }
    }
}
