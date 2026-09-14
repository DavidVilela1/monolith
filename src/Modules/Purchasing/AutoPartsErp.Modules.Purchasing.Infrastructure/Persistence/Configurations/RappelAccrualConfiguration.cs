using AutoPartsErp.Modules.Purchasing.Domain;
using AutoPartsErp.Modules.Purchasing.Domain.Agreements;
using AutoPartsErp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoPartsErp.Modules.Purchasing.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="RappelAccrual"/> onto <c>purchasing.rappel_accruals</c>.
/// <para>
/// Four money columns and nothing derived. <c>Outstanding</c> is the subtraction the whole feature
/// exists for and it is deliberately not stored: a figure kept beside the numbers it comes from is
/// a figure that will one day disagree with them, and this one would disagree in front of a
/// supplier.
/// </para>
/// </summary>
public sealed class RappelAccrualConfiguration : IEntityTypeConfiguration<RappelAccrual>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RappelAccrual> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("rappel_accruals");

        builder.HasKey(accrual => accrual.Id);

        builder.Property(accrual => accrual.Id)
            .HasConversion(id => id.Value, value => new RappelAccrualId(value))
            .ValueGeneratedNever();

        // Every settled document in a busy month writes to the same row. Optimistic concurrency is
        // what turns two of them arriving together into one retry instead of one lost purchase.
        builder.Property(accrual => accrual.Version)
            .IsRowVersion()
            .HasColumnName("xmin")
            .HasColumnType("xid");

        builder.Property(accrual => accrual.TenantId).IsRequired();

        builder.Property(accrual => accrual.SupplierId)
            .HasConversion(id => id.Value, value => new SupplierRef(value))
            .HasColumnName("supplier_id")
            .IsRequired();

        builder.Property(accrual => accrual.SupplierCode).HasMaxLength(30).IsRequired();

        builder.Property(accrual => accrual.Basis)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(accrual => accrual.PeriodFrom).IsRequired();
        builder.Property(accrual => accrual.PeriodTo).IsRequired();
        builder.Property(accrual => accrual.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(accrual => accrual.IsClosed).IsRequired();

        builder.OwnsOne(accrual => accrual.Purchased, money => MapMoney(money, "purchased"));
        builder.Navigation(accrual => accrual.Purchased).IsRequired();

        builder.OwnsOne(
            accrual => accrual.TakenOnInvoices, money => MapMoney(money, "taken_on_invoices"));
        builder.Navigation(accrual => accrual.TakenOnInvoices).IsRequired();

        builder.OwnsOne(accrual => accrual.Earned, money => MapMoney(money, "earned"));
        builder.Navigation(accrual => accrual.Earned).IsRequired();

        builder.OwnsOne(accrual => accrual.Credited, money => MapMoney(money, "credited"));
        builder.Navigation(accrual => accrual.Credited).IsRequired();

        builder.Ignore(accrual => accrual.Outstanding);
        builder.Ignore(accrual => accrual.IsOverclaimed);

        builder.Property(accrual => accrual.CreatedAtUtc).IsRequired();
        builder.Property(accrual => accrual.CreatedBy).HasMaxLength(120).IsRequired();
        builder.Property(accrual => accrual.ModifiedBy).HasMaxLength(120);

        // One period per supplier per stretch of days. Two settled documents arriving in the same
        // instant would otherwise each open their own, and the year would be counted twice at half
        // the rate — which is worse than not counting it at all, because it looks right.
        builder.HasIndex(accrual => new { accrual.TenantId, accrual.SupplierId, accrual.PeriodFrom })
            .IsUnique()
            .HasDatabaseName("ux_rappel_accruals_tenant_supplier_period");

        builder.HasIndex(accrual => new { accrual.TenantId, accrual.IsClosed })
            .HasDatabaseName("ix_rappel_accruals_tenant_open");
    }

    /// <summary>
    /// The four money columns are mapped the same way, so the shape is written once.
    /// <para>
    /// Takes the owned builder rather than an expression, which is the shape the purchase order's
    /// quantity mapping already uses next door — a helper that takes a selector has to infer two
    /// generic arguments and is the sort of thing that compiles differently than it reads.
    /// </para>
    /// </summary>
    private static void MapMoney(
        OwnedNavigationBuilder<RappelAccrual, Money> money,
        string columnPrefix)
    {
        money.Property(m => m.Amount)
            .HasColumnName(columnPrefix)
            .HasPrecision(18, 4)
            .IsRequired();

        money.Property(m => m.Currency)
            .HasColumnName($"{columnPrefix}_currency")
            .HasConversion(
                currency => currency.Code,
                code => Currency.FromCode(code),
                new ValueComparer<Currency>(
                    (left, right) => left!.Code == right!.Code,
                    currency => currency.Code.GetHashCode(StringComparison.Ordinal),
                    currency => Currency.FromCode(currency.Code)))
            .HasMaxLength(3)
            .IsRequired();
    }
}
