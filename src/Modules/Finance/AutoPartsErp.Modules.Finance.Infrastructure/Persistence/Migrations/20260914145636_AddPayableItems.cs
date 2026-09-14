using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoPartsErp.Modules.Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayableItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payable_items",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    document_number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    supplier_invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    original_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    settled_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    settled_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    document_date = table.Column<DateOnly>(type: "date", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    cancellation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    modified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "text", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payable_items", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payable_items_tenant_due",
                schema: "finance",
                table: "payable_items",
                columns: new[] { "tenant_id", "due_date" },
                filter: "status IN ('Open', 'PartiallySettled')");

            migrationBuilder.CreateIndex(
                name: "ix_payable_items_tenant_supplier_outstanding",
                schema: "finance",
                table: "payable_items",
                columns: new[] { "tenant_id", "supplier_id", "due_date" },
                filter: "status IN ('Open', 'PartiallySettled')");

            migrationBuilder.CreateIndex(
                name: "ux_payable_items_tenant_document",
                schema: "finance",
                table: "payable_items",
                columns: new[] { "tenant_id", "supplier_invoice_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payable_items",
                schema: "finance");
        }
    }
}
