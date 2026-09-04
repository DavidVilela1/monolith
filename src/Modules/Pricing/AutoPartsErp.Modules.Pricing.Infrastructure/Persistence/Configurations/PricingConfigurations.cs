using AutoPartsErp.Modules.Pricing.Domain;
using AutoPartsErp.Modules.Pricing.Domain.Customers;
using AutoPartsErp.Modules.Pricing.Domain.PriceLists;
using AutoPartsErp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoPartsErp.Modules.Pricing.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="PriceList"/> onto <c>pricing.price_lists</c>.</summary>
public sealed class PriceListConfiguration : IEntityTypeConfiguration<PriceList>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PriceList> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("price_lists");

        builder.HasKey(list => list.Id);

        builder.Property(list => list.Id)
            .HasConversion(id => id.Value, value => new PriceListId(value))
            .ValueGeneratedNever();

        builder.Property(list => list.Version)
            .IsRowVersion()
            .HasColumnName("xmin")
            .HasColumnType("xid");

        builder.Property(list => list.TenantId).IsRequired();

        builder.Property(list => list.Code)
            .HasMaxLength(PriceList.MaxCodeLength)
            .IsRequired();

        builder.Property(list => list.Name)
            .HasMaxLength(PriceList.MaxNameLength)
            .IsRequired();

        builder.Property(list => list.CurrencyCode)
            .HasColumnName("currency_code")
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(list => list.Kind)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(list => list.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(list => list.EffectiveFrom).HasColumnName("effective_from");
        builder.Property(list => list.EffectiveTo).HasColumnName("effective_to");
        builder.Property(list => list.IsDefault).HasColumnName("is_default").IsRequired();

        builder.Property(list => list.CreatedAtUtc).IsRequired();
        builder.Property(list => list.CreatedBy).HasMaxLength(120).IsRequired();
        builder.Property(list => list.ModifiedBy).HasMaxLength(120);
        builder.Property(list => list.DeletedBy).HasMaxLength(120);

        builder.HasIndex(list => new { list.TenantId, list.Code })
            .IsUnique()
            .HasDatabaseName("ux_price_lists_tenant_code");

        // At most one default per tenant, enforced by the database.
        //
        // The command that moves the flag clears the old default in the same transaction, which is
        // enough while one request does it at a time. Two administrators promoting different lists
        // at once would both read one default and both write another; this partial index is what
        // stops the resolver being handed two lists that each claim to be the fallback.
        //
        // The filter is raw SQL and is correct only because the column is named is_default by the
        // snake_case convention. Renaming that property without renaming this string gives a
        // migration that builds and an index that silently never applies.
        builder.HasIndex(list => list.TenantId)
            .IsUnique()
            .HasFilter("is_default = true AND is_deleted = false")
            .HasDatabaseName("ux_price_lists_one_default_per_tenant");

        builder.HasIndex(list => new { list.TenantId, list.Status, list.Kind })
            .HasDatabaseName("ix_price_lists_tenant_status_kind");
    }
}

