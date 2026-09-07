using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoPartsErp.Modules.Sales.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SalesOrderInvoicing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "invoice_document_number",
                schema: "sales",
                table: "sales_orders",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "invoice_id",
                schema: "sales",
                table: "sales_orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "invoiced_on",
                schema: "sales",
                table: "sales_orders",
                type: "date",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_sales_orders_tenant_awaiting_invoice",
                schema: "sales",
                table: "sales_orders",
                columns: new[] { "tenant_id", "status", "invoice_id" },
                filter: "invoice_id IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_sales_orders_tenant_awaiting_invoice",
                schema: "sales",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "invoice_document_number",
                schema: "sales",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "invoice_id",
                schema: "sales",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "invoiced_on",
                schema: "sales",
                table: "sales_orders");
        }
    }
}
