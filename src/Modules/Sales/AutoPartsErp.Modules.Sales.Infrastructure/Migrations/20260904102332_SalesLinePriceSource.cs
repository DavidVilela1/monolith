using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoPartsErp.Modules.Sales.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SalesLinePriceSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "price_source",
                schema: "sales",
                table: "sales_order_lines",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "price_source",
                schema: "sales",
                table: "sales_order_lines");
        }
    }
}
