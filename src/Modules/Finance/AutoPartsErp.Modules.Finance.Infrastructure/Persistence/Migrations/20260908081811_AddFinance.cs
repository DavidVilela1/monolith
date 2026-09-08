using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoPartsErp.Modules.Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFinance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "finance");

            migrationBuilder.CreateTable(
                name: "customer_terms",
                schema: "finance",
                columns: table => new
                {
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    legal_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    payment_due_in_days = table.Column<int>(type: "integer", nullable: false),
                    payment_end_of_month = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    modified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "text", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer_terms", x => x.customer_id);
                });

            migrationBuilder.CreateTable(
                name: "inbox_messages",
                schema: "finance",
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
                name: "number_sequences",
                schema: "finance",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence_key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    next_number = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_number_sequences", x => new { x.tenant_id, x.sequence_key, x.year });
                });

            migrationBuilder.CreateTable(
                name: "open_items",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    original_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    settled_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    settled_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    document_date = table.Column<DateOnly>(type: "date", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    cancellation_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    modified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "text", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_open_items", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "finance",
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
                name: "receipts",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    allocated_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    allocated_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    received_on = table.Column<DateOnly>(type: "date", nullable: false),
                    method = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    modified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "text", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_receipts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "receipt_allocations",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    open_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    allocated_on = table.Column<DateOnly>(type: "date", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receipt_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_receipt_allocations", x => x.id);
                    table.ForeignKey(
                        name: "fk_receipt_allocations_receipts_receipt_id",
                        column: x => x.receipt_id,
                        principalSchema: "finance",
                        principalTable: "receipts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_customer_terms_tenant_code",
                schema: "finance",
                table: "customer_terms",
                columns: new[] { "tenant_id", "code" });

            migrationBuilder.CreateIndex(
                name: "ix_inbox_messages_handled_at",
                schema: "finance",
                table: "inbox_messages",
                column: "handled_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_open_items_tenant_outstanding",
                schema: "finance",
                table: "open_items",
                columns: new[] { "tenant_id", "customer_id", "due_date" },
                filter: "status IN ('Open', 'PartiallySettled')");

            migrationBuilder.CreateIndex(
                name: "ux_open_items_tenant_document",
                schema: "finance",
                table: "open_items",
                columns: new[] { "tenant_id", "document_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pending",
                schema: "finance",
                table: "outbox_messages",
                columns: new[] { "next_attempt_at_utc", "occurred_at_utc" },
                filter: "processed_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_receipt_allocations_open_item",
                schema: "finance",
                table: "receipt_allocations",
                column: "open_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_receipt_allocations_receipt_id",
                schema: "finance",
                table: "receipt_allocations",
                column: "receipt_id");

            migrationBuilder.CreateIndex(
                name: "ix_receipts_tenant_unallocated",
                schema: "finance",
                table: "receipts",
                columns: new[] { "tenant_id", "customer_id", "received_on" },
                filter: "status IN ('Unallocated', 'PartiallyAllocated')");

            migrationBuilder.CreateIndex(
                name: "ux_receipts_tenant_number",
                schema: "finance",
                table: "receipts",
                columns: new[] { "tenant_id", "number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "customer_terms",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "inbox_messages",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "number_sequences",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "open_items",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "receipt_allocations",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "receipts",
                schema: "finance");
        }
    }
}
