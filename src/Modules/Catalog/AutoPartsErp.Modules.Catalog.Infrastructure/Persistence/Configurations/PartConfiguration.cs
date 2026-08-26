using AutoPartsErp.Modules.Catalog.Domain;
using AutoPartsErp.Modules.Catalog.Domain.Parts;
using AutoPartsErp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AutoPartsErp.Modules.Catalog.Infrastructure.Persistence.Configurations;

/// <summary>Maps the <see cref="Part"/> aggregate onto the <c>catalog.parts</c> table and its children.</summary>
public sealed class PartConfiguration : IEntityTypeConfiguration<Part>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Part> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("parts");

        builder.HasKey(part => part.Id);

        builder.Property(part => part.Id)
            .HasConversion(id => id.Value, value => new PartId(value))
            .ValueGeneratedNever();

        builder.Property(part => part.TenantId).IsRequired();

        // xmin is PostgreSQL's own row version. Using it costs nothing in storage and gives
        // optimistic concurrency for free: two people editing the same part cannot silently
        // overwrite one another.
        builder.Property(part => part.Version)
            .IsRowVersion()
            .HasColumnName("xmin")
            .HasColumnType("xid");

        // A single-column value object, so a converter maps it to one plain column. The comparer
        // is what lets EF detect a changed SKU: without it, change tracking would compare object
        // references and quietly miss the edit.
        builder.Property(part => part.Sku)
            .HasConversion(
                sku => sku.Value,
                value => Sku.FromStorage(value),
                new ValueComparer<Sku>(
                    (left, right) => left!.Value == right!.Value,
                    sku => sku.Value.GetHashCode(StringComparison.Ordinal),
                    sku => Sku.FromStorage(sku.Value)))
            .HasColumnName("sku")
            .HasMaxLength(Sku.MaxLength)
            .IsRequired();

        builder.OwnsOne(part => part.ManufacturerPartNumber, number =>
        {
            number.Property(n => n.Display)
                .HasColumnName("manufacturer_part_number")
                .HasMaxLength(PartNumber.MaxLength)
                .IsRequired();

            number.Property(n => n.Normalized)
                .HasColumnName("manufacturer_part_number_normalized")
                .HasMaxLength(PartNumber.MaxLength)
                .IsRequired();

            // The lookup a counter terminal performs hundreds of times a day.
            number.HasIndex(n => n.Normalized)
                .HasDatabaseName("ix_parts_mpn_normalized");
        });

        builder.Navigation(part => part.ManufacturerPartNumber).IsRequired();

        builder.Property(part => part.BrandId)
            .HasConversion(id => id.Value, value => new BrandId(value))
            .IsRequired();

        builder.Property(part => part.CategoryId)
            .HasConversion(id => id.Value, value => new CategoryId(value))
            .IsRequired();

        builder.Property(part => part.Name).HasMaxLength(200).IsRequired();
        builder.Property(part => part.Description).HasMaxLength(4000);

        builder.Property(part => part.StockUnit)
            .HasConversion(unit => unit.Code, code => UnitOfMeasure.FromCode(code))
            .HasMaxLength(8)
            .IsRequired();

        builder.Property(part => part.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(part => part.RequiresCoreReturn).IsRequired();

        // A non-nullable converter applied to a nullable property: EF handles the null case,
        // which keeps the lambdas total instead of dereferencing a missing value.
        builder.Property(part => part.SupersededByPartId)
            .HasConversion(new ValueConverter<PartId, Guid>(id => id.Value, value => new PartId(value)));

        builder.OwnsOne(part => part.Package, package =>
        {
            package.Property(p => p.WeightKg)
                .HasColumnName("weight_kg")
                .HasPrecision(12, 4)
                .IsRequired();

            package.Property(p => p.LengthMm).HasColumnName("length_mm").HasPrecision(12, 2);
            package.Property(p => p.WidthMm).HasColumnName("width_mm").HasPrecision(12, 2);
            package.Property(p => p.HeightMm).HasColumnName("height_mm").HasPrecision(12, 2);

            package.Property(p => p.IsDangerousGoods)
                .HasColumnName("is_dangerous_goods")
                .IsRequired();

            package.Property(p => p.UnNumber).HasColumnName("un_number").HasMaxLength(12);
        });

        builder.Navigation(part => part.Package).IsRequired();

        builder.OwnsOne(part => part.CoreCharge, money =>
        {
            money.Property(m => m.Amount)
                .HasColumnName("core_charge_amount")
                .HasPrecision(18, 4);

            money.Property(m => m.Currency)
                .HasColumnName("core_charge_currency")
                .HasConversion(currency => currency.Code, code => Currency.FromCode(code))
                .HasMaxLength(3);
        });

        ConfigureCrossReferences(builder);
        ConfigureFitments(builder);
        ConfigureAuditing(builder);
        ConfigureIndexes(builder);
    }

    private static void ConfigureCrossReferences(EntityTypeBuilder<Part> builder)
    {
        builder.OwnsMany(part => part.CrossReferences, reference =>
        {
            reference.ToTable("part_cross_references");
            reference.WithOwner().HasForeignKey("part_id");
            reference.Property<long>("id").ValueGeneratedOnAdd();
            reference.HasKey("id");

            reference.Property(r => r.Kind)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();

            reference.Property(r => r.SourceBrand).HasMaxLength(40);
            reference.Property(r => r.Number).HasMaxLength(PartNumber.MaxLength).IsRequired();

            reference.Property(r => r.NormalizedNumber)
                .HasMaxLength(PartNumber.MaxLength)
                .IsRequired();

            reference.Property(r => r.Notes).HasMaxLength(400);

            // "The customer read me this number off the old part" - the single most
            // frequent query in the whole system.
            reference.HasIndex(r => r.NormalizedNumber)
                .HasDatabaseName("ix_part_cross_references_normalized_number");

            reference.HasIndex("part_id", nameof(CrossReference.Kind))
                .HasDatabaseName("ix_part_cross_references_part_kind");
        });

        // Owned collections are always loaded with their owner, so there is no AutoInclude to set;
        // this just tells EF to go through the backing field rather than the read-only property.
        builder.Navigation(part => part.CrossReferences)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }

    private static void ConfigureFitments(EntityTypeBuilder<Part> builder)
    {
        builder.OwnsMany(part => part.Fitments, fitment =>
        {
            fitment.ToTable("part_fitments");
            fitment.WithOwner().HasForeignKey("part_id");
            fitment.Property<long>("id").ValueGeneratedOnAdd();
            fitment.HasKey("id");

            fitment.Property(f => f.Make).HasMaxLength(60).IsRequired();
            fitment.Property(f => f.Model).HasMaxLength(80).IsRequired();
            fitment.Property(f => f.EngineCode).HasMaxLength(40);
            fitment.Property(f => f.YearFrom).IsRequired();
            fitment.Property(f => f.YearTo).IsRequired();
            fitment.Property(f => f.Position).HasMaxLength(40);
            fitment.Property(f => f.Notes).HasMaxLength(400);

            // "What fits a 2014 Golf?" - make and model first, then the year range is a
            // cheap filter on the matching rows.
            fitment.HasIndex(f => new { f.Make, f.Model, f.YearFrom, f.YearTo })
                .HasDatabaseName("ix_part_fitments_vehicle");
        });

        builder.Navigation(part => part.Fitments)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }

    private static void ConfigureAuditing(EntityTypeBuilder<Part> builder)
    {
        builder.Property(part => part.CreatedAtUtc).IsRequired();
        builder.Property(part => part.CreatedBy).HasMaxLength(120).IsRequired();
        builder.Property(part => part.ModifiedBy).HasMaxLength(120);
        builder.Property(part => part.DeletedBy).HasMaxLength(120);
        builder.Property(part => part.IsDeleted).IsRequired();
    }

    private static void ConfigureIndexes(EntityTypeBuilder<Part> builder)
    {
        // A SKU is unique per tenant, not globally: two operating companies may run
        // independent numbering schemes.
        builder.HasIndex(part => new { part.TenantId, part.Sku })
            .IsUnique()
            .HasDatabaseName("ux_parts_tenant_sku");

        builder.HasIndex(part => new { part.TenantId, part.BrandId })
            .HasDatabaseName("ix_parts_tenant_brand");

        builder.HasIndex(part => new { part.TenantId, part.CategoryId })
            .HasDatabaseName("ix_parts_tenant_category");

        builder.HasIndex(part => part.Status).HasDatabaseName("ix_parts_status");
    }
}
