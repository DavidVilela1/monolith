using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AutoPartsErp.Modules.Purchasing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierAgreementsAndInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "supplier_agreements",
                schema: "purchasing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    rappel_basis = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    rappel_period = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    modified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_supplier_agreements", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "supplier_invoices",
                schema: "purchasing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    received_on = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    rappel_rate_percent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    supplier_document_number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    document_date = table.Column<DateOnly>(type: "date", nullable: true),
                    stated_gross_total = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    stated_gross_total_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    modified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_supplier_invoices", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "supplier_prices",
                schema: "purchasing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    part_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_price_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    supplier_part_number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    modified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_supplier_prices", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "supplier_rappel_steps",
                schema: "purchasing",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    from_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    from_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    percent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    supplier_agreement_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_supplier_rappel_steps", x => x.id);
                    table.ForeignKey(
                        name: "fk_supplier_rappel_steps_supplier_agreements_supplier_agreemen",
                        column: x => x.supplier_agreement_id,
                        principalSchema: "purchasing",
                        principalTable: "supplier_agreements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "supplier_invoice_lines",
                schema: "purchasing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    purchase_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purchase_order_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    part_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    quantity_unit = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_price_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    vat_rate_percent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    price_source_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    modified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    supplier_invoice_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_supplier_invoice_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_supplier_invoice_lines_supplier_invoices_supplier_invoice_id",
                        column: x => x.supplier_invoice_id,
                        principalSchema: "purchasing",
                        principalTable: "supplier_invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_supplier_agreements_tenant_supplier_from",
                schema: "purchasing",
                table: "supplier_agreements",
                columns: new[] { "tenant_id", "supplier_id", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ux_supplier_agreements_tenant_supplier_live",
                schema: "purchasing",
                table: "supplier_agreements",
                columns: new[] { "tenant_id", "supplier_id" },
                unique: true,
                filter: "effective_to IS NULL AND is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_supplier_invoice_lines_supplier_invoice_id",
                schema: "purchasing",
                table: "supplier_invoice_lines",
                column: "supplier_invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_supplier_invoice_lines_tenant_order_line",
                schema: "purchasing",
                table: "supplier_invoice_lines",
                columns: new[] { "tenant_id", "purchase_order_line_id" });

            migrationBuilder.CreateIndex(
                name: "ix_supplier_invoices_tenant_status_day",
                schema: "purchasing",
                table: "supplier_invoices",
                columns: new[] { "tenant_id", "status", "received_on" });

            migrationBuilder.CreateIndex(
                name: "ix_supplier_invoices_tenant_supplier_document_date",
                schema: "purchasing",
                table: "supplier_invoices",
                columns: new[] { "tenant_id", "supplier_id", "document_date" });

            migrationBuilder.CreateIndex(
                name: "ux_supplier_invoices_tenant_supplier_day_draft",
                schema: "purchasing",
                table: "supplier_invoices",
                columns: new[] { "tenant_id", "supplier_id", "received_on" },
                unique: true,
                filter: "status = 'Drafted' AND is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_supplier_prices_tenant_supplier_part",
                schema: "purchasing",
                table: "supplier_prices",
                columns: new[] { "tenant_id", "supplier_id", "part_id" });

            migrationBuilder.CreateIndex(
                name: "ux_supplier_prices_tenant_supplier_part_from",
                schema: "purchasing",
                table: "supplier_prices",
                columns: new[] { "tenant_id", "supplier_id", "part_id", "effective_from" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_supplier_rappel_steps_supplier_agreement_id",
                schema: "purchasing",
                table: "supplier_rappel_steps",
                column: "supplier_agreement_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "supplier_invoice_lines",
                schema: "purchasing");

            migrationBuilder.DropTable(
                name: "supplier_prices",
                schema: "purchasing");

            migrationBuilder.DropTable(
                name: "supplier_rappel_steps",
                schema: "purchasing");

            migrationBuilder.DropTable(
                name: "supplier_invoices",
                schema: "purchasing");

            migrationBuilder.DropTable(
                name: "supplier_agreements",
                schema: "purchasing");
        }
    }
}
