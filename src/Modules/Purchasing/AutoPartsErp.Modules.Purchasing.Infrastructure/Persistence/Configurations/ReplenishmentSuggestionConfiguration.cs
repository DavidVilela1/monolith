using AutoPartsErp.Modules.Purchasing.Domain;
using AutoPartsErp.Modules.Purchasing.Domain.Replenishment;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AutoPartsErp.Modules.Purchasing.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="ReplenishmentSuggestion"/> onto <c>purchasing.replenishment_suggestions</c>.
/// </summary>
public sealed class ReplenishmentSuggestionConfiguration
    : IEntityTypeConfiguration<ReplenishmentSuggestion>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ReplenishmentSuggestion> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("replenishment_suggestions");

        builder.HasKey(suggestion => suggestion.Id);

        builder.Property(suggestion => suggestion.Id)
            .HasConversion(id => id.Value, value => new SuggestionId(value))
            .ValueGeneratedNever();

        builder.Property(suggestion => suggestion.Version)
            .IsRowVersion()
            .HasColumnName("xmin")
            .HasColumnType("xid");

        builder.Property(suggestion => suggestion.TenantId).IsRequired();

        builder.Property(suggestion => suggestion.PartId)
            .HasConversion(part => part.Value, value => new PartRef(value))
            .HasColumnName("part_id")
            .IsRequired();

        builder.Property(suggestion => suggestion.WarehouseId)
            .HasConversion(id => id.Value, value => new WarehouseRef(value))
            .HasColumnName("warehouse_id")
            .IsRequired();

        builder.Property(suggestion => suggestion.QuantityAvailable).HasPrecision(18, 4).IsRequired();
        builder.Property(suggestion => suggestion.ReorderPoint).HasPrecision(18, 4).IsRequired();
        builder.Property(suggestion => suggestion.SuggestedQuantity).HasPrecision(18, 4).IsRequired();

        builder.Property(suggestion => suggestion.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(suggestion => suggestion.RaisedAtUtc).IsRequired();
        builder.Property(suggestion => suggestion.LastSeenAtUtc).IsRequired();

        builder.Property(suggestion => suggestion.PurchaseOrderId)
            .HasConversion(new ValueConverter<PurchaseOrderId, Guid>(
                id => id.Value, value => new PurchaseOrderId(value)))
            .HasColumnName("purchase_order_id");

        builder.Property(suggestion => suggestion.DismissedReason)
            .HasMaxLength(ReplenishmentSuggestion.MaxReasonLength);

        builder.Property(suggestion => suggestion.CreatedAtUtc).IsRequired();
        builder.Property(suggestion => suggestion.CreatedBy).HasMaxLength(120).IsRequired();
        builder.Property(suggestion => suggestion.ModifiedBy).HasMaxLength(120);

        // At most one open suggestion per part per warehouse, enforced by the database.
        //
        // The handler that consumes the reorder-point signal checks for an existing open row and
        // refreshes it, which is enough while events arrive one at a time. Two deliveries racing
        // would both find nothing and both insert; this partial index is what stops that becoming
        // a buyer's list with the same part on it twice.
        //
        // The loser of that race now recovers on its own: the insert fails, the handler throws,
        // the outbox row keeps its place and the sweep retries it - by which point the winning
        // row exists and the handler takes the refresh path instead.
        //
        // The filter is raw SQL, and it is correct only because the column is named 'status' by
        // the snake_case convention and the enum is stored by member name. Change either and
        // Postgres will accept the index and quietly stop enforcing anything.
        builder.HasIndex(suggestion => new
        {
            suggestion.TenantId,
            suggestion.PartId,
            suggestion.WarehouseId,
        })
            .IsUnique()
            .HasFilter("status = 'Open'")
            .HasDatabaseName("ux_replenishment_suggestions_open");

        // The buyer's list: everything open in one warehouse, worst shortfall first.
        builder.HasIndex(suggestion => new
        {
            suggestion.TenantId,
            suggestion.Status,
            suggestion.WarehouseId,
        })
            .HasDatabaseName("ix_replenishment_suggestions_status_warehouse");
    }
}
