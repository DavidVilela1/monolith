using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoPartsErp.Modules.Inventory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StockValuation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "unit_cost_currency",
                schema: "inventory",
                table: "stock_movements",
                newName: "cost_currency");

            migrationBuilder.RenameColumn(
                name: "unit_cost",
                schema: "inventory",
                table: "stock_movements",
                newName: "cost_value");

            migrationBuilder.AddColumn<decimal>(
                name: "stock_value",
                schema: "inventory",
                table: "stock_items",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "stock_value_currency",
                schema: "inventory",
                table: "stock_items",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "stock_value",
                schema: "inventory",
                table: "stock_items");

            migrationBuilder.DropColumn(
                name: "stock_value_currency",
                schema: "inventory",
                table: "stock_items");

            migrationBuilder.RenameColumn(
                name: "cost_value",
                schema: "inventory",
                table: "stock_movements",
                newName: "unit_cost");

            migrationBuilder.RenameColumn(
                name: "cost_currency",
                schema: "inventory",
                table: "stock_movements",
                newName: "unit_cost_currency");
        }
    }
}
