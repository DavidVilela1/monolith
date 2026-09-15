using AutoPartsErp.Modules.Purchasing.Domain;
using AutoPartsErp.Modules.Purchasing.Domain.Agreements;
using AutoPartsErp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoPartsErp.Modules.Purchasing.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="RappelClaim"/> onto <c>purchasing.rappel_claims</c>.
/// <para>
/// Two money columns, and <c>Outstanding</c> is again the subtraction rather than a column. A
/// stored copy is a figure that will one day disagree with the two it comes from, and this one
/// would disagree in front of the supplier it is being asked of.
/// </para>
/// </summary>
public sealed class RappelClaimConfiguration : IEntityTypeConfiguration<RappelClaim>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RappelClaim> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("rappel_claims");

        builder.HasKey(claim => claim.Id);

        builder.Property(claim => claim.Id)
            .HasConversion(id => id.Value, value => new RappelClaimId(value))
            .ValueGeneratedNever();

        builder.Property(claim => claim.Version)
            .IsRowVersion()
            .HasColumnName("xmin")
            .HasColumnType("xid");

        builder.Property(claim => claim.TenantId).IsRequired();

        builder.Property(claim => claim.Number)
            .HasMaxLength(RappelClaim.MaxNumberLength)
            .IsRequired();

        builder.Property(claim => claim.AccrualId)
            .HasConversion(id => id.Value, value => new RappelAccrualId(value))
            .HasColumnName("accrual_id")
            .IsRequired();

        builder.Property(claim => claim.SupplierId)
            .HasConversion(id => id.Value, value => new SupplierRef(value))
            .HasColumnName("supplier_id")
            .IsRequired();

        builder.Property(claim => claim.SupplierCode).HasMaxLength(30).IsRequired();

        builder.Property(claim => claim.PeriodFrom).IsRequired();
        builder.Property(claim => claim.PeriodTo).IsRequired();
        builder.Property(claim => claim.CurrencyCode).HasMaxLength(3).IsRequired();

        builder.Property(claim => claim.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(claim => claim.SentOn);

        builder.Property(claim => claim.Note).HasMaxLength(RappelClaim.MaxNoteLength);

        builder.OwnsOne(claim => claim.Claimed, money => MapMoney(money, "claimed"));
        builder.Navigation(claim => claim.Claimed).IsRequired();

        builder.OwnsOne(claim => claim.Credited, money => MapMoney(money, "credited"));
        builder.Navigation(claim => claim.Credited).IsRequired();

        builder.Ignore(claim => claim.Outstanding);
        builder.Ignore(claim => claim.IsOpen);

        builder.Property(claim => claim.CreatedAtUtc).IsRequired();
        builder.Property(claim => claim.CreatedBy).HasMaxLength(120).IsRequired();
        builder.Property(claim => claim.ModifiedBy).HasMaxLength(120);

        // One claim per period. A second would have the company asking for the same money twice,
        // which is how a supplier stops taking the first one seriously.
        builder.HasIndex(claim => new { claim.TenantId, claim.AccrualId })
            .IsUnique()
            .HasDatabaseName("ux_rappel_claims_tenant_period");

        builder.HasIndex(claim => new { claim.TenantId, claim.Number })
            .IsUnique()
            .HasDatabaseName("ux_rappel_claims_tenant_number");

        // The buyer's list: what is still open, oldest period first.
        builder.HasIndex(claim => new { claim.TenantId, claim.Status, claim.PeriodTo })
            .HasDatabaseName("ix_rappel_claims_tenant_status_period");
    }

    /// <summary>
    /// The two money columns are mapped the same way, so the shape is written once. Takes the
    /// owned builder rather than a selector, matching the accrual's mapping next door.
    /// </summary>
    private static void MapMoney(
        OwnedNavigationBuilder<RappelClaim, Money> money,
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
