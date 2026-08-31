using AutoPartsErp.Modules.Partners.Domain;
using AutoPartsErp.Modules.Partners.Domain.Partners;
using AutoPartsErp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoPartsErp.Modules.Partners.Infrastructure.Persistence.Configurations;

/// <summary>Maps the <see cref="Partner"/> aggregate onto <c>partners.partners</c>.</summary>
public sealed class PartnerConfiguration : IEntityTypeConfiguration<Partner>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Partner> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("partners");

        builder.HasKey(partner => partner.Id);

        builder.Property(partner => partner.Id)
            .HasConversion(id => id.Value, value => new PartnerId(value))
            .ValueGeneratedNever();

        builder.Property(partner => partner.Version)
            .IsRowVersion()
            .HasColumnName("xmin")
            .HasColumnType("xid");

        builder.Property(partner => partner.TenantId).IsRequired();
        builder.Property(partner => partner.Code).HasMaxLength(Partner.MaxCodeLength).IsRequired();
        builder.Property(partner => partner.LegalName).HasMaxLength(Partner.MaxNameLength).IsRequired();
        builder.Property(partner => partner.TradingName).HasMaxLength(Partner.MaxNameLength);

        // Flags enum stored as its integer value. Storing it as a string would work until the
        // day somebody is both customer and supplier, and "Customer, Supplier" starts appearing
        // in the column.
        builder.Property(partner => partner.Roles)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(partner => partner.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(partner => partner.HoldReason).HasMaxLength(500);

        ConfigureTaxNumber(builder);
        ConfigureCustomerTerms(builder);
        ConfigureSupplierTerms(builder);
        ConfigureAddresses(builder);
        ConfigureContacts(builder);

        builder.Property(partner => partner.CreatedAtUtc).IsRequired();
        builder.Property(partner => partner.CreatedBy).HasMaxLength(120).IsRequired();
        builder.Property(partner => partner.ModifiedBy).HasMaxLength(120);
        builder.Property(partner => partner.DeletedBy).HasMaxLength(120);
        builder.Property(partner => partner.IsDeleted).IsRequired();

        builder.HasIndex(partner => new { partner.TenantId, partner.Code })
            .IsUnique()
            .HasDatabaseName("ux_partners_tenant_code");

        builder.HasIndex(partner => partner.Status).HasDatabaseName("ix_partners_status");
        builder.HasIndex(partner => partner.Roles).HasDatabaseName("ix_partners_roles");
    }

    private static void ConfigureTaxNumber(EntityTypeBuilder<Partner> builder)
    {
        builder.OwnsOne(partner => partner.TaxNumber, tax =>
        {
            tax.Property(t => t.CountryCode)
                .HasColumnName("tax_country_code")
                .HasMaxLength(2)
                .IsRequired();

            tax.Property(t => t.Value)
                .HasColumnName("tax_number")
                .HasMaxLength(TaxNumber.MaxLength)
                .IsRequired();

            tax.Property(t => t.IsVerified)
                .HasColumnName("tax_number_verified")
                .IsRequired();

            // Catching the same company entered twice is the whole point of this index.
            tax.HasIndex(t => new { t.CountryCode, t.Value })
                .HasDatabaseName("ix_partners_tax_number");
        });

        builder.Navigation(partner => partner.TaxNumber).IsRequired();
    }

    /// <summary>
    /// Customer terms live in their own table rather than sharing the partners row.
    /// <para>
    /// They are optional - a supplier-only partner has none - and they contain nothing but other
    /// owned types. Shared into the partners table, EF cannot tell "this partner has no customer
    /// terms" from "this partner has terms whose every column happens to be null", and refuses to
    /// build the model. A separate table makes the presence of a row the answer, which is also
    /// the truer statement: terms are satellite data about a relationship, not columns of it.
    /// </para>
    /// </summary>
    private static void ConfigureCustomerTerms(EntityTypeBuilder<Partner> builder)
    {
        builder.OwnsOne(partner => partner.CustomerTerms, terms =>
        {
            terms.ToTable("partner_customer_terms");
            terms.WithOwner().HasForeignKey("partner_id");

            terms.OwnsOne(t => t.CreditLimit, money =>
            {
                money.Property(m => m.Amount)
                    .HasColumnName("credit_limit")
                    .HasPrecision(18, 4)
                    .IsRequired();

                money.Property(m => m.Currency)
                    .HasColumnName("credit_currency")
                    .HasConversion(currency => currency.Code, code => Currency.FromCode(code))
                    .HasMaxLength(3)
                    .IsRequired();
            });

            terms.Navigation(t => t.CreditLimit).IsRequired();

            terms.OwnsOne(t => t.PaymentTerms, payment =>
            {
                payment.Property(p => p.DueInDays)
                    .HasColumnName("payment_due_in_days")
                    .IsRequired();

                payment.Property(p => p.Method)
                    .HasColumnName("payment_method")
                    .HasConversion<string>()
                    .HasMaxLength(20)
                    .IsRequired();

                payment.Property(p => p.EndOfMonth)
                    .HasColumnName("payment_end_of_month")
                    .IsRequired();
            });

            terms.Navigation(t => t.PaymentTerms).IsRequired();

            terms.Property(t => t.PriceListCode)
                .HasColumnName("price_list_code")
                .HasMaxLength(20);
        });
    }

    /// <summary>Supplier terms, in their own table for the same reason as customer terms.</summary>
    private static void ConfigureSupplierTerms(EntityTypeBuilder<Partner> builder)
    {
        builder.OwnsOne(partner => partner.SupplierTerms, terms =>
        {
            terms.ToTable("partner_supplier_terms");
            terms.WithOwner().HasForeignKey("partner_id");

            terms.OwnsOne(t => t.PaymentTerms, payment =>
            {
                payment.Property(p => p.DueInDays)
                    .HasColumnName("payment_due_in_days")
                    .IsRequired();

                payment.Property(p => p.Method)
                    .HasColumnName("payment_method")
                    .HasConversion<string>()
                    .HasMaxLength(20)
                    .IsRequired();

                payment.Property(p => p.EndOfMonth)
                    .HasColumnName("payment_end_of_month")
                    .IsRequired();
            });

            terms.Navigation(t => t.PaymentTerms).IsRequired();

            terms.Property(t => t.LeadTimeDays)
                .HasColumnName("lead_time_days")
                .IsRequired();

            // Optional, but it contains only scalars, so EF can tell an absent minimum from a
            // present one without help.
            terms.OwnsOne(t => t.MinimumOrderValue, money =>
            {
                money.Property(m => m.Amount)
                    .HasColumnName("minimum_order_value")
                    .HasPrecision(18, 4);

                money.Property(m => m.Currency)
                    .HasColumnName("minimum_order_currency")
                    .HasConversion(currency => currency.Code, code => Currency.FromCode(code))
                    .HasMaxLength(3);
            });

            terms.Property(t => t.OurAccountNumber)
                .HasColumnName("our_account_number")
                .HasMaxLength(60);
        });
    }

    private static void ConfigureAddresses(EntityTypeBuilder<Partner> builder)
    {
        builder.OwnsMany(partner => partner.Addresses, address =>
        {
            address.ToTable("partner_addresses");
            address.WithOwner().HasForeignKey("partner_id");
            address.Property<long>("id").ValueGeneratedOnAdd();
            address.HasKey("id");

            address.Property(a => a.Kind).HasConversion<string>().HasMaxLength(20).IsRequired();
            address.Property(a => a.Line1).HasMaxLength(200).IsRequired();
            address.Property(a => a.Line2).HasMaxLength(200);
            address.Property(a => a.Postcode).HasMaxLength(20).IsRequired();
            address.Property(a => a.City).HasMaxLength(120).IsRequired();
            address.Property(a => a.CountryCode).HasMaxLength(2).IsRequired();
            address.Property(a => a.Notes).HasMaxLength(500);

            address.HasIndex("partner_id", nameof(Address.Kind))
                .HasDatabaseName("ix_partner_addresses_partner_kind");
        });

        builder.Navigation(partner => partner.Addresses)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }

    private static void ConfigureContacts(EntityTypeBuilder<Partner> builder)
    {
        builder.OwnsMany(partner => partner.Contacts, contact =>
        {
            contact.ToTable("partner_contacts");
            contact.WithOwner().HasForeignKey("partner_id");
            contact.Property<long>("id").ValueGeneratedOnAdd();
            contact.HasKey("id");

            contact.Property(c => c.Name).HasMaxLength(160).IsRequired();
            contact.Property(c => c.Role).HasMaxLength(80);
            contact.Property(c => c.Email).HasMaxLength(200);
            contact.Property(c => c.Phone).HasMaxLength(40);
            contact.Property(c => c.IsPrimary).IsRequired();
        });

        builder.Navigation(partner => partner.Contacts)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
