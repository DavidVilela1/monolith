using AutoPartsErp.Modules.Inventory.Domain;
using AutoPartsErp.Modules.Inventory.Domain.Counting;
using AutoPartsErp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoPartsErp.Modules.Inventory.Infrastructure.Persistence.Configurations;

/// <summary>Maps the <see cref="StockCount"/> aggregate onto <c>inventory.stock_counts</c>.</summary>
public sealed class StockCountConfiguration : IEntityTypeConfiguration<StockCount>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<StockCount> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("stock_counts");

        builder.HasKey(count => count.Id);

        builder.Property(count => count.Id)
            .HasConversion(id => id.Value, value => new StockCountId(value))
            .ValueGeneratedNever();

        builder.Property(count => count.Version)
            .IsRowVersion()
            .HasColumnName("xmin")
            .HasColumnType("xid");

        builder.Property(count => count.TenantId).IsRequired();

        builder.Property(count => count.Number)
            .HasMaxLength(StockCount.MaxNumberLength)
            .IsRequired();

        builder.Property(count => count.WarehouseId)
            .HasConversion(id => id.Value, value => new WarehouseId(value))
            .HasColumnName("warehouse_id")
            .IsRequired();

        builder.Property(count => count.CountedOn).IsRequired();

        builder.Property(count => count.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(count => count.Notes).HasMaxLength(StockCount.MaxNotesLength);
        builder.Property(count => count.CancellationReason).HasMaxLength(StockCount.MaxNotesLength);

        builder.Property(count => count.SubmittedBy).HasMaxLength(120);
        builder.Property(count => count.PostedBy).HasMaxLength(120);
        builder.Property(count => count.SubmittedAtUtc);
        builder.Property(count => count.PostedAtUtc);

        builder.Property(count => count.CreatedAtUtc).IsRequired();
        builder.Property(count => count.CreatedBy).HasMaxLength(120).IsRequired();
        builder.Property(count => count.ModifiedBy).HasMaxLength(120);

        ConfigureLines(builder);

        builder.HasIndex(count => new { count.TenantId, count.Number })
            .IsUnique()
            .HasDatabaseName("ux_stock_counts_tenant_number");

        // The sheets somebody still has to do something about. Partial, because a warehouse
        // counted monthly for ten years has a hundred and twenty sheets and two of them are live.
        builder.HasIndex(count => new { count.TenantId, count.WarehouseId, count.CountedOn })
            .HasFilter("status IN ('Open', 'Submitted')")
            .HasDatabaseName("ix_stock_counts_tenant_live");
    }

    private static void ConfigureLines(EntityTypeBuilder<StockCount> builder)
    {
        builder.OwnsMany(count => count.Lines, line =>
        {
            line.ToTable("stock_count_lines");
            line.WithOwner().HasForeignKey("stock_count_id");

            line.HasKey(item => item.Id);

            line.Property(item => item.Id)
                .HasConversion(id => id.Value, value => new StockCountLineId(value))
                .HasColumnName("id")
                .ValueGeneratedNever();

            line.Property(item => item.LineNumber).IsRequired();

            line.Property(item => item.PartId)
                .HasConversion(id => id.Value, value => new PartRef(value))
                .HasColumnName("part_id")
                .IsRequired();

            line.Property(item => item.Sku)
                .HasMaxLength(StockCountLine.MaxSkuLength)
                .IsRequired();

            line.Property(item => item.Description)
                .HasMaxLength(StockCountLine.MaxDescriptionLength)
                .IsRequired();

            line.OwnsOne(item => item.SystemQuantity, quantity =>
            {
                quantity.Property(q => q.Value)
                    .HasColumnName("system_quantity")
                    .HasPrecision(18, 4)
                    .IsRequired();

                quantity.Property(q => q.Unit)
                    .HasColumnName("system_unit")
                    .HasConversion(unit => unit.Code, code => UnitOfMeasure.FromCode(code))
                    .HasMaxLength(8)
                    .IsRequired();
            });

            line.Navigation(item => item.SystemQuantity).IsRequired();

            // Nullable, and the nullability is the whole point: an empty shelf counts as zero,
            // a shelf nobody reached counts as nothing at all, and posting must be able to tell
            // them apart. Both columns null together or neither.
            line.OwnsOne(item => item.CountedQuantity, quantity =>
            {
                quantity.Property(q => q.Value)
                    .HasColumnName("counted_quantity")
                    .HasPrecision(18, 4);

                quantity.Property(q => q.Unit)
                    .HasColumnName("counted_unit")
                    .HasConversion(unit => unit.Code, code => UnitOfMeasure.FromCode(code))
                    .HasMaxLength(8);
            });

            line.Property(item => item.CountedBy).HasMaxLength(120);
            line.Property(item => item.CountedAtUtc);

            line.HasIndex(item => item.PartId)
                .HasDatabaseName("ix_stock_count_lines_part");
        });

        builder.Navigation(count => count.Lines)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
