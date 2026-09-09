using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoPartsErp.Modules.Invoicing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreditNoteFromReturn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "customer_return_id",
                schema: "invoicing",
                table: "invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_invoices_tenant_customer_return",
                schema: "invoicing",
                table: "invoices",
                columns: new[] { "tenant_id", "customer_return_id" },
                unique: true,
                filter: "customer_return_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_invoices_tenant_customer_return",
                schema: "invoicing",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "customer_return_id",
                schema: "invoicing",
                table: "invoices");
        }
    }
}
