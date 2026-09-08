using AutoPartsErp.Modules.Finance.Domain;
using AutoPartsErp.Modules.Finance.Domain.Customers;
using AutoPartsErp.Modules.Finance.Domain.Receipts;
using AutoPartsErp.Modules.Finance.Domain.Receivables;
using AutoPartsErp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoPartsErp.Modules.Finance.Infrastructure.Persistence.Configurations;

/// <summary>
/// The currency converter and its comparer, written once.
/// <para>
/// A <see cref="Currency"/> is a reference type, so EF needs telling how to compare two of them
/// and how to snapshot one. Without the comparer it compares by reference, decides every loaded
/// currency has changed, and writes the column back on every save.
/// </para>
/// </summary>
internal static class CurrencyMapping
{
    /// <summary>
    /// Applies the conversion to a currency column.
    /// <para>
    /// No type parameter for the owning entity, and there must not be one: it would appear in no
    /// argument, so nothing could infer it, and every call would fail with CS0411 — taking the
    /// surrounding <c>OwnsOne</c> overload resolution down with it and reporting the damage as a
    /// dozen entities that suddenly have no <c>Property</c> method.
    /// </para>
    /// </summary>
    /// <param name="builder">The property.</param>
    /// <param name="columnName">The column to store the code in.</param>
    public static PropertyBuilder<Currency> AsCurrency(
        this PropertyBuilder<Currency> builder,
        string columnName) =>
        builder
            .HasColumnName(columnName)
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

/// <summary>Maps <see cref="OpenItem"/> onto <c>finance.open_items</c>.</summary>
public sealed class OpenItemConfiguration : IEntityTypeConfiguration<OpenItem>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OpenItem> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("open_items");

        builder.HasKey(item => item.Id);

        builder.Property(item => item.Id)
            .HasConversion(id => id.Value, value => new OpenItemId(value))
            .ValueGeneratedNever();

        builder.Property(item => item.Version)
            .IsRowVersion()
            .HasColumnName("xmin")
            .HasColumnType("xid");

        builder.Property(item => item.TenantId).IsRequired();

        builder.Property(item => item.CustomerId)
            .HasConversion(customer => customer.Value, value => new CustomerRef(value))
            .HasColumnName("customer_id")
            .IsRequired();

        builder.Property(item => item.DocumentId)
            .HasConversion(document => document.Value, value => new DocumentRef(value))
            .HasColumnName("document_id")
            .IsRequired();

        builder.Property(item => item.DocumentNumber)
            .HasMaxLength(OpenItem.MaxDocumentNumberLength)
            .IsRequired();

        builder.Property(item => item.Kind)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(item => item.Status)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(item => item.DocumentDate).IsRequired();
        builder.Property(item => item.DueDate).IsRequired();

        builder.Property(item => item.CancellationReason)
            .HasMaxLength(OpenItem.MaxReasonLength);

        builder.OwnsOne(item => item.OriginalAmount, amount =>
        {
            amount.Property(value => value.Amount)
                .HasColumnName("original_amount")
                .HasPrecision(18, 4)
                .IsRequired();

            amount.Property(value => value.Currency).AsCurrency("currency");
        });

        builder.OwnsOne(item => item.SettledAmount, amount =>
        {
            amount.Property(value => value.Amount)
                .HasColumnName("settled_amount")
                .HasPrecision(18, 4)
                .IsRequired();

            // A second currency column, and it has to be: the owned type carries one and EF maps
            // what the type has. It is always equal to the first, which the aggregate guarantees
            // by refusing a settlement in another currency.
            amount.Property(value => value.Currency).AsCurrency("settled_currency");
        });

        builder.Navigation(item => item.OriginalAmount).IsRequired();
        builder.Navigation(item => item.SettledAmount).IsRequired();

        // One item per document, per tenant. The handler checks for an existing item before
        // raising one, and this is what makes that check a guarantee rather than a race: two
        // deliveries of the same message arriving together would otherwise both find nothing.
        builder.HasIndex(item => new { item.TenantId, item.DocumentId })
            .IsUnique()
            .HasDatabaseName("ux_open_items_tenant_document");

        // The index behind every statement, every ageing report and the allocation screen. Partial
        // on the two live statuses, so it stays the size of the ledger rather than the size of
        // every document the company has ever issued.
        builder.HasIndex(item => new { item.TenantId, item.CustomerId, item.DueDate })
            .HasFilter("status IN ('Open', 'PartiallySettled')")
            .HasDatabaseName("ix_open_items_tenant_outstanding");
    }
}

