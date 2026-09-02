using AutoPartsErp.Modules.Sales.Domain;
using AutoPartsErp.Modules.Sales.Domain.Customers;
using AutoPartsErp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoPartsErp.Modules.Sales.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="CustomerAccount"/> onto <c>sales.customer_accounts</c>.
/// <para>
/// The key is the partner's own identifier rather than one Sales invented. One account per
/// partner, no lookup table between the modules, and a rebuilt projection lands on the same rows
/// it had before.
/// </para>
/// <para>
/// Not soft-deletable. A closed account is a status, and an account that vanished would take the
/// credit exposure figure with it.
/// </para>
/// </summary>
public sealed class CustomerAccountConfiguration : IEntityTypeConfiguration<CustomerAccount>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<CustomerAccount> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("customer_accounts");

        builder.HasKey(account => account.Id);

        builder.Property(account => account.Id)
            .HasConversion(id => id.Value, value => new CustomerRef(value))
            .HasColumnName("customer_id")
            .ValueGeneratedNever();

        builder.Property(account => account.Version)
            .IsRowVersion()
            .HasColumnName("xmin")
            .HasColumnType("xid");

        builder.Property(account => account.TenantId).IsRequired();

        builder.Property(account => account.Code)
            .HasMaxLength(CustomerAccount.MaxCodeLength)
            .IsRequired();

        builder.Property(account => account.LegalName)
            .HasMaxLength(CustomerAccount.MaxNameLength)
            .IsRequired();

        builder.OwnsOne(account => account.CreditLimit, money => MapMoney(money, "credit_limit"));
        builder.Navigation(account => account.CreditLimit).IsRequired();

        builder.OwnsOne(account => account.Committed, money => MapMoney(money, "committed"));
        builder.Navigation(account => account.Committed).IsRequired();

        builder.Property(account => account.PaymentDueInDays).IsRequired();
        builder.Property(account => account.PaymentEndOfMonth).IsRequired();

        builder.Property(account => account.PriceListCode)
            .HasMaxLength(CustomerAccount.MaxCodeLength);

        builder.Property(account => account.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(account => account.HoldReason)
            .HasMaxLength(CustomerAccount.MaxReasonLength);

        builder.Property(account => account.CreatedAtUtc).IsRequired();
        builder.Property(account => account.CreatedBy).HasMaxLength(120).IsRequired();
        builder.Property(account => account.ModifiedBy).HasMaxLength(120);

        // One code per tenant: the counter types a code, and two matches is not an answer.
        builder.HasIndex(account => new { account.TenantId, account.Code })
            .IsUnique()
            .HasDatabaseName("ux_customer_accounts_tenant_code");

        // The credit-control list: everyone on hold, and everyone close to their limit.
        builder.HasIndex(account => new { account.TenantId, account.Status })
            .HasDatabaseName("ix_customer_accounts_tenant_status");
    }

    private static void MapMoney(OwnedNavigationBuilder<CustomerAccount, Money> money, string columnPrefix)
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
