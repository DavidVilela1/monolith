using AutoPartsErp.Modules.Purchasing.Domain;
using AutoPartsErp.Modules.Purchasing.Domain.Orders;
using AutoPartsErp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoPartsErp.Modules.Purchasing.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the <see cref="PurchaseOrder"/> aggregate onto <c>purchasing.purchase_orders</c> and its
/// lines onto <c>purchasing.purchase_order_lines</c>.
/// <para>
/// Lines are an owned collection rather than an independent entity, which is the mapping that
/// matches the domain: there is no line repository, EF always loads them with their order, and
/// nothing can save a line without saving the order whose status depends on it.
/// </para>
/// </summary>
public sealed class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PurchaseOrder> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("purchase_orders");

        builder.HasKey(order => order.Id);

        builder.Property(order => order.Id)
            .HasConversion(id => id.Value, value => new PurchaseOrderId(value))
            .ValueGeneratedNever();

        builder.Property(order => order.Version)
            .IsRowVersion()
            .HasColumnName("xmin")
            .HasColumnType("xid");

        builder.Property(order => order.TenantId).IsRequired();

        builder.Property(order => order.OrderNumber)
            .HasMaxLength(PurchaseOrder.MaxOrderNumberLength)
            .IsRequired();

        // Plain Guid columns. No foreign key into partners.partners or inventory.warehouses:
        // a database constraint across module schemas is exactly the coupling the boundary
        // exists to avoid.
        builder.Property(order => order.SupplierId)
            .HasConversion(id => id.Value, value => new SupplierRef(value))
            .HasColumnName("supplier_id")
            .IsRequired();

        builder.Property(order => order.SupplierCode)
            .HasMaxLength(PurchaseOrder.MaxSupplierCodeLength)
            .IsRequired();

        builder.Property(order => order.DeliverToWarehouseId)
            .HasConversion(id => id.Value, value => new WarehouseRef(value))
            .HasColumnName("deliver_to_warehouse_id")
            .IsRequired();

        builder.Property(order => order.CurrencyCode)
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(order => order.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(order => order.OrderedOn);
        builder.Property(order => order.ExpectedOn);

        builder.Property(order => order.SupplierReference)
            .HasMaxLength(PurchaseOrder.MaxSupplierReferenceLength);

        builder.Property(order => order.Notes).HasMaxLength(PurchaseOrder.MaxNotesLength);
        builder.Property(order => order.ClosureReason).HasMaxLength(PurchaseOrder.MaxNotesLength);

        builder.Property(order => order.CreatedAtUtc).IsRequired();
        builder.Property(order => order.CreatedBy).HasMaxLength(120).IsRequired();
        builder.Property(order => order.ModifiedBy).HasMaxLength(120);
        builder.Property(order => order.DeletedBy).HasMaxLength(120);

        ConfigureLines(builder);

        // One order number per tenant, enforced by the database rather than by hope. The
        // repository picks the next number with a max-plus-one, which will collide if two
        // buyers create an order in the same instant; this index is what turns that into a
        // loud failure instead of two documents with the same number.
        builder.HasIndex(order => new { order.TenantId, order.OrderNumber })
            .IsUnique()
            .HasDatabaseName("ux_purchase_orders_tenant_number");

        // "What is still to come from this supplier?" - the buyer's daily question.
        builder.HasIndex(order => new { order.TenantId, order.SupplierId, order.Status })
            .HasDatabaseName("ix_purchase_orders_tenant_supplier_status");

        // The chase list: orders past their promised date.
        builder.HasIndex(order => new { order.TenantId, order.ExpectedOn })
            .HasDatabaseName("ix_purchase_orders_tenant_expected");
    }

    private static void ConfigureLines(EntityTypeBuilder<PurchaseOrder> builder)
    {
        builder.OwnsMany(order => order.Lines, line =>
        {
            line.ToTable("purchase_order_lines");
            line.WithOwner().HasForeignKey("purchase_order_id");

            line.HasKey(l => l.Id);

            line.Property(l => l.Id)
                .HasConversion(id => id.Value, value => new PurchaseOrderLineId(value))
                .HasColumnName("id")
                .ValueGeneratedNever();

            line.Property(l => l.PartId)
                .HasConversion(part => part.Value, value => new PartRef(value))
                .HasColumnName("part_id")
                .IsRequired();

            line.Property(l => l.Sku)
                .HasMaxLength(PurchaseOrderLine.MaxSkuLength)
                .IsRequired();

            line.Property(l => l.Description)
                .HasMaxLength(PurchaseOrderLine.MaxDescriptionLength)
                .IsRequired();

            line.OwnsOne(l => l.Quantity, quantity => MapQuantity(quantity, "quantity"));
            line.Navigation(l => l.Quantity).IsRequired();

            line.OwnsOne(l => l.ReceivedQuantity, quantity => MapQuantity(quantity, "received_quantity"));
            line.Navigation(l => l.ReceivedQuantity).IsRequired();

            line.OwnsOne(l => l.UnitPrice, price =>
            {
                price.Property(m => m.Amount)
                    .HasColumnName("unit_price")
                    .HasPrecision(18, 4)
                    .IsRequired();

                price.Property(m => m.Currency)
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

            line.Navigation(l => l.UnitPrice).IsRequired();

            line.Property(l => l.TenantId).IsRequired();
            line.Property(l => l.CreatedAtUtc).IsRequired();
            line.Property(l => l.CreatedBy).HasMaxLength(120).IsRequired();
            line.Property(l => l.ModifiedBy).HasMaxLength(120);

            // Nothing queries lines directly today - they are an owned type with no DbSet, so
            // they are only ever reached through their order. This is for the question Sales
            // will ask first ("is any of this on order?"), and it is tenant-scoped because
            // every query in this codebase starts that way.
            line.HasIndex(l => new { l.TenantId, l.PartId })
                .HasDatabaseName("ix_purchase_order_lines_tenant_part");
        });

        builder.Navigation(order => order.Lines)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }

    private static void MapQuantity(
        OwnedNavigationBuilder<PurchaseOrderLine, Quantity> quantity,
        string columnPrefix)
    {
        quantity.Property(q => q.Value)
            .HasColumnName(columnPrefix)
            .HasPrecision(18, 4)
            .IsRequired();

        quantity.Property(q => q.Unit)
            .HasColumnName($"{columnPrefix}_unit")
            .HasConversion(
                unit => unit.Code,
                code => UnitOfMeasure.FromCode(code),
                new ValueComparer<UnitOfMeasure>(
                    (left, right) => left!.Code == right!.Code,
                    unit => unit.Code.GetHashCode(StringComparison.Ordinal),
                    unit => UnitOfMeasure.FromCode(unit.Code)))
            .HasMaxLength(8)
            .IsRequired();
    }
}
