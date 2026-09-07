using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoPartsErp.Modules.Invoicing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreditNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "invoicing");

            migrationBuilder.CreateTable(
                name: "document_series",
                schema: "invoicing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    validation_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    next_number = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    validated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    modified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_series", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "inbox_messages",
                schema: "invoicing",
                columns: table => new
                {
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    handler_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    handled_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbox_messages", x => new { x.message_id, x.handler_name });
                });

            migrationBuilder.CreateTable(
                name: "invoices",
                schema: "invoicing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    customer_tax_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    customer_country = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    tax_region = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    document_date = table.Column<DateOnly>(type: "date", nullable: false),
                    sales_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    credited_invoice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    credited_document_number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    credit_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    series_id = table.Column<Guid>(type: "uuid", nullable: true),
                    document_number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    series_number = table.Column<int>(type: "integer", nullable: false),
                    atcud_validation_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    atcud_number = table.Column<int>(type: "integer", nullable: true),
                    signature = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    signature_printed = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    qr_code = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    system_entry_date_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    void_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    voided_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    modified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoices", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "invoicing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    content = table.Column<string>(type: "jsonb", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    error = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    next_attempt_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "invoice_lines",
                schema: "invoicing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_number = table.Column<int>(type: "integer", nullable: false),
                    part_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_price_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    discount_percent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    vat_category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    vat_percent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    vat_exemption_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    vat_exemption_reason = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    credits_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    credited_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    credited_quantity_unit = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoice_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_invoice_lines_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalSchema: "invoicing",
                        principalTable: "invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_document_series_tenant_type_year_status",
                schema: "invoicing",
                table: "document_series",
                columns: new[] { "tenant_id", "type", "year", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_document_series_tenant_type_code_year",
                schema: "invoicing",
                table: "document_series",
                columns: new[] { "tenant_id", "type", "code", "year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inbox_messages_handled_at",
                schema: "invoicing",
                table: "inbox_messages",
                column: "handled_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_invoice_lines_invoice_id",
                schema: "invoicing",
                table: "invoice_lines",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_tenant_credited",
                schema: "invoicing",
                table: "invoices",
                columns: new[] { "tenant_id", "credited_invoice_id" },
                filter: "credited_invoice_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_tenant_customer_date",
                schema: "invoicing",
                table: "invoices",
                columns: new[] { "tenant_id", "customer_id", "document_date" });

            migrationBuilder.CreateIndex(
                name: "ix_invoices_tenant_date",
                schema: "invoicing",
                table: "invoices",
                columns: new[] { "tenant_id", "document_date" });

            migrationBuilder.CreateIndex(
                name: "ix_invoices_tenant_series_number",
                schema: "invoicing",
                table: "invoices",
                columns: new[] { "tenant_id", "series_id", "series_number" });

            migrationBuilder.CreateIndex(
                name: "ux_invoices_tenant_number",
                schema: "invoicing",
                table: "invoices",
                columns: new[] { "tenant_id", "document_number" },
                unique: true,
                filter: "document_number <> ''");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pending",
                schema: "invoicing",
                table: "outbox_messages",
                columns: new[] { "next_attempt_at_utc", "occurred_at_utc" },
                filter: "processed_at_utc IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "document_series",
                schema: "invoicing");

            migrationBuilder.DropTable(
                name: "inbox_messages",
                schema: "invoicing");

            migrationBuilder.DropTable(
                name: "invoice_lines",
                schema: "invoicing");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "invoicing");

            migrationBuilder.DropTable(
                name: "invoices",
                schema: "invoicing");
        }
    }
}
