using AutoPartsErp.Modules.Sales.Domain;
using AutoPartsErp.Modules.Sales.Domain.Returns;
using AutoPartsErp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AutoPartsErp.Modules.Sales.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the <see cref="CustomerReturn"/> aggregate onto <c>sales.customer_returns</c> and its
/// lines onto <c>sales.customer_return_lines</c>.
/// <para>
/// Shaped like the sales order it mirrors, and for the same reasons: the money on a line is
/// derived from the quantity, the unit price and two percentages rather than stored, and the
/// customer's code and name are snapshots rather than joins. What was credited has to keep
/// reading the same way in five years, whatever has happened to the account since.
/// </para>
/// </summary>
public sealed class CustomerReturnConfiguration : IEntityTypeConfiguration<CustomerReturn>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<CustomerReturn> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("customer_returns");

        builder.HasKey(customerReturn => customerReturn.Id);

        builder.Property(customerReturn => customerReturn.Id)
            .HasConversion(id => id.Value, value => new CustomerReturnId(value))
            .ValueGeneratedNever();

        builder.Property(customerReturn => customerReturn.Version)
            .IsRowVersion()
            .HasColumnName("xmin")
            .HasColumnType("xid");

        builder.Property(customerReturn => customerReturn.TenantId).IsRequired();

        builder.Property(customerReturn => customerReturn.Number)
            .HasMaxLength(CustomerReturn.MaxNumberLength)
            .IsRequired();

        // A plain Guid, like every other cross-aggregate reference in this module. No foreign key
        // into sales_orders: the return is a document in its own right, and an order archived or
        // reshaped later must not be able to take its returns with it.
        builder.Property(customerReturn => customerReturn.SalesOrderId)
            .HasConversion(id => id.Value, value => new SalesOrderId(value))
            .HasColumnName("sales_order_id")
            .IsRequired();

        builder.Property(customerReturn => customerReturn.OrderNumber)
            .HasMaxLength(CustomerReturn.MaxNumberLength)
            .IsRequired();

        builder.Property(customerReturn => customerReturn.CustomerId)
            .HasConversion(id => id.Value, value => new CustomerRef(value))
            .HasColumnName("customer_id")
            .IsRequired();

        builder.Property(customerReturn => customerReturn.CustomerCode)
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(customerReturn => customerReturn.CustomerName)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(customerReturn => customerReturn.ToWarehouseId)
            .HasConversion(id => id.Value, value => new WarehouseRef(value))
            .HasColumnName("to_warehouse_id")
            .IsRequired();

        builder.Property(customerReturn => customerReturn.CurrencyCode).HasMaxLength(3).IsRequired();

        builder.Property(customerReturn => customerReturn.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(customerReturn => customerReturn.Reason)
            .HasMaxLength(CustomerReturn.MaxNotesLength)
            .IsRequired();

        builder.Property(customerReturn => customerReturn.ReceivedOn).HasColumnName("received_on");

        builder.Property(customerReturn => customerReturn.ClosureReason)
            .HasMaxLength(CustomerReturn.MaxNotesLength);

        builder.Property(customerReturn => customerReturn.CreatedAtUtc).IsRequired();
        builder.Property(customerReturn => customerReturn.CreatedBy).HasMaxLength(120).IsRequired();
        builder.Property(customerReturn => customerReturn.ModifiedBy).HasMaxLength(120);

        ConfigureLines(builder);

        // One return number per tenant, on the same reasoning as the order number.
        builder.HasIndex(customerReturn => new { customerReturn.TenantId, customerReturn.Number })
            .IsUnique()
            .HasDatabaseName("ux_customer_returns_tenant_number");

        // "What has this customer sent back?" and "what is still waiting to arrive?" — the two
        // questions a counter asks about returns, both answered from here.
        builder.HasIndex(customerReturn => new
        {
            customerReturn.TenantId,
            customerReturn.CustomerId,
            customerReturn.Status,
        })
            .HasDatabaseName("ix_customer_returns_tenant_customer_status");

        // "What has come back against this order?" — asked when somebody is about to raise a
        // second return on the same order, which is exactly when it matters.
        builder.HasIndex(customerReturn => new { customerReturn.TenantId, customerReturn.SalesOrderId })
            .HasDatabaseName("ix_customer_returns_tenant_order");
    }

    private static void ConfigureLines(EntityTypeBuilder<CustomerReturn> builder)
    {
        builder.OwnsMany(customerReturn => customerReturn.Lines, line =>
        {
            line.ToTable("customer_return_lines");
            line.WithOwner().HasForeignKey("customer_return_id");

            line.HasKey(l => l.Id);

            line.Property(l => l.Id)
                .HasConversion(id => id.Value, value => new CustomerReturnLineId(value))
                .HasColumnName("id")
                .ValueGeneratedNever();

            line.Property(l => l.SalesOrderLineId)
                .HasConversion(id => id.Value, value => new SalesOrderLineId(value))
                .HasColumnName("sales_order_line_id")
                .IsRequired();

            line.Property(l => l.PartId)
                .HasConversion(part => part.Value, value => new PartRef(value))
                .HasColumnName("part_id")
                .IsRequired();

            line.Property(l => l.Sku).HasMaxLength(CustomerReturnLine.MaxSkuLength).IsRequired();

            line.Property(l => l.Description)
                .HasMaxLength(CustomerReturnLine.MaxDescriptionLength)
                .IsRequired();

            line.OwnsOne(l => l.Quantity, quantity => MapQuantity(quantity, "quantity"));
            line.Navigation(l => l.Quantity).IsRequired();

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

            line.Property(l => l.DiscountPercent).HasPrecision(9, 4).IsRequired();
            line.Property(l => l.VatRatePercent).HasPrecision(9, 4).IsRequired();

            // Stored as text, like every other enum in this system. The column is read by a
            // person looking at a returns report far more often than by code.
            line.Property(l => l.Disposition)
                .HasConversion<string>()
                .HasColumnName("disposition")
                .HasMaxLength(20)
                .IsRequired();

            line.Property(l => l.ConditionNote)
                .HasColumnName("condition_note")
                .HasMaxLength(CustomerReturnLine.MaxConditionNoteLength);

            line.Property(l => l.TenantId).IsRequired();
            line.Property(l => l.CreatedAtUtc).IsRequired();
            line.Property(l => l.CreatedBy).HasMaxLength(120).IsRequired();
            line.Property(l => l.ModifiedBy).HasMaxLength(120);

            // "How much of this order line has come back?" asked across every return there has
            // ever been on it, which is the check that stops a customer being credited twice.
            line.HasIndex(l => new { l.TenantId, l.SalesOrderLineId })
                .HasDatabaseName("ix_customer_return_lines_tenant_order_line");
        });

        builder.Navigation(customerReturn => customerReturn.Lines)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }

    private static void MapQuantity(
        OwnedNavigationBuilder<CustomerReturnLine, Quantity> quantity,
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
