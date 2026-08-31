using AutoPartsErp.Modules.Inventory.Domain;
using AutoPartsErp.Modules.Inventory.Domain.Stock;
using AutoPartsErp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AutoPartsErp.Modules.Inventory.Infrastructure.Persistence.Configurations;

/// <summary>Maps the <see cref="StockItem"/> aggregate onto <c>inventory.stock_items</c>.</summary>
public sealed class StockItemConfiguration : IEntityTypeConfiguration<StockItem>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<StockItem> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("stock_items");

        builder.HasKey(item => item.Id);

        builder.Property(item => item.Id)
            .HasConversion(id => id.Value, value => new StockItemId(value))
            .ValueGeneratedNever();

        builder.Property(item => item.Version)
            .IsRowVersion()
            .HasColumnName("xmin")
            .HasColumnType("xid");

        builder.Property(item => item.TenantId).IsRequired();

        // A plain Guid column. No foreign key to catalog.parts: a database-level constraint
        // across module schemas would be exactly the coupling the boundary exists to avoid.
        builder.Property(item => item.Part)
            .HasConversion(part => part.Value, value => new PartRef(value))
            .HasColumnName("part_id")
            .IsRequired();

        builder.Property(item => item.WarehouseId)
            .HasConversion(id => id.Value, value => new WarehouseId(value))
            .IsRequired();

        builder.Property(item => item.Unit)
            .HasConversion(
                unit => unit.Code,
                code => UnitOfMeasure.FromCode(code),
                new ValueComparer<UnitOfMeasure>(
                    (left, right) => left!.Code == right!.Code,
                    unit => unit.Code.GetHashCode(StringComparison.Ordinal),
                    unit => UnitOfMeasure.FromCode(unit.Code)))
            .HasColumnName("unit")
            .HasMaxLength(8)
            .IsRequired();

        builder.OwnsOne(item => item.OnHand, quantity => MapQuantity(quantity, "on_hand"));
        builder.Navigation(item => item.OnHand).IsRequired();

        builder.OwnsOne(item => item.Reserved, quantity => MapQuantity(quantity, "reserved"));
        builder.Navigation(item => item.Reserved).IsRequired();

        builder.OwnsOne(item => item.OnOrder, quantity => MapQuantity(quantity, "on_order"));
        builder.Navigation(item => item.OnOrder).IsRequired();

        builder.OwnsOne(item => item.ReorderPoint, quantity => MapQuantity(quantity, "reorder_point"));
        builder.OwnsOne(item => item.ReorderQuantity, quantity => MapQuantity(quantity, "reorder_quantity"));

        builder.Property(item => item.DefaultBinId)
            .HasConversion(new ValueConverter<BinId, Guid>(id => id.Value, value => new BinId(value)));

        builder.Property(item => item.LastCountedAtUtc);

        builder.Property(item => item.CreatedAtUtc).IsRequired();
        builder.Property(item => item.CreatedBy).HasMaxLength(120).IsRequired();
        builder.Property(item => item.ModifiedBy).HasMaxLength(120);

        ConfigureReservations(builder);

        // One balance per part per warehouse, enforced by the database rather than by hope:
        // two concurrent PartActivated deliveries would otherwise open two rows and split one
        // part's stock across both.
        builder.HasIndex(item => new { item.TenantId, item.Part, item.WarehouseId })
            .IsUnique()
            .HasDatabaseName("ux_stock_items_tenant_part_warehouse");

        builder.HasIndex(item => new { item.TenantId, item.WarehouseId })
            .HasDatabaseName("ix_stock_items_tenant_warehouse");
    }

    private static void MapQuantity(OwnedNavigationBuilder<StockItem, Quantity> quantity, string columnPrefix)
    {
        quantity.Property(q => q.Value)
            .HasColumnName(columnPrefix)
            .HasPrecision(18, 4);

        // The unit is stored alongside each quantity as well as on the row. Redundant while a
        // part keeps one stocking unit forever, and it is what makes the column readable on its
        // own in a report or a hand-written query.
        quantity.Property(q => q.Unit)
            .HasColumnName($"{columnPrefix}_unit")
            .HasConversion(unit => unit.Code, code => UnitOfMeasure.FromCode(code))
            .HasMaxLength(8);
    }

    private static void ConfigureReservations(EntityTypeBuilder<StockItem> builder)
    {
        builder.OwnsMany(item => item.Reservations, reservation =>
        {
            reservation.ToTable("stock_reservations");
            reservation.WithOwner().HasForeignKey("stock_item_id");

            reservation.HasKey(r => r.Id);

            reservation.Property(r => r.Id)
                .HasConversion(id => id.Value, value => new ReservationId(value))
                .HasColumnName("id")
                .ValueGeneratedNever();

            reservation.OwnsOne(r => r.Quantity, quantity =>
            {
                quantity.Property(q => q.Value)
                    .HasColumnName("quantity")
                    .HasPrecision(18, 4)
                    .IsRequired();

                quantity.Property(q => q.Unit)
                    .HasColumnName("unit")
                    .HasConversion(unit => unit.Code, code => UnitOfMeasure.FromCode(code))
                    .HasMaxLength(8)
                    .IsRequired();
            });

            reservation.Navigation(r => r.Quantity).IsRequired();

            reservation.OwnsOne(r => r.Reference, reference =>
            {
                reference.Property(x => x.Type)
                    .HasColumnName("reference_type")
                    .HasConversion<string>()
                    .HasMaxLength(30)
                    .IsRequired();

                reference.Property(x => x.Number)
                    .HasColumnName("reference_number")
                    .HasMaxLength(MovementReference.MaxNumberLength)
                    .IsRequired();

                reference.Property(x => x.Note).HasColumnName("reference_note").HasMaxLength(400);
            });

            reservation.Navigation(r => r.Reference).IsRequired();

            reservation.Property(r => r.Status)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();

            reservation.Property(r => r.CreatedAtUtc).IsRequired();
            reservation.Property(r => r.ExpiresAtUtc);

            // The sweep that returns abandoned quote stock looks for exactly this pair.
            reservation.HasIndex(r => new { r.Status, r.ExpiresAtUtc })
                .HasDatabaseName("ix_stock_reservations_status_expiry");
        });

        builder.Navigation(item => item.Reservations)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
