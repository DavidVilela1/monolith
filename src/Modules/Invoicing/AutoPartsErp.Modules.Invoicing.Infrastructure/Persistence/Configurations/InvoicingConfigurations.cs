using AutoPartsErp.Modules.Invoicing.Domain;
using AutoPartsErp.Modules.Invoicing.Domain.Invoices;
using AutoPartsErp.Modules.Invoicing.Domain.Series;
using AutoPartsErp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AutoPartsErp.Modules.Invoicing.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="DocumentSeries"/> onto <c>invoicing.document_series</c>.</summary>
public sealed class DocumentSeriesConfiguration : IEntityTypeConfiguration<DocumentSeries>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<DocumentSeries> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("document_series");

        builder.HasKey(series => series.Id);

        builder.Property(series => series.Id)
            .HasConversion(id => id.Value, value => new DocumentSeriesId(value))
            .ValueGeneratedNever();

        builder.Property(series => series.Version)
            .IsRowVersion()
            .HasColumnName("xmin")
            .HasColumnType("xid");

        builder.Property(series => series.TenantId).IsRequired();

        builder.Property(series => series.Type)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(series => series.Code)
            .HasMaxLength(DocumentSeries.MaxCodeLength)
            .IsRequired();

        builder.Property(series => series.Year).IsRequired();

        builder.Property(series => series.ValidationCode)
            .HasColumnName("validation_code")
            .HasMaxLength(DocumentSeries.MaxValidationCodeLength);

        builder.Property(series => series.NextNumber).HasColumnName("next_number").IsRequired();

        builder.Property(series => series.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(series => series.ValidatedAtUtc).HasColumnName("validated_at_utc");
        builder.Property(series => series.ClosedAtUtc).HasColumnName("closed_at_utc");

        builder.Property(series => series.CreatedAtUtc).IsRequired();
        builder.Property(series => series.CreatedBy).HasMaxLength(120).IsRequired();
        builder.Property(series => series.ModifiedBy).HasMaxLength(120);

        // One code per document type per year. The command checks first so a duplicate reads as a
        // 409; this holds under two people opening the same series at once.
        builder.HasIndex(series => new { series.TenantId, series.Type, series.Code, series.Year })
            .IsUnique()
            .HasDatabaseName("ux_document_series_tenant_type_code_year");

        // What "the live series for FT in 2026" resolves through, on every single issue.
        builder.HasIndex(series => new { series.TenantId, series.Type, series.Year, series.Status })
            .HasDatabaseName("ix_document_series_tenant_type_year_status");
    }
}

