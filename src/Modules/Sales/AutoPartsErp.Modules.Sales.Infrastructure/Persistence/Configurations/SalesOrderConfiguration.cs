using AutoPartsErp.Modules.Sales.Domain;
using AutoPartsErp.Modules.Sales.Domain.Orders;
using AutoPartsErp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoPartsErp.Modules.Sales.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the <see cref="SalesOrder"/> aggregate onto <c>sales.sales_orders</c> and its lines onto
/// <c>sales.sales_order_lines</c>.
/// <para>
/// None of the four money figures per line are stored. Extended price, discount, net and VAT are
/// all derived from the quantity, the unit price and two percentages, and storing the results as
/// well would create four columns that can disagree with the inputs after one bad migration.
/// The arithmetic is fixed, tested and cheap; a stored total that is wrong is an invoice that is
/// wrong.
/// </para>
/// </summary>
public sealed class SalesOrderConfiguration : IEntityTypeConfiguration<SalesOrder>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SalesOrder> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("sales_orders");

        builder.HasKey(order => order.Id);

        builder.Property(order => order.Id)
            .HasConversion(id => id.Value, value => new SalesOrderId(value))
            .ValueGeneratedNever();

        builder.Property(order => order.Version)
            .IsRowVersion()
            .HasColumnName("xmin")
            .HasColumnType("xid");

        builder.Property(order => order.TenantId).IsRequired();

        builder.Property(order => order.OrderNumber)
            .HasMaxLength(SalesOrder.MaxOrderNumberLength)
            .IsRequired();

        builder.Property(order => order.Kind)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        // Plain Guid columns. No foreign key into partners.partners or inventory.warehouses -
        // and, notably, none into sales.customer_accounts either. An order references the
        // customer it was taken for even if the account record is later reshaped by a
        // projection rebuild.
        builder.Property(order => order.CustomerId)
            .HasConversion(id => id.Value, value => new CustomerRef(value))
            .HasColumnName("customer_id")
            .IsRequired();

        builder.Property(order => order.CustomerCode)
            .HasMaxLength(SalesOrder.MaxCustomerCodeLength)
            .IsRequired();

        builder.Property(order => order.CustomerName)
            .HasMaxLength(SalesOrder.MaxCustomerNameLength)
            .IsRequired();

        builder.Property(order => order.FromWarehouseId)
            .HasConversion(id => id.Value, value => new WarehouseRef(value))
            .HasColumnName("from_warehouse_id")
            .IsRequired();

        builder.Property(order => order.CurrencyCode).HasMaxLength(3).IsRequired();

        builder.Property(order => order.Status)
            .HasConversion<string>()
            .HasMaxLength(25)
            .IsRequired();

        builder.Property(order => order.ConfirmedOn);
        builder.Property(order => order.RequiredBy);

        builder.Property(order => order.CustomerReference)
            .HasMaxLength(SalesOrder.MaxCustomerReferenceLength);

        builder.Property(order => order.Notes).HasMaxLength(SalesOrder.MaxNotesLength);
        builder.Property(order => order.ClosureReason).HasMaxLength(SalesOrder.MaxNotesLength);

        builder.Property(order => order.CreatedAtUtc).IsRequired();
        builder.Property(order => order.CreatedBy).HasMaxLength(120).IsRequired();
        builder.Property(order => order.ModifiedBy).HasMaxLength(120);
        builder.Property(order => order.DeletedBy).HasMaxLength(120);

        ConfigureLines(builder);

        // One order number per tenant. Portuguese invoicing will eventually need this to be
        // gapless as well as unique, which a max-plus-one cannot promise - hence the warning on
        // the repository method rather than a quiet pretence that this index is enough.
        builder.HasIndex(order => new { order.TenantId, order.OrderNumber })
            .IsUnique()
            .HasDatabaseName("ux_sales_orders_tenant_number");

        // "What is still owed to this customer?" - asked at the counter, by name, constantly.
        builder.HasIndex(order => new { order.TenantId, order.CustomerId, order.Status })
            .HasDatabaseName("ix_sales_orders_tenant_customer_status");

        // The picking list, and the late list.
        builder.HasIndex(order => new { order.TenantId, order.Status, order.RequiredBy })
            .HasDatabaseName("ix_sales_orders_tenant_status_required");
    }

    private static void ConfigureLines(EntityTypeBuilder<SalesOrder> builder)
    {
        builder.OwnsMany(order => order.Lines, line =>
        {
            line.ToTable("sales_order_lines");
            line.WithOwner().HasForeignKey("sales_order_id");

            line.HasKey(l => l.Id);

            line.Property(l => l.Id)
                .HasConversion(id => id.Value, value => new SalesOrderLineId(value))
                .HasColumnName("id")
                .ValueGeneratedNever();

            line.Property(l => l.PartId)
                .HasConversion(part => part.Value, value => new PartRef(value))
                .HasColumnName("part_id")
                .IsRequired();

            line.Property(l => l.Sku).HasMaxLength(SalesOrderLine.MaxSkuLength).IsRequired();

            line.Property(l => l.Description)
                .HasMaxLength(SalesOrderLine.MaxDescriptionLength)
                .IsRequired();

            line.OwnsOne(l => l.Quantity, quantity => MapQuantity(quantity, "quantity"));
            line.Navigation(l => l.Quantity).IsRequired();

            line.OwnsOne(l => l.DispatchedQuantity, quantity => MapQuantity(quantity, "dispatched_quantity"));
            line.Navigation(l => l.DispatchedQuantity).IsRequired();

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

            // Percentages, not money. Four decimal places because a trade discount of 12.5%
            // is ordinary and 33.333% happens.
            line.Property(l => l.DiscountPercent).HasPrecision(9, 4).IsRequired();
            line.Property(l => l.VatRatePercent).HasPrecision(9, 4).IsRequired();

            line.Property(l => l.TenantId).IsRequired();
            line.Property(l => l.CreatedAtUtc).IsRequired();
            line.Property(l => l.CreatedBy).HasMaxLength(120).IsRequired();
            line.Property(l => l.ModifiedBy).HasMaxLength(120);

            line.HasIndex(l => new { l.TenantId, l.PartId })
                .HasDatabaseName("ix_sales_order_lines_tenant_part");
        });

        builder.Navigation(order => order.Lines)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }

    private static void MapQuantity(
        OwnedNavigationBuilder<SalesOrderLine, Quantity> quantity,
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
