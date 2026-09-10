using AutoPartsErp.Modules.Purchasing.Domain;
using AutoPartsErp.Modules.Purchasing.Domain.Agreements;
using AutoPartsErp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoPartsErp.Modules.Purchasing.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="SupplierAgreement"/> onto <c>purchasing.supplier_agreements</c>, with the
/// rebate steps in <c>purchasing.supplier_rappel_steps</c>.
/// <para>
/// The steps are an owned collection even though <see cref="RappelScale"/> is a value object,
/// because a value object with a variable number of parts has to go somewhere and a JSON column
/// would put the one thing in this table anybody wants to query beyond reach of a query.
/// </para>
/// </summary>
public sealed class SupplierAgreementConfiguration : IEntityTypeConfiguration<SupplierAgreement>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SupplierAgreement> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("supplier_agreements");

        builder.HasKey(agreement => agreement.Id);

        builder.Property(agreement => agreement.Id)
            .HasConversion(id => id.Value, value => new SupplierAgreementId(value))
            .ValueGeneratedNever();

        builder.Property(agreement => agreement.Version)
            .IsRowVersion()
            .HasColumnName("xmin")
            .HasColumnType("xid");

        builder.Property(agreement => agreement.TenantId).IsRequired();

        // A plain Guid, like everywhere else in this schema. Partners owns the supplier.
        builder.Property(agreement => agreement.SupplierId)
            .HasConversion(id => id.Value, value => new SupplierRef(value))
            .HasColumnName("supplier_id")
            .IsRequired();

        builder.Property(agreement => agreement.SupplierCode)
            .HasMaxLength(SupplierAgreement.MaxSupplierCodeLength)
            .IsRequired();

        builder.Property(agreement => agreement.CurrencyCode)
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(agreement => agreement.EffectiveFrom).IsRequired();
        builder.Property(agreement => agreement.EffectiveTo);

        builder.Property(agreement => agreement.RappelBasis)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(agreement => agreement.RappelPeriod)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(agreement => agreement.Note).HasMaxLength(SupplierAgreement.MaxNoteLength);

        builder.Property(agreement => agreement.CreatedAtUtc).IsRequired();
        builder.Property(agreement => agreement.CreatedBy).HasMaxLength(120).IsRequired();
        builder.Property(agreement => agreement.ModifiedBy).HasMaxLength(120);
        builder.Property(agreement => agreement.DeletedBy).HasMaxLength(120);

        ConfigureScale(builder);

        // At most one live agreement per supplier, enforced by the database rather than by the
        // handler alone. Two buyers opening one on the same afternoon is exactly the race the
        // aggregate cannot see, and "which of these two prices the delivery?" is a question with
        // no good answer.
        builder.HasIndex(agreement => new { agreement.TenantId, agreement.SupplierId })
            .IsUnique()
            .HasFilter("effective_to IS NULL AND is_deleted = false")
            .HasDatabaseName("ux_supplier_agreements_tenant_supplier_live");

        builder.HasIndex(agreement => new { agreement.TenantId, agreement.SupplierId, agreement.EffectiveFrom })
            .HasDatabaseName("ix_supplier_agreements_tenant_supplier_from");
    }

    private static void ConfigureScale(EntityTypeBuilder<SupplierAgreement> builder)
    {
        // The steps hang off the agreement itself, not off the scale. RappelScale has no scalar
        // properties of its own — it is a list and some arithmetic — and an owned type with
        // nothing but a nested collection is a shape EF has no column to anchor. The aggregate
        // rebuilds the scale from these rows, so there is still exactly one copy of the truth.
        builder.OwnsMany(agreement => agreement.RappelSteps, step =>
        {
            step.ToTable("supplier_rappel_steps");
            step.WithOwner().HasForeignKey("supplier_agreement_id");

            // No natural key: a step is a threshold and a rate, and the same pair could
            // legitimately appear on two suppliers' scales. EF generates the row identity.
            step.Property<int>("id").ValueGeneratedOnAdd();
            step.HasKey("id");

            step.OwnsOne(s => s.From, from =>
            {
                from.Property(m => m.Amount)
                    .HasColumnName("from_value")
                    .HasPrecision(18, 4)
                    .IsRequired();

                from.Property(m => m.Currency)
                    .HasColumnName("from_currency")
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

            step.Navigation(s => s.From).IsRequired();

            step.Property(s => s.Percent)
                .HasColumnName("percent")
                .HasPrecision(9, 4)
                .IsRequired();
        });

        builder.Navigation(agreement => agreement.RappelSteps)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        // Computed from the rows above every time it is asked for. Nothing to store.
        builder.Ignore(agreement => agreement.Scale);
    }
}

/// <summary>
/// Maps <see cref="SupplierPrice"/> onto <c>purchasing.supplier_prices</c>.
/// <para>
/// Its own table and its own root, not a collection on the agreement, because a supplier's
/// catalogue runs to tens of thousands of parts and correcting one price should not mean loading
/// all of them.
/// </para>
/// </summary>
public sealed class SupplierPriceConfiguration : IEntityTypeConfiguration<SupplierPrice>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SupplierPrice> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("supplier_prices");

        builder.HasKey(price => price.Id);

        builder.Property(price => price.Id)
            .HasConversion(id => id.Value, value => new SupplierPriceId(value))
            .ValueGeneratedNever();

        builder.Property(price => price.Version)
            .IsRowVersion()
            .HasColumnName("xmin")
            .HasColumnType("xid");

        builder.Property(price => price.TenantId).IsRequired();

        builder.Property(price => price.SupplierId)
            .HasConversion(id => id.Value, value => new SupplierRef(value))
            .HasColumnName("supplier_id")
            .IsRequired();

        builder.Property(price => price.PartId)
            .HasConversion(id => id.Value, value => new PartRef(value))
            .HasColumnName("part_id")
            .IsRequired();

        builder.OwnsOne(price => price.UnitPrice, money =>
        {
            money.Property(m => m.Amount)
                .HasColumnName("unit_price")
                .HasPrecision(18, 4)
                .IsRequired();

            money.Property(m => m.Currency)
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

        builder.Navigation(price => price.UnitPrice).IsRequired();

        builder.Property(price => price.EffectiveFrom).IsRequired();
        builder.Property(price => price.SupplierPartNumber).HasMaxLength(60);
        builder.Property(price => price.Note).HasMaxLength(500);

        builder.Property(price => price.CreatedAtUtc).IsRequired();
        builder.Property(price => price.CreatedBy).HasMaxLength(120).IsRequired();
        builder.Property(price => price.ModifiedBy).HasMaxLength(120);
        builder.Property(price => price.DeletedBy).HasMaxLength(120);

        // One price per supplier per part per day. A second row for the same day is somebody
        // recording the same rise twice, and it would leave the lookup picking whichever came
        // back first.
        builder.HasIndex(price => new
        {
            price.TenantId,
            price.SupplierId,
            price.PartId,
            price.EffectiveFrom,
        })
            .IsUnique()
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ux_supplier_prices_tenant_supplier_part_from");

        // The lookup every delivery makes: this supplier, this part, latest day not in the future.
        builder.HasIndex(price => new { price.TenantId, price.SupplierId, price.PartId })
            .HasDatabaseName("ix_supplier_prices_tenant_supplier_part");
    }
}
