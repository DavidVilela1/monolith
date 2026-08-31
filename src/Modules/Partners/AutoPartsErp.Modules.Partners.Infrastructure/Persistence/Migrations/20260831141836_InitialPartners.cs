using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AutoPartsErp.Modules.Partners.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialPartners : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "partners");

            migrationBuilder.CreateTable(
                name: "partners",
                schema: "partners",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    legal_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    trading_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    tax_country_code = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    tax_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    tax_number_verified = table.Column<bool>(type: "boolean", nullable: false),
                    roles = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    hold_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("pk_partners", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "partner_addresses",
                schema: "partners",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    line1 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    line2 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    postcode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    city = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    country_code = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    partner_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_partner_addresses", x => x.id);
                    table.ForeignKey(
                        name: "fk_partner_addresses_partners_partner_id",
                        column: x => x.partner_id,
                        principalSchema: "partners",
                        principalTable: "partners",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "partner_contacts",
                schema: "partners",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    role = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false),
                    partner_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_partner_contacts", x => x.id);
                    table.ForeignKey(
                        name: "fk_partner_contacts_partners_partner_id",
                        column: x => x.partner_id,
                        principalSchema: "partners",
                        principalTable: "partners",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "partner_customer_terms",
                schema: "partners",
                columns: table => new
                {
                    partner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    credit_limit = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    credit_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    payment_due_in_days = table.Column<int>(type: "integer", nullable: false),
                    payment_method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    payment_end_of_month = table.Column<bool>(type: "boolean", nullable: false),
                    price_list_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_partner_customer_terms", x => x.partner_id);
                    table.ForeignKey(
                        name: "fk_partner_customer_terms_partners_partner_id",
                        column: x => x.partner_id,
                        principalSchema: "partners",
                        principalTable: "partners",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "partner_supplier_terms",
                schema: "partners",
                columns: table => new
                {
                    partner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_due_in_days = table.Column<int>(type: "integer", nullable: false),
                    payment_method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    payment_end_of_month = table.Column<bool>(type: "boolean", nullable: false),
                    lead_time_days = table.Column<int>(type: "integer", nullable: false),
                    minimum_order_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    minimum_order_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    our_account_number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_partner_supplier_terms", x => x.partner_id);
                    table.ForeignKey(
                        name: "fk_partner_supplier_terms_partners_partner_id",
                        column: x => x.partner_id,
                        principalSchema: "partners",
                        principalTable: "partners",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_partner_addresses_partner_kind",
                schema: "partners",
                table: "partner_addresses",
                columns: new[] { "partner_id", "kind" });

            migrationBuilder.CreateIndex(
                name: "ix_partner_contacts_partner_id",
                schema: "partners",
                table: "partner_contacts",
                column: "partner_id");

            migrationBuilder.CreateIndex(
                name: "ix_partners_roles",
                schema: "partners",
                table: "partners",
                column: "roles");

            migrationBuilder.CreateIndex(
                name: "ix_partners_status",
                schema: "partners",
                table: "partners",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_partners_tax_number",
                schema: "partners",
                table: "partners",
                columns: new[] { "tax_country_code", "tax_number" });

            migrationBuilder.CreateIndex(
                name: "ux_partners_tenant_code",
                schema: "partners",
                table: "partners",
                columns: new[] { "tenant_id", "code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "partner_addresses",
                schema: "partners");

            migrationBuilder.DropTable(
                name: "partner_contacts",
                schema: "partners");

            migrationBuilder.DropTable(
                name: "partner_customer_terms",
                schema: "partners");

            migrationBuilder.DropTable(
                name: "partner_supplier_terms",
                schema: "partners");

            migrationBuilder.DropTable(
                name: "partners",
                schema: "partners");
        }
    }
}
