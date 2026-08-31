using AutoPartsErp.Modules.Inventory.Domain;
using AutoPartsErp.Modules.Inventory.Domain.Warehouses;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoPartsErp.Modules.Inventory.Infrastructure.Persistence.Configurations;

/// <summary>Maps the <see cref="Warehouse"/> aggregate onto <c>inventory.warehouses</c>.</summary>
public sealed class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Warehouse> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("warehouses");

        builder.HasKey(warehouse => warehouse.Id);

        builder.Property(warehouse => warehouse.Id)
            .HasConversion(id => id.Value, value => new WarehouseId(value))
            .ValueGeneratedNever();

        builder.Property(warehouse => warehouse.Version)
            .IsRowVersion()
            .HasColumnName("xmin")
            .HasColumnType("xid");

        builder.Property(warehouse => warehouse.TenantId).IsRequired();
        builder.Property(warehouse => warehouse.Code).HasMaxLength(Warehouse.MaxCodeLength).IsRequired();
        builder.Property(warehouse => warehouse.Name).HasMaxLength(Warehouse.MaxNameLength).IsRequired();

        builder.Property(warehouse => warehouse.Kind)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(warehouse => warehouse.IsActive).IsRequired();
        builder.Property(warehouse => warehouse.AllowsNegativeStock).IsRequired();
        builder.Property(warehouse => warehouse.RequiresBinTracking).IsRequired();

        builder.Property(warehouse => warehouse.CreatedAtUtc).IsRequired();
        builder.Property(warehouse => warehouse.CreatedBy).HasMaxLength(120).IsRequired();
        builder.Property(warehouse => warehouse.ModifiedBy).HasMaxLength(120);
        builder.Property(warehouse => warehouse.DeletedBy).HasMaxLength(120);
        builder.Property(warehouse => warehouse.IsDeleted).IsRequired();

        builder.HasIndex(warehouse => new { warehouse.TenantId, warehouse.Code })
            .IsUnique()
            .HasDatabaseName("ux_warehouses_tenant_code");
    }
}

/// <summary>Maps the <see cref="StorageBin"/> aggregate onto <c>inventory.storage_bins</c>.</summary>
public sealed class StorageBinConfiguration : IEntityTypeConfiguration<StorageBin>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<StorageBin> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("storage_bins");

        builder.HasKey(bin => bin.Id);

        builder.Property(bin => bin.Id)
            .HasConversion(id => id.Value, value => new BinId(value))
            .ValueGeneratedNever();

        builder.Property(bin => bin.Version)
            .IsRowVersion()
            .HasColumnName("xmin")
            .HasColumnType("xid");

        builder.Property(bin => bin.TenantId).IsRequired();

        builder.Property(bin => bin.WarehouseId)
            .HasConversion(id => id.Value, value => new WarehouseId(value))
            .IsRequired();

        builder.Property(bin => bin.Code).HasMaxLength(StorageBin.MaxCodeLength).IsRequired();

        builder.Property(bin => bin.Kind)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(bin => bin.PickSequence).IsRequired();
        builder.Property(bin => bin.IsActive).IsRequired();

        builder.Property(bin => bin.CreatedAtUtc).IsRequired();
        builder.Property(bin => bin.CreatedBy).HasMaxLength(120).IsRequired();
        builder.Property(bin => bin.ModifiedBy).HasMaxLength(120);
        builder.Property(bin => bin.DeletedBy).HasMaxLength(120);
        builder.Property(bin => bin.IsDeleted).IsRequired();

        builder.HasIndex(bin => new { bin.TenantId, bin.WarehouseId, bin.Code })
            .IsUnique()
            .HasDatabaseName("ux_storage_bins_warehouse_code");

        // Picking routes walk bins in sequence, so this is the order a pick list is built in.
        builder.HasIndex(bin => new { bin.WarehouseId, bin.PickSequence })
            .HasDatabaseName("ix_storage_bins_pick_route");
    }
}
