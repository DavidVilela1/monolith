using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoPartsErp.Modules.Sales.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PartialInvoicing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
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

            migrationBuilder.RenameColumn(
                name: "invoiced_on",
                schema: "sales",
                table: "sales_orders",
                newName: "last_invoiced_on");

            migrationBuilder.AddColumn<string>(
                name: "invoicing_status",
                schema: "sales",
                table: "sales_orders",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "invoiced_quantity",
                schema: "sales",
                table: "sales_order_lines",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "invoiced_quantity_unit",
                schema: "sales",
                table: "sales_order_lines",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "ix_sales_orders_tenant_awaiting_invoice",
                schema: "sales",
                table: "sales_orders",
                columns: new[] { "tenant_id", "status", "invoicing_status" },
                filter: "invoicing_status <> 'Invoiced'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_sales_orders_tenant_awaiting_invoice",
                schema: "sales",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "invoicing_status",
                schema: "sales",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "invoiced_quantity",
                schema: "sales",
                table: "sales_order_lines");

            migrationBuilder.DropColumn(
                name: "invoiced_quantity_unit",
                schema: "sales",
                table: "sales_order_lines");

            migrationBuilder.RenameColumn(
                name: "last_invoiced_on",
                schema: "sales",
                table: "sales_orders",
                newName: "invoiced_on");

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

            migrationBuilder.CreateIndex(
                name: "ix_sales_orders_tenant_awaiting_invoice",
                schema: "sales",
                table: "sales_orders",
                columns: new[] { "tenant_id", "status", "invoice_id" },
                filter: "invoice_id IS NULL");
        }
    }
}
