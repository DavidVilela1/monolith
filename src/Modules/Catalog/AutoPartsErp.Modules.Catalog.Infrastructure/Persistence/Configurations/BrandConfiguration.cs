using AutoPartsErp.Modules.Catalog.Domain;
using AutoPartsErp.Modules.Catalog.Domain.Brands;
using AutoPartsErp.Modules.Catalog.Domain.Categories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AutoPartsErp.Modules.Catalog.Infrastructure.Persistence.Configurations;

/// <summary>Maps the <see cref="Brand"/> aggregate onto <c>catalog.brands</c>.</summary>
public sealed class BrandConfiguration : IEntityTypeConfiguration<Brand>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Brand> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("brands");

        builder.HasKey(brand => brand.Id);

        builder.Property(brand => brand.Id)
            .HasConversion(id => id.Value, value => new BrandId(value))
            .ValueGeneratedNever();

        builder.Property(brand => brand.Version)
            .IsRowVersion()
            .HasColumnName("xmin")
            .HasColumnType("xid");

        builder.Property(brand => brand.TenantId).IsRequired();
        builder.Property(brand => brand.Code).HasMaxLength(Brand.MaxCodeLength).IsRequired();
        builder.Property(brand => brand.Name).HasMaxLength(Brand.MaxNameLength).IsRequired();
        builder.Property(brand => brand.IsOriginalEquipment).IsRequired();
        builder.Property(brand => brand.IsActive).IsRequired();
        builder.Property(brand => brand.CountryCode).HasMaxLength(2);

        builder.Property(brand => brand.CreatedAtUtc).IsRequired();
        builder.Property(brand => brand.CreatedBy).HasMaxLength(120).IsRequired();
        builder.Property(brand => brand.ModifiedBy).HasMaxLength(120);
        builder.Property(brand => brand.DeletedBy).HasMaxLength(120);
        builder.Property(brand => brand.IsDeleted).IsRequired();

        builder.HasIndex(brand => new { brand.TenantId, brand.Code })
            .IsUnique()
            .HasDatabaseName("ux_brands_tenant_code");
    }
}

/// <summary>Maps the <see cref="PartCategory"/> aggregate onto <c>catalog.categories</c>.</summary>
public sealed class CategoryConfiguration : IEntityTypeConfiguration<PartCategory>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PartCategory> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("categories");

        builder.HasKey(category => category.Id);

        builder.Property(category => category.Id)
            .HasConversion(id => id.Value, value => new CategoryId(value))
            .ValueGeneratedNever();

        builder.Property(category => category.Version)
            .IsRowVersion()
            .HasColumnName("xmin")
            .HasColumnType("xid");

        builder.Property(category => category.TenantId).IsRequired();
        builder.Property(category => category.Code).HasMaxLength(PartCategory.MaxCodeLength).IsRequired();
        builder.Property(category => category.Name).HasMaxLength(PartCategory.MaxNameLength).IsRequired();
        builder.Property(category => category.SortOrder).IsRequired();
        builder.Property(category => category.IsActive).IsRequired();

        builder.Property(category => category.ParentId)
            .HasConversion(new ValueConverter<CategoryId, Guid>(
                id => id.Value, value => new CategoryId(value)));

        builder.Property(category => category.CreatedAtUtc).IsRequired();
        builder.Property(category => category.CreatedBy).HasMaxLength(120).IsRequired();
        builder.Property(category => category.ModifiedBy).HasMaxLength(120);
        builder.Property(category => category.DeletedBy).HasMaxLength(120);
        builder.Property(category => category.IsDeleted).IsRequired();

        builder.HasIndex(category => new { category.TenantId, category.Code })
            .IsUnique()
            .HasDatabaseName("ux_categories_tenant_code");

        builder.HasIndex(category => category.ParentId)
            .HasDatabaseName("ix_categories_parent");
    }
}
