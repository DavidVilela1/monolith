using AutoPartsErp.Modules.Inventory.Domain;
using AutoPartsErp.Modules.Inventory.Domain.Stock;
using AutoPartsErp.Modules.Inventory.Domain.Warehouses;
using AutoPartsErp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AutoPartsErp.Modules.Inventory.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the <see cref="StockMovement"/> ledger onto <c>inventory.stock_movements</c>.
/// <para>
/// This is the table that grows without limit: every receipt, issue and count is a row, kept
/// forever. Its indexes are chosen for the two questions it actually gets asked — "show me this
/// part's history in this warehouse" and "what moved between these dates" — because retrofitting
/// an index onto ten million rows means a maintenance window.
/// </para>
/// </summary>
public sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("stock_movements");

        builder.HasKey(movement => movement.Id);

        builder.Property(movement => movement.Id)
            .HasConversion(id => id.Value, value => new MovementId(value))
            .ValueGeneratedNever();

        // No concurrency token: rows are inserted and never updated, so there is nothing to
        // collide over.
        builder.Ignore(movement => movement.Version);

        builder.Property(movement => movement.TenantId).IsRequired();

        builder.Property(movement => movement.Part)
            .HasConversion(part => part.Value, value => new PartRef(value))
            .HasColumnName("part_id")
            .IsRequired();

        builder.Property(movement => movement.WarehouseId)
            .HasConversion(id => id.Value, value => new WarehouseId(value))
            .IsRequired();

        builder.Property(movement => movement.Type)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.OwnsOne(movement => movement.Quantity, quantity =>
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

        builder.Navigation(movement => movement.Quantity).IsRequired();

        builder.OwnsOne(movement => movement.BalanceAfter, balance =>
        {
            balance.Property(q => q.Value)
                .HasColumnName("balance_after")
                .HasPrecision(18, 4)
                .IsRequired();

            balance.Property(q => q.Unit)
                .HasColumnName("balance_after_unit")
                .HasConversion(unit => unit.Code, code => UnitOfMeasure.FromCode(code))
                .HasMaxLength(8)
                .IsRequired();
        });

        builder.Navigation(movement => movement.BalanceAfter).IsRequired();

        builder.OwnsOne(movement => movement.Reference, reference =>
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

            // "Which movements came from GRN-2026-00042?" - the reconciliation question.
            reference.HasIndex(x => x.Number).HasDatabaseName("ix_stock_movements_reference_number");
        });

        builder.Navigation(movement => movement.Reference).IsRequired();

        builder.OwnsOne(movement => movement.UnitCost, cost =>
        {
            cost.Property(m => m.Amount).HasColumnName("unit_cost").HasPrecision(18, 4);

            cost.Property(m => m.Currency)
                .HasColumnName("unit_cost_currency")
                .HasConversion(currency => currency.Code, code => Currency.FromCode(code))
                .HasMaxLength(3);
        });

        builder.Property(movement => movement.BinId)
            .HasConversion(new ValueConverter<BinId, Guid>(id => id.Value, value => new BinId(value)));

        builder.Property(movement => movement.OccurredAtUtc).IsRequired();
        builder.Property(movement => movement.CreatedAtUtc).IsRequired();
        builder.Property(movement => movement.CreatedBy).HasMaxLength(120).IsRequired();
        builder.Property(movement => movement.ModifiedBy).HasMaxLength(120);

        // The ledger view for one part in one place, newest first.
        builder.HasIndex(movement => new
        {
            movement.TenantId,
            movement.Part,
            movement.WarehouseId,
            movement.OccurredAtUtc,
        })
            .HasDatabaseName("ix_stock_movements_part_warehouse_date");

        // Period reporting and stock valuation at a date.
        builder.HasIndex(movement => new { movement.TenantId, movement.OccurredAtUtc })
            .HasDatabaseName("ix_stock_movements_tenant_date");
    }
}
