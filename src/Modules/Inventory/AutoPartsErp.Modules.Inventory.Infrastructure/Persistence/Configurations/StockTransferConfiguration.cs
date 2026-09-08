using AutoPartsErp.Modules.Inventory.Domain;
using AutoPartsErp.Modules.Inventory.Domain.Transfers;
using AutoPartsErp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoPartsErp.Modules.Inventory.Infrastructure.Persistence.Configurations;

/// <summary>Maps the <see cref="StockTransfer"/> aggregate onto <c>inventory.stock_transfers</c>.</summary>
public sealed class StockTransferConfiguration : IEntityTypeConfiguration<StockTransfer>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<StockTransfer> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("stock_transfers");

        builder.HasKey(transfer => transfer.Id);

        builder.Property(transfer => transfer.Id)
            .HasConversion(id => id.Value, value => new StockTransferId(value))
            .ValueGeneratedNever();

        builder.Property(transfer => transfer.Version)
            .IsRowVersion()
            .HasColumnName("xmin")
            .HasColumnType("xid");

        builder.Property(transfer => transfer.TenantId).IsRequired();

        builder.Property(transfer => transfer.Number)
            .HasMaxLength(StockTransfer.MaxNumberLength)
            .IsRequired();

        builder.Property(transfer => transfer.FromWarehouseId)
            .HasConversion(id => id.Value, value => new WarehouseId(value))
            .HasColumnName("from_warehouse_id")
            .IsRequired();

        builder.Property(transfer => transfer.ToWarehouseId)
            .HasConversion(id => id.Value, value => new WarehouseId(value))
            .HasColumnName("to_warehouse_id")
            .IsRequired();

        builder.Property(transfer => transfer.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(transfer => transfer.DispatchedAtUtc);
        builder.Property(transfer => transfer.DispatchedBy).HasMaxLength(120);
        builder.Property(transfer => transfer.CompletedAtUtc);

        builder.Property(transfer => transfer.Notes).HasMaxLength(StockTransfer.MaxNotesLength);
        builder.Property(transfer => transfer.ClosureReason).HasMaxLength(StockTransfer.MaxNotesLength);

        builder.Property(transfer => transfer.CreatedAtUtc).IsRequired();
        builder.Property(transfer => transfer.CreatedBy).HasMaxLength(120).IsRequired();
        builder.Property(transfer => transfer.ModifiedBy).HasMaxLength(120);

        ConfigureLines(builder);

        builder.HasIndex(transfer => new { transfer.TenantId, transfer.Number })
            .IsUnique()
            .HasDatabaseName("ux_stock_transfers_tenant_number");

        // "What is on a van right now?" - asked every morning, answered from this index rather
        // than from a scan of every transfer the company has ever made. Partial, because the live
        // ones are a handful against a table that only grows.
        builder.HasIndex(transfer => new { transfer.TenantId, transfer.ToWarehouseId, transfer.DispatchedAtUtc })
            .HasFilter("status IN ('InTransit', 'PartiallyReceived')")
            .HasDatabaseName("ix_stock_transfers_tenant_in_transit");

        // The sending end asks the same question the other way round: what have we sent that has
        // not been signed for.
        builder.HasIndex(transfer => new { transfer.TenantId, transfer.FromWarehouseId, transfer.Status })
            .HasDatabaseName("ix_stock_transfers_tenant_from_status");
    }

    private static void ConfigureLines(EntityTypeBuilder<StockTransfer> builder)
    {
        builder.OwnsMany(transfer => transfer.Lines, line =>
        {
            line.ToTable("stock_transfer_lines");
            line.WithOwner().HasForeignKey("stock_transfer_id");

            line.HasKey(item => item.Id);

            line.Property(item => item.Id)
                .HasConversion(id => id.Value, value => new StockTransferLineId(value))
                .HasColumnName("id")
                .ValueGeneratedNever();

            line.Property(item => item.LineNumber).IsRequired();

            line.Property(item => item.PartId)
                .HasConversion(id => id.Value, value => new PartRef(value))
                .HasColumnName("part_id")
                .IsRequired();

            line.Property(item => item.Sku)
                .HasMaxLength(StockTransferLine.MaxSkuLength)
                .IsRequired();

            line.Property(item => item.Description)
                .HasMaxLength(StockTransferLine.MaxDescriptionLength)
                .IsRequired();

            // Four quantities, written out rather than looped through a helper. A helper taking
            // the property as an expression needs a type parameter for it, and a type parameter
            // that appears in no argument cannot be inferred - CS0411, which then takes the
            // surrounding OwnsOne overload resolution down with it and reports the damage as a
            // dozen unrelated errors. Repetition is cheaper than that half hour.
            line.OwnsOne(item => item.Quantity, quantity =>
            {
                quantity.Property(q => q.Value)
                    .HasColumnName("quantity").HasPrecision(18, 4).IsRequired();

                quantity.Property(q => q.Unit)
                    .HasColumnName("quantity_unit")
                    .HasConversion(unit => unit.Code, code => UnitOfMeasure.FromCode(code))
                    .HasMaxLength(8).IsRequired();
            });

            line.Navigation(item => item.Quantity).IsRequired();

            line.OwnsOne(item => item.DispatchedQuantity, quantity =>
            {
                quantity.Property(q => q.Value)
                    .HasColumnName("dispatched_quantity").HasPrecision(18, 4).IsRequired();

                quantity.Property(q => q.Unit)
                    .HasColumnName("dispatched_unit")
                    .HasConversion(unit => unit.Code, code => UnitOfMeasure.FromCode(code))
                    .HasMaxLength(8).IsRequired();
            });

            line.Navigation(item => item.DispatchedQuantity).IsRequired();

            line.OwnsOne(item => item.ReceivedQuantity, quantity =>
            {
                quantity.Property(q => q.Value)
                    .HasColumnName("received_quantity").HasPrecision(18, 4).IsRequired();

                quantity.Property(q => q.Unit)
                    .HasColumnName("received_unit")
                    .HasConversion(unit => unit.Code, code => UnitOfMeasure.FromCode(code))
                    .HasMaxLength(8).IsRequired();
            });

            line.Navigation(item => item.ReceivedQuantity).IsRequired();

            line.OwnsOne(item => item.LostQuantity, quantity =>
            {
                quantity.Property(q => q.Value)
                    .HasColumnName("lost_quantity").HasPrecision(18, 4).IsRequired();

                quantity.Property(q => q.Unit)
                    .HasColumnName("lost_unit")
                    .HasConversion(unit => unit.Code, code => UnitOfMeasure.FromCode(code))
                    .HasMaxLength(8).IsRequired();
            });

            line.Navigation(item => item.LostQuantity).IsRequired();

            // Nullable on purpose. Null means the sending shelf never had a value of its own —
            // stock that has not been through a priced receipt — and that is a different fact
            // from goods worth nothing.
            line.OwnsOne(item => item.ValueInTransit, value =>
            {
                value.Property(m => m.Amount)
                    .HasColumnName("value_in_transit")
                    .HasPrecision(18, 4);

                value.Property(m => m.Currency)
                    .HasColumnName("value_currency")
                    .HasConversion(currency => currency.Code, code => Currency.FromCode(code))
                    .HasMaxLength(3);
            });

            line.HasIndex(item => item.PartId)
                .HasDatabaseName("ix_stock_transfer_lines_part");
        });

        builder.Navigation(transfer => transfer.Lines)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