/// <summary>Maps <see cref="PriceListEntry"/> onto <c>pricing.price_list_entries</c>.</summary>
public sealed class PriceListEntryConfiguration : IEntityTypeConfiguration<PriceListEntry>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PriceListEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("price_list_entries");

        builder.HasKey(entry => entry.Id);

        builder.Property(entry => entry.Id)
            .HasConversion(id => id.Value, value => new PriceListEntryId(value))
            .ValueGeneratedNever();

        builder.Property(entry => entry.Version)
            .IsRowVersion()
            .HasColumnName("xmin")
            .HasColumnType("xid");

        builder.Property(entry => entry.TenantId).IsRequired();

        builder.Property(entry => entry.PriceListId)
            .HasConversion(id => id.Value, value => new PriceListId(value))
            .HasColumnName("price_list_id")
            .IsRequired();

        builder.Property(entry => entry.PartId)
            .HasConversion(part => part.Value, value => new PartRef(value))
            .HasColumnName("part_id")
            .IsRequired();

        builder.Property(entry => entry.CreatedAtUtc).IsRequired();
        builder.Property(entry => entry.CreatedBy).HasMaxLength(120).IsRequired();
        builder.Property(entry => entry.ModifiedBy).HasMaxLength(120);
        builder.Property(entry => entry.DeletedBy).HasMaxLength(120);

        // The breaks are an owned collection in their own table.
        //
        // Nothing here orders them, deliberately: EF materializes a collection in whatever order
        // the rows arrive, and there is no mapping-level ORDER BY to lean on. The aggregate is
        // written so that order does not matter - it sorts on the way out and takes the minimum
        // rather than the first. Adding an ordering here would hide that rather than help it.
        builder.OwnsMany(entry => entry.Breaks, quantityBreak =>
        {
            quantityBreak.ToTable("price_breaks");
            quantityBreak.WithOwner().HasForeignKey("price_list_entry_id");

            quantityBreak.Property<Guid>("id").ValueGeneratedOnAdd();
            quantityBreak.HasKey("id");

            quantityBreak.Property(item => item.MinimumQuantity)
                .HasColumnName("minimum_quantity")
                .HasPrecision(18, 4)
                .IsRequired();

            quantityBreak.OwnsOne(item => item.UnitPrice, price =>
            {
                price.Property(money => money.Amount)
                    .HasColumnName("unit_price")
                    .HasPrecision(18, 4)
                    .IsRequired();

                price.Property(money => money.Currency)
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

            quantityBreak.Navigation(item => item.UnitPrice).IsRequired();

            // Property names, not column names. "price_list_entry_id" is the shadow foreign key
            // declared above and really is called that; MinimumQuantity is a CLR property and has
            // to be named as one, however it ends up spelled in the database.
            quantityBreak.HasIndex("price_list_entry_id", nameof(PriceBreak.MinimumQuantity))
                .IsUnique()
                .HasDatabaseName("ux_price_breaks_entry_minimum");
        });

        // One price per part per list. The command checks first so a duplicate reads as a 409;
        // this is what holds under two people pricing the same part at the same moment.
        builder.HasIndex(entry => new { entry.TenantId, entry.PriceListId, entry.PartId })
            .IsUnique()
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ux_price_list_entries_list_part");

        // The index the resolver's candidate query runs on: every list that prices this part.
        builder.HasIndex(entry => new { entry.TenantId, entry.PartId })
            .HasDatabaseName("ix_price_list_entries_tenant_part");
    }
}

/// <summary>Maps <see cref="CustomerPricing"/> onto <c>pricing.customer_agreements</c>.</summary>
public sealed class CustomerPricingConfiguration : IEntityTypeConfiguration<CustomerPricing>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<CustomerPricing> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("customer_agreements");

        builder.HasKey(agreement => agreement.Id);

        builder.Property(agreement => agreement.Id)
            .HasConversion(id => id.Value, value => new CustomerPricingId(value))
            .ValueGeneratedNever();

        builder.Property(agreement => agreement.Version)
            .IsRowVersion()
            .HasColumnName("xmin")
            .HasColumnType("xid");

        builder.Property(agreement => agreement.TenantId).IsRequired();

        builder.Property(agreement => agreement.CustomerId)
            .HasConversion(customer => customer.Value, value => new CustomerRef(value))
            .HasColumnName("customer_id")
            .IsRequired();

        builder.Property(agreement => agreement.PriceListId)
            .HasConversion(id => id.Value, value => new PriceListId(value))
            .HasColumnName("price_list_id")
            .IsRequired();

        builder.Property(agreement => agreement.DiscountPercent)
            .HasColumnName("discount_percent")
            .HasPrecision(5, 2)
            .IsRequired();

        builder.Property(agreement => agreement.EffectiveFrom).HasColumnName("effective_from");
        builder.Property(agreement => agreement.EffectiveTo).HasColumnName("effective_to");

        builder.Property(agreement => agreement.Note)
            .HasMaxLength(CustomerPricing.MaxNoteLength);

        builder.Property(agreement => agreement.CreatedAtUtc).IsRequired();
        builder.Property(agreement => agreement.CreatedBy).HasMaxLength(120).IsRequired();
        builder.Property(agreement => agreement.ModifiedBy).HasMaxLength(120);
        builder.Property(agreement => agreement.DeletedBy).HasMaxLength(120);

        // One agreement per customer. Layering several would need a rule for which wins, and that
        // rule already exists as the price list's own precedence.
        builder.HasIndex(agreement => new { agreement.TenantId, agreement.CustomerId })
            .IsUnique()
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ux_customer_agreements_tenant_customer");

        // "Who does changing this list reach?" - asked before every price change.
        builder.HasIndex(agreement => new { agreement.TenantId, agreement.PriceListId })
            .HasDatabaseName("ix_customer_agreements_tenant_list");
    }
}