/// <summary>Maps <see cref="Receipt"/> onto <c>finance.receipts</c>, with its allocations.</summary>
public sealed class ReceiptConfiguration : IEntityTypeConfiguration<Receipt>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Receipt> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("receipts");

        builder.HasKey(receipt => receipt.Id);

        builder.Property(receipt => receipt.Id)
            .HasConversion(id => id.Value, value => new ReceiptId(value))
            .ValueGeneratedNever();

        builder.Property(receipt => receipt.Version)
            .IsRowVersion()
            .HasColumnName("xmin")
            .HasColumnType("xid");

        builder.Property(receipt => receipt.TenantId).IsRequired();

        builder.Property(receipt => receipt.Number)
            .HasMaxLength(Receipt.MaxNumberLength)
            .IsRequired();

        builder.Property(receipt => receipt.CustomerId)
            .HasConversion(customer => customer.Value, value => new CustomerRef(value))
            .HasColumnName("customer_id")
            .IsRequired();

        builder.Property(receipt => receipt.ReceivedOn).IsRequired();

        builder.Property(receipt => receipt.Method)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(receipt => receipt.Status)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(receipt => receipt.Reference).HasMaxLength(Receipt.MaxReferenceLength);
        builder.Property(receipt => receipt.Notes).HasMaxLength(Receipt.MaxNotesLength);

        builder.OwnsOne(receipt => receipt.Amount, amount =>
        {
            amount.Property(value => value.Amount)
                .HasColumnName("amount")
                .HasPrecision(18, 4)
                .IsRequired();

            amount.Property(value => value.Currency).AsCurrency("currency");
        });

        builder.OwnsOne(receipt => receipt.AllocatedAmount, amount =>
        {
            amount.Property(value => value.Amount)
                .HasColumnName("allocated_amount")
                .HasPrecision(18, 4)
                .IsRequired();

            amount.Property(value => value.Currency).AsCurrency("allocated_currency");
        });

        builder.Navigation(receipt => receipt.Amount).IsRequired();
        builder.Navigation(receipt => receipt.AllocatedAmount).IsRequired();

        builder.OwnsMany(receipt => receipt.Allocations, allocation =>
        {
            allocation.ToTable("receipt_allocations");
            allocation.WithOwner().HasForeignKey("receipt_id");

            allocation.HasKey(item => item.Id);

            allocation.Property(item => item.Id)
                .HasConversion(id => id.Value, value => new ReceiptAllocationId(value))
                .HasColumnName("id")
                .ValueGeneratedNever();

            allocation.Property(item => item.TenantId).IsRequired();

            allocation.Property(item => item.OpenItemId)
                .HasConversion(id => id.Value, value => new OpenItemId(value))
                .HasColumnName("open_item_id")
                .IsRequired();

            allocation.Property(item => item.DocumentNumber)
                .HasMaxLength(OpenItem.MaxDocumentNumberLength)
                .IsRequired();

            allocation.Property(item => item.AllocatedOn).IsRequired();

            allocation.OwnsOne(item => item.Amount, amount =>
            {
                amount.Property(value => value.Amount)
                    .HasColumnName("amount")
                    .HasPrecision(18, 4)
                    .IsRequired();

                amount.Property(value => value.Currency).AsCurrency("currency");
            });

            allocation.Navigation(item => item.Amount).IsRequired();

            // "What paid this document?" is asked as often as "what did this receipt pay", and
            // without this it is a scan of every allocation ever made.
            allocation.HasIndex(item => item.OpenItemId)
                .HasDatabaseName("ix_receipt_allocations_open_item");
        });

        builder.HasIndex(receipt => new { receipt.TenantId, receipt.Number })
            .IsUnique()
            .HasDatabaseName("ux_receipts_tenant_number");

        // Money that arrived and has not been matched yet is the working list of a person's day.
        // Partial, because it is a handful of rows against a table that only grows.
        builder.HasIndex(receipt => new { receipt.TenantId, receipt.CustomerId, receipt.ReceivedOn })
            .HasFilter("status IN ('Unallocated', 'PartiallyAllocated')")
            .HasDatabaseName("ix_receipts_tenant_unallocated");
    }
}

/// <summary>Maps <see cref="CustomerTerms"/> onto <c>finance.customer_terms</c>.</summary>
public sealed class CustomerTermsConfiguration : IEntityTypeConfiguration<CustomerTerms>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<CustomerTerms> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("customer_terms");

        // Keyed by the customer, not by an identifier of its own. There is exactly one set of
        // terms per customer, and a surrogate key would create the possibility of two.
        builder.HasKey(terms => terms.Id);

        builder.Property(terms => terms.Id)
            .HasConversion(id => id.Value, value => new CustomerRef(value))
            .HasColumnName("customer_id")
            .ValueGeneratedNever();

        builder.Property(terms => terms.Version)
            .IsRowVersion()
            .HasColumnName("xmin")
            .HasColumnType("xid");

        builder.Property(terms => terms.TenantId).IsRequired();

        builder.Property(terms => terms.Code)
            .HasMaxLength(CustomerTerms.MaxCodeLength)
            .IsRequired();

        builder.Property(terms => terms.LegalName)
            .HasMaxLength(CustomerTerms.MaxNameLength)
            .IsRequired();

        builder.Property(terms => terms.Currency).AsCurrency("currency");

        builder.Property(terms => terms.PaymentDueInDays).IsRequired();
        builder.Property(terms => terms.PaymentEndOfMonth).IsRequired();

        builder.HasIndex(terms => new { terms.TenantId, terms.Code })
            .HasDatabaseName("ix_customer_terms_tenant_code");
    }
}
