using AutoPartsErp.Modules.Purchasing.Domain;
using AutoPartsErp.Modules.Purchasing.Domain.Invoices;
using AutoPartsErp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AutoPartsErp.Modules.Purchasing.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="SupplierInvoice"/> onto <c>purchasing.supplier_invoices</c> and its lines onto
/// <c>purchasing.supplier_invoice_lines</c>.
/// <para>
/// No unique index on the supplier's document number, deliberately. It is their sequence, not
/// ours, and suppliers reuse numbers across years, restart series after changing software, and
/// send the same number on a duplicate copy of a document that has already been settled. A unique
/// constraint on somebody else's numbering is a constraint the company cannot fix when it fires.
/// </para>
/// </summary>
public sealed class SupplierInvoiceConfiguration : IEntityTypeConfiguration<SupplierInvoice>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SupplierInvoice> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("supplier_invoices");

        builder.HasKey(invoice => invoice.Id);

        builder.Property(invoice => invoice.Id)
            .HasConversion(id => id.Value, value => new SupplierInvoiceId(value))
            .ValueGeneratedNever();

        builder.Property(invoice => invoice.Version)
            .IsRowVersion()
            .HasColumnName("xmin")
            .HasColumnType("xid");

        builder.Property(invoice => invoice.TenantId).IsRequired();

        builder.Property(invoice => invoice.SupplierId)
            .HasConversion(id => id.Value, value => new SupplierRef(value))
            .HasColumnName("supplier_id")
            .IsRequired();

        builder.Property(invoice => invoice.SupplierCode)
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(invoice => invoice.CurrencyCode)
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(invoice => invoice.ReceivedOn).IsRequired();

        builder.Property(invoice => invoice.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(invoice => invoice.RappelRatePercent)
            .HasPrecision(9, 4)
            .IsRequired();

        builder.Property(invoice => invoice.SupplierDocumentNumber)
            .HasMaxLength(SupplierInvoice.MaxDocumentNumberLength);

        builder.Property(invoice => invoice.DocumentDate);

        // Nullable, because until their paper arrives there is nothing they have said. Null is
        // not zero here: zero would be a supplier claiming the delivery was free.
        builder.OwnsOne(invoice => invoice.StatedGrossTotal, money =>
        {
            money.Property(m => m.Amount)
                .HasColumnName("stated_gross_total")
                .HasPrecision(18, 4);

            money.Property(m => m.Currency)
                .HasColumnName("stated_gross_total_currency")
                .HasConversion(
                    currency => currency.Code,
                    code => Currency.FromCode(code),
                    new ValueComparer<Currency>(
                        (left, right) => left!.Code == right!.Code,
                        currency => currency.Code.GetHashCode(StringComparison.Ordinal),
                        currency => Currency.FromCode(currency.Code)))
                .HasMaxLength(3);
        });

        builder.Property(invoice => invoice.Reason).HasMaxLength(SupplierInvoice.MaxReasonLength);

        builder.Property(invoice => invoice.CreatedAtUtc).IsRequired();
        builder.Property(invoice => invoice.CreatedBy).HasMaxLength(120).IsRequired();
        builder.Property(invoice => invoice.ModifiedBy).HasMaxLength(120);
        builder.Property(invoice => invoice.DeletedBy).HasMaxLength(120);

        // Everything below is computed from the lines and the stamped rate. Storing any of it
        // would create a second copy of a figure that has to agree with the first one forever.
        builder.Ignore(invoice => invoice.LinesTotal);
        builder.Ignore(invoice => invoice.RappelAmount);
        builder.Ignore(invoice => invoice.NetTotal);
        builder.Ignore(invoice => invoice.VatTotal);
        builder.Ignore(invoice => invoice.GrossTotal);
        builder.Ignore(invoice => invoice.Difference);

        ConfigureLines(builder);

        // At most one open draft per supplier per delivery day. This is what makes a receipt land
        // beside the ones that came on the same van rather than opening a document of its own,
        // and it is enforced here as well as in the handler because two lines of one delivery can
        // be confirmed by two people in the same second.
        builder.HasIndex(invoice => new { invoice.TenantId, invoice.SupplierId, invoice.ReceivedOn })
            .IsUnique()
            .HasFilter("status = 'Drafted' AND is_deleted = false")
            .HasDatabaseName("ux_supplier_invoices_tenant_supplier_day_draft");

        // The two screens: what is waiting for paper, and what is in dispute.
        builder.HasIndex(invoice => new { invoice.TenantId, invoice.Status, invoice.ReceivedOn })
            .HasDatabaseName("ix_supplier_invoices_tenant_status_day");

        // Reconciling their statement: everything from this supplier, newest first.
        builder.HasIndex(invoice => new { invoice.TenantId, invoice.SupplierId, invoice.DocumentDate })
            .HasDatabaseName("ix_supplier_invoices_tenant_supplier_document_date");
    }

    private static void ConfigureLines(EntityTypeBuilder<SupplierInvoice> builder)
    {
        builder.OwnsMany(invoice => invoice.Lines, line =>
        {
            line.ToTable("supplier_invoice_lines");
            line.WithOwner().HasForeignKey("supplier_invoice_id");

            line.HasKey(l => l.Id);

            line.Property(l => l.Id)
                .HasConversion(id => id.Value, value => new SupplierInvoiceLineId(value))
                .HasColumnName("id")
                .ValueGeneratedNever();

            line.Property(l => l.PurchaseOrderId)
                .HasConversion(id => id.Value, value => new PurchaseOrderId(value))
                .HasColumnName("purchase_order_id")
                .IsRequired();

            line.Property(l => l.PurchaseOrderLineId)
                .HasConversion(id => id.Value, value => new PurchaseOrderLineId(value))
                .HasColumnName("purchase_order_line_id")
                .IsRequired();

            line.Property(l => l.PartId)
                .HasConversion(id => id.Value, value => new PartRef(value))
                .HasColumnName("part_id")
                .IsRequired();

            line.Property(l => l.Sku).HasMaxLength(40).IsRequired();

            line.Property(l => l.Description)
                .HasMaxLength(SupplierInvoiceLine.MaxDescriptionLength)
                .IsRequired();

            line.OwnsOne(l => l.Quantity, quantity =>
            {
                quantity.Property(q => q.Value)
                    .HasColumnName("quantity")
                    .HasPrecision(18, 4)
                    .IsRequired();

                quantity.Property(q => q.Unit)
                    .HasColumnName("quantity_unit")
                    .HasConversion(
                        unit => unit.Code,
                        code => UnitOfMeasure.FromCode(code),
                        new ValueComparer<UnitOfMeasure>(
                            (left, right) => left!.Code == right!.Code,
                            unit => unit.Code.GetHashCode(StringComparison.Ordinal),
                            unit => UnitOfMeasure.FromCode(unit.Code)))
                    .HasMaxLength(8)
                    .IsRequired();
            });

            line.Navigation(l => l.Quantity).IsRequired();

            line.OwnsOne(l => l.UnitPrice, price =>
            {
                price.Property(m => m.Amount)
                    .HasColumnName("unit_price")
                    .HasPrecision(18, 4)
                    .IsRequired();

                price.Property(m => m.Currency)
                    .HasColumnName("unit_price_currency")
                    .HasConversion(
                        currency => currency.Code,
                        code => Currency.FromCode(code),
                        new ValueComparer<Currency>(
                            (left, right) => left!.Code == right!.Code,
                            currency => currency.Code.GetHashCode(StringComparison.Ordinal),
                            currency => Currency.FromCode(currency.Code)))
                    .HasMaxLength(3)
                    .IsRequired();
            });

            line.Navigation(l => l.UnitPrice).IsRequired();

            line.Property(l => l.VatRatePercent).HasPrecision(9, 4).IsRequired();

            // Null once somebody has accepted the supplier's own figure on the line: at that point
            // it is not priced from anything, and saying otherwise would be the document claiming
            // an agreement it no longer follows.
            line.Property(l => l.PriceSource)
                .HasConversion(new ValueConverter<SupplierPriceId, Guid>(
                    id => id.Value, value => new SupplierPriceId(value)))
                .HasColumnName("price_source_id");

            line.Ignore(l => l.LineTotal);

            line.Property(l => l.TenantId).IsRequired();
            line.Property(l => l.CreatedAtUtc).IsRequired();
            line.Property(l => l.CreatedBy).HasMaxLength(120).IsRequired();
            line.Property(l => l.ModifiedBy).HasMaxLength(120);

            // The idempotence guard's index: "has this receipt already been charged for?".
            line.HasIndex(l => new { l.TenantId, l.PurchaseOrderLineId })
                .HasDatabaseName("ix_supplier_invoice_lines_tenant_order_line");
        });

        builder.Navigation(invoice => invoice.Lines)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
