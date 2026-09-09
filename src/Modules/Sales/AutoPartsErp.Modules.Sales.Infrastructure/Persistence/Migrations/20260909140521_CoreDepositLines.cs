using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoPartsErp.Modules.Sales.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CoreDepositLines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "core_for_line_id",
                schema: "sales",
                table: "sales_order_lines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "kind",
                schema: "sales",
                table: "sales_order_lines",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "core_for_line_id",
                schema: "sales",
                table: "sales_order_lines");

            migrationBuilder.DropColumn(
                name: "kind",
                schema: "sales",
                table: "sales_order_lines");
        }
    }
}