/// <summary>
/// Maps <see cref="Invoice"/> onto <c>invoicing.invoices</c> and its lines onto
/// <c>invoicing.invoice_lines</c>.
/// </summary>
public sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("invoices");

        builder.HasKey(invoice => invoice.Id);

        builder.Property(invoice => invoice.Id)
            .HasConversion(id => id.Value, value => new InvoiceId(value))
            .ValueGeneratedNever();

        builder.Property(invoice => invoice.Version)
            .IsRowVersion()
            .HasColumnName("xmin")
            .HasColumnType("xid");

        builder.Property(invoice => invoice.TenantId).IsRequired();

        builder.Property(invoice => invoice.Type)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(invoice => invoice.CustomerId)
            .HasConversion(customer => customer.Value, value => new CustomerRef(value))
            .HasColumnName("customer_id")
            .IsRequired();

        builder.Property(invoice => invoice.CustomerName)
            .HasColumnName("customer_name")
            .HasMaxLength(Invoice.MaxCustomerNameLength)
            .IsRequired();

        builder.Property(invoice => invoice.CustomerTaxNumber)
            .HasColumnName("customer_tax_number")
            .HasMaxLength(20);

        builder.Property(invoice => invoice.CustomerCountry)
            .HasColumnName("customer_country")
            .HasMaxLength(2)
            .IsRequired();

        builder.Property(invoice => invoice.CurrencyCode)
            .HasColumnName("currency_code")
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(invoice => invoice.TaxRegion)
            .HasConversion<string>()
            .HasColumnName("tax_region")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(invoice => invoice.DocumentDate).HasColumnName("document_date").IsRequired();

        // A converter declared for the non-nullable type, which EF lifts onto the nullable
        // property. Writing the lambda against SalesOrderRef? instead would not compile against
        // any HasConversion overload, and the shape that does is the one Purchasing already uses
        // for its nullable order reference.
        builder.Property(invoice => invoice.SalesOrderId)
            .HasConversion(new ValueConverter<SalesOrderRef, Guid>(
                order => order.Value, value => new SalesOrderRef(value)))
            .HasColumnName("sales_order_id");

        builder.Property(invoice => invoice.SeriesId)
            .HasConversion(new ValueConverter<DocumentSeriesId, Guid>(
                series => series.Value, value => new DocumentSeriesId(value)))
            .HasColumnName("series_id");

        builder.Property(invoice => invoice.DocumentNumber)
            .HasColumnName("document_number")
            .HasMaxLength(60)
            .IsRequired();

        builder.Property(invoice => invoice.SeriesNumber).HasColumnName("series_number").IsRequired();

        // The ATCUD and the signature are owned values rather than plain strings, because both
        // are a pair of facts that only mean anything together: a validation code without its
        // number is not a code, and a signature without its four printed characters cannot be
        // checked against the page.
        builder.OwnsOne(invoice => invoice.Atcud, atcud =>
        {
            atcud.Property(value => value.ValidationCode)
                .HasColumnName("atcud_validation_code")
                .HasMaxLength(DocumentSeries.MaxValidationCodeLength);

            atcud.Property(value => value.Number).HasColumnName("atcud_number");
        });

        builder.OwnsOne(invoice => invoice.Signature, signature =>
        {
            // 172 characters for a 1024-bit key, more for a larger one. Not capped tightly,
            // because a deployment that upgrades to 2048 bits should not need a migration.
            signature.Property(value => value.Value)
                .HasColumnName("signature")
                .HasMaxLength(1000);

            signature.Property(value => value.Printed)
                .HasColumnName("signature_printed")
                .HasMaxLength(4);
        });

        builder.Property(invoice => invoice.QrCode)
            .HasColumnName("qr_code")
            .HasMaxLength(1000);

        builder.Property(invoice => invoice.SystemEntryDateUtc).HasColumnName("system_entry_date_utc");

        builder.Property(invoice => invoice.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(invoice => invoice.VoidReason)
            .HasColumnName("void_reason")
            .HasMaxLength(Invoice.MaxVoidReasonLength);

        builder.Property(invoice => invoice.VoidedAtUtc).HasColumnName("voided_at_utc");

        builder.Property(invoice => invoice.CreatedAtUtc).IsRequired();
        builder.Property(invoice => invoice.CreatedBy).HasMaxLength(120).IsRequired();
        builder.Property(invoice => invoice.ModifiedBy).HasMaxLength(120);

        builder.OwnsMany(invoice => invoice.Lines, line =>
        {
            line.ToTable("invoice_lines");
            line.WithOwner().HasForeignKey("invoice_id");

            line.HasKey(item => item.Id);

            line.Property(item => item.Id)
                .HasConversion(id => id.Value, value => new InvoiceLineId(value))
                .HasColumnName("id")
                .ValueGeneratedNever();

            line.Property(item => item.TenantId).IsRequired();
            line.Property(item => item.Number).HasColumnName("line_number").IsRequired();

            line.Property(item => item.PartId)
                .HasConversion(part => part.Value, value => new PartRef(value))
                .HasColumnName("part_id")
                .IsRequired();

            line.Property(item => item.Sku).HasMaxLength(InvoiceLine.MaxSkuLength).IsRequired();

            line.Property(item => item.Description)
                .HasMaxLength(InvoiceLine.MaxDescriptionLength)
                .IsRequired();

            line.OwnsOne(item => item.Quantity, quantity =>
            {
                quantity.Property(value => value.Value)
                    .HasColumnName("quantity")
                    .HasPrecision(18, 4)
                    .IsRequired();

                quantity.Property(value => value.Unit)
                    .HasColumnName("unit_code")
                    .HasConversion(
                        unit => unit.Code,
                        code => UnitOfMeasure.FromCode(code),
                        new ValueComparer<UnitOfMeasure>(
                            (left, right) => left!.Code == right!.Code,
                            unit => unit.Code.GetHashCode(StringComparison.Ordinal),
                            unit => UnitOfMeasure.FromCode(unit.Code)))
                    .HasMaxLength(10)
                    .IsRequired();
            });

            line.Navigation(item => item.Quantity).IsRequired();

            line.OwnsOne(item => item.UnitPrice, price =>
            {
                price.Property(value => value.Amount)
                    .HasColumnName("unit_price")
                    .HasPrecision(18, 4)
                    .IsRequired();

                price.Property(value => value.Currency)
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

            line.Navigation(item => item.UnitPrice).IsRequired();

            line.Property(item => item.DiscountPercent)
                .HasColumnName("discount_percent")
                .HasPrecision(9, 4)
                .IsRequired();

            line.OwnsOne(item => item.VatRate, rate =>
            {
                rate.Property(value => value.Category)
                    .HasConversion<string>()
                    .HasColumnName("vat_category")
                    .HasMaxLength(20)
                    .IsRequired();

                rate.Property(value => value.Percent)
                    .HasColumnName("vat_percent")
                    .HasPrecision(9, 4)
                    .IsRequired();

                rate.Property(value => value.ExemptionCode)
                    .HasColumnName("vat_exemption_code")
                    .HasMaxLength(VatRate.MaxExemptionCodeLength);

                rate.Property(value => value.ExemptionReason)
                    .HasColumnName("vat_exemption_reason")
                    .HasMaxLength(VatRate.MaxExemptionReasonLength);
            });

            line.Navigation(item => item.VatRate).IsRequired();
        });

        // A document number is unique within a tenant, and the index is filtered because every
        // draft carries an empty one. Without the filter the second draft would collide with the
        // first, which is a strange way to find out that drafts have no number.
        builder.HasIndex(invoice => new { invoice.TenantId, invoice.DocumentNumber })
            .IsUnique()
            .HasFilter("document_number <> ''")
            .HasDatabaseName("ux_invoices_tenant_number");

        // What the chain reads on every issue: the last signature in a series. The series number
        // is in the index rather than only the series, so that read is a single backwards seek
        // rather than a sort over every document the series has ever issued — which, on a series
        // in its eleventh month, is the difference between a lookup and a table scan on the hot
        // path of every single sale.
        builder.HasIndex(invoice => new { invoice.TenantId, invoice.SeriesId, invoice.SeriesNumber })
            .HasDatabaseName("ix_invoices_tenant_series_number");

        builder.HasIndex(invoice => new { invoice.TenantId, invoice.CustomerId, invoice.DocumentDate })
            .HasDatabaseName("ix_invoices_tenant_customer_date");

        builder.HasIndex(invoice => new { invoice.TenantId, invoice.DocumentDate })
            .HasDatabaseName("ix_invoices_tenant_date");
    }
}
