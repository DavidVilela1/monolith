using AutoPartsErp.Modules.Finance.Domain;
using AutoPartsErp.Modules.Finance.Domain.Customers;
using AutoPartsErp.Modules.Finance.Domain.Ledger;
using AutoPartsErp.Modules.Finance.Domain.Payables;
using AutoPartsErp.Modules.Finance.Domain.Payments;
using AutoPartsErp.Modules.Finance.Domain.Receipts;
using AutoPartsErp.Modules.Finance.Domain.Receivables;
using AutoPartsErp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

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

/// <summary>
/// Maps <see cref="PayableItem"/> onto <c>finance.payable_items</c>.
/// <para>
/// Its own table, not a side flag on <c>open_items</c>. A sales ledger and a purchase ledger
/// sharing one table would, one afternoon, meet a query that forgot the filter and net what a
/// customer owes against what the company owes a supplier.
/// </para>
/// </summary>
public sealed class PayableItemConfiguration : IEntityTypeConfiguration<PayableItem>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PayableItem> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("payable_items");

        builder.HasKey(item => item.Id);

        builder.Property(item => item.Id)
            .HasConversion(id => id.Value, value => new PayableItemId(value))
            .ValueGeneratedNever();

        builder.Property(item => item.Version)
            .IsRowVersion()
            .HasColumnName("xmin")
            .HasColumnType("xid");

        builder.Property(item => item.TenantId).IsRequired();

        builder.Property(item => item.SupplierId)
            .HasConversion(supplier => supplier.Value, value => new SupplierRef(value))
            .HasColumnName("supplier_id")
            .IsRequired();

        builder.Property(item => item.SupplierCode).HasMaxLength(30).IsRequired();

        builder.Property(item => item.SupplierInvoiceId)
            .HasConversion(document => document.Value, value => new SupplierInvoiceRef(value))
            .HasColumnName("supplier_invoice_id")
            .IsRequired();

        // No unique index on the supplier's number: it is their sequence, and they restart series
        // and resend copies of documents already settled.
        builder.Property(item => item.DocumentNumber)
            .HasMaxLength(PayableItem.MaxDocumentNumberLength)
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
            .HasMaxLength(PayableItem.MaxReasonLength);

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

            amount.Property(value => value.Currency).AsCurrency("settled_currency");
        });

        builder.Navigation(item => item.OriginalAmount).IsRequired();
        builder.Navigation(item => item.SettledAmount).IsRequired();

        // One item per supplier document. The handler checks before raising one; this is what
        // makes that check a guarantee rather than a race, and a race here pays a supplier twice.
        builder.HasIndex(item => new { item.TenantId, item.SupplierInvoiceId })
            .IsUnique()
            .HasDatabaseName("ux_payable_items_tenant_document");

        // The payment run and the supplier statement, both. Partial on the two live statuses so it
        // stays the size of what is owed rather than of everything ever bought.
        builder.HasIndex(item => new { item.TenantId, item.DueDate })
            .HasFilter("status IN ('Open', 'PartiallySettled')")
            .HasDatabaseName("ix_payable_items_tenant_due");

        builder.HasIndex(item => new { item.TenantId, item.SupplierId, item.DueDate })
            .HasFilter("status IN ('Open', 'PartiallySettled')")
            .HasDatabaseName("ix_payable_items_tenant_supplier_outstanding");
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

/// <summary>
/// Maps <see cref="SupplierPayment"/> onto <c>finance.supplier_payments</c>, with its allocations.
/// </summary>
public sealed class SupplierPaymentConfiguration : IEntityTypeConfiguration<SupplierPayment>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SupplierPayment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("supplier_payments");

        builder.HasKey(payment => payment.Id);

        builder.Property(payment => payment.Id)
            .HasConversion(id => id.Value, value => new SupplierPaymentId(value))
            .ValueGeneratedNever();

        builder.Property(payment => payment.Version)
            .IsRowVersion()
            .HasColumnName("xmin")
            .HasColumnType("xid");

        builder.Property(payment => payment.TenantId).IsRequired();

        builder.Property(payment => payment.Number)
            .HasMaxLength(SupplierPayment.MaxNumberLength)
            .IsRequired();

        builder.Property(payment => payment.SupplierId)
            .HasConversion(supplier => supplier.Value, value => new SupplierRef(value))
            .HasColumnName("supplier_id")
            .IsRequired();

        builder.Property(payment => payment.SupplierCode).HasMaxLength(30).IsRequired();

        builder.Property(payment => payment.PaidOn).IsRequired();

        builder.Property(payment => payment.Method)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(payment => payment.Status)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(payment => payment.Reference)
            .HasMaxLength(SupplierPayment.MaxReferenceLength);

        builder.Property(payment => payment.Notes).HasMaxLength(SupplierPayment.MaxNotesLength);

        builder.OwnsOne(payment => payment.Amount, amount =>
        {
            amount.Property(value => value.Amount)
                .HasColumnName("amount")
                .HasPrecision(18, 4)
                .IsRequired();

            amount.Property(value => value.Currency).AsCurrency("currency");
        });

        builder.OwnsOne(payment => payment.AllocatedAmount, amount =>
        {
            amount.Property(value => value.Amount)
                .HasColumnName("allocated_amount")
                .HasPrecision(18, 4)
                .IsRequired();

            amount.Property(value => value.Currency).AsCurrency("allocated_currency");
        });

        builder.Navigation(payment => payment.Amount).IsRequired();
        builder.Navigation(payment => payment.AllocatedAmount).IsRequired();

        builder.OwnsMany(payment => payment.Allocations, allocation =>
        {
            allocation.ToTable("supplier_payment_allocations");
            allocation.WithOwner().HasForeignKey("supplier_payment_id");

            allocation.HasKey(item => item.Id);

            allocation.Property(item => item.Id)
                .HasConversion(id => id.Value, value => new SupplierPaymentAllocationId(value))
                .HasColumnName("id")
                .ValueGeneratedNever();

            allocation.Property(item => item.TenantId).IsRequired();

            allocation.Property(item => item.PayableItemId)
                .HasConversion(id => id.Value, value => new PayableItemId(value))
                .HasColumnName("payable_item_id")
                .IsRequired();

            allocation.Property(item => item.DocumentNumber)
                .HasMaxLength(PayableItem.MaxDocumentNumberLength)
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

            // "What paid this document?" is asked as often as "what did this payment pay", and
            // without this it is a scan of every allocation ever made.
            allocation.HasIndex(item => item.PayableItemId)
                .HasDatabaseName("ix_supplier_payment_allocations_payable_item");
        });

        builder.Navigation(payment => payment.Allocations)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(payment => new { payment.TenantId, payment.Number })
            .IsUnique()
            .HasDatabaseName("ux_supplier_payments_tenant_number");

        // Money that left the bank and is matched to nothing is what makes a statement disagree.
        // Partial, so the index is the size of the problem rather than of every payment ever made.
        builder.HasIndex(payment => new { payment.TenantId, payment.SupplierId, payment.PaidOn })
            .HasFilter("status IN ('Unallocated', 'PartiallyAllocated')")
            .HasDatabaseName("ix_supplier_payments_tenant_unallocated");
    }
}

/// <summary>Maps <see cref="Account"/> onto <c>finance.accounts</c>.</summary>
public sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("accounts");

        builder.HasKey(account => account.Id);

        builder.Property(account => account.Id)
            .HasConversion(id => id.Value, value => new AccountId(value))
            .ValueGeneratedNever();

        builder.Property(account => account.Version)
            .IsRowVersion()
            .HasColumnName("xmin")
            .HasColumnType("xid");

        builder.Property(account => account.TenantId).IsRequired();

        builder.Property(account => account.Code)
            .HasMaxLength(Account.MaxCodeLength)
            .IsRequired();

        builder.Property(account => account.Name)
            .HasMaxLength(Account.MaxNameLength)
            .IsRequired();

        builder.Property(account => account.Type)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(account => account.AllowsPosting).IsRequired();
        builder.Property(account => account.IsActive).IsRequired();

        builder.Property(account => account.ParentId)
            .HasConversion(new ValueConverter<AccountId, Guid>(
                id => id.Value, value => new AccountId(value)))
            .HasColumnName("parent_id");

        // The side an account grows on is a fact about its kind, computed every time it is asked
        // for. A stored copy could disagree with the type, and an asset growing on the credit side
        // makes every report built on it wrong in a direction nobody checks.
        builder.Ignore(account => account.NormalSide);
        builder.Ignore(account => account.CanTakePostings);

        builder.Property(account => account.CreatedAtUtc).IsRequired();
        builder.Property(account => account.CreatedBy).HasMaxLength(120).IsRequired();
        builder.Property(account => account.ModifiedBy).HasMaxLength(120);

        // Two accounts sharing a code is a trial balance with two rows nobody can tell apart.
        builder.HasIndex(account => new { account.TenantId, account.Code })
            .IsUnique()
            .HasDatabaseName("ux_accounts_tenant_code");
    }
}

/// <summary>
/// Maps <see cref="JournalEntry"/> onto <c>finance.journal_entries</c>, with its lines.
/// <para>
/// The lines are an owned collection: there is no line repository, EF always loads them with their
/// entry, and nothing can save a line without saving the entry whose balance depends on it. An
/// aggregate you can load half of is not an aggregate, and half a journal entry is an unbalanced
/// one.
/// </para>
/// </summary>
public sealed class JournalEntryConfiguration : IEntityTypeConfiguration<JournalEntry>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<JournalEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("journal_entries");

        builder.HasKey(entry => entry.Id);

        builder.Property(entry => entry.Id)
            .HasConversion(id => id.Value, value => new JournalEntryId(value))
            .ValueGeneratedNever();

        builder.Property(entry => entry.Version)
            .IsRowVersion()
            .HasColumnName("xmin")
            .HasColumnType("xid");

        builder.Property(entry => entry.TenantId).IsRequired();

        builder.Property(entry => entry.Number)
            .HasMaxLength(JournalEntry.MaxNumberLength)
            .IsRequired();

        builder.Property(entry => entry.EntryDate).IsRequired();

        builder.Property(entry => entry.Source)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(entry => entry.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(entry => entry.Description)
            .HasMaxLength(JournalEntry.MaxDescriptionLength)
            .IsRequired();

        builder.Property(entry => entry.Reference).HasMaxLength(JournalEntry.MaxReferenceLength);
        builder.Property(entry => entry.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(entry => entry.PostedAtUtc);

        builder.Property(entry => entry.ReversesId)
            .HasConversion(new ValueConverter<JournalEntryId, Guid>(
                id => id.Value, value => new JournalEntryId(value)))
            .HasColumnName("reverses_id");

        // Every total is the sum of the lines. Storing one would create a second copy of a figure
        // that has to agree with the first forever, and this is the one figure in the system where
        // disagreement means the ledger has stopped proving anything.
        builder.Ignore(entry => entry.TotalDebits);
        builder.Ignore(entry => entry.TotalCredits);
        builder.Ignore(entry => entry.IsBalanced);

        builder.Property(entry => entry.CreatedAtUtc).IsRequired();
        builder.Property(entry => entry.CreatedBy).HasMaxLength(120).IsRequired();
        builder.Property(entry => entry.ModifiedBy).HasMaxLength(120);

        builder.OwnsMany(entry => entry.Lines, line =>
        {
            line.ToTable("journal_lines");
            line.WithOwner().HasForeignKey("journal_entry_id");

            line.HasKey(item => item.Id);

            line.Property(item => item.Id)
                .HasConversion(id => id.Value, value => new JournalLineId(value))
                .HasColumnName("id")
                .ValueGeneratedNever();

            line.Property(item => item.TenantId).IsRequired();

            line.Property(item => item.AccountId)
                .HasConversion(id => id.Value, value => new AccountId(value))
                .HasColumnName("account_id")
                .IsRequired();

            // Copied, not joined. A trial balance printed in March has to keep reading the same
            // way in December, and an account renamed in between would restate every report that
            // ever showed it.
            line.Property(item => item.AccountCode)
                .HasMaxLength(Account.MaxCodeLength)
                .IsRequired();

            line.Property(item => item.Side)
                .HasConversion<string>()
                .HasMaxLength(10)
                .IsRequired();

            line.Property(item => item.Narrative)
                .HasMaxLength(JournalEntry.MaxDescriptionLength);

            line.OwnsOne(item => item.Amount, amount =>
            {
                amount.Property(value => value.Amount)
                    .HasColumnName("amount")
                    .HasPrecision(18, 4)
                    .IsRequired();

                amount.Property(value => value.Currency).AsCurrency("currency");
            });

            line.Navigation(item => item.Amount).IsRequired();

            line.Ignore(item => item.SignedAmount);

            // The trial balance: every line on an account, in date order. Without it, a balance
            // for one account is a scan of the whole ledger.
            line.HasIndex(item => new { item.TenantId, item.AccountId })
                .HasDatabaseName("ix_journal_lines_tenant_account");
        });

        builder.Navigation(entry => entry.Lines)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(entry => new { entry.TenantId, entry.Number })
            .IsUnique()
            .HasDatabaseName("ux_journal_entries_tenant_number");

        // Everything a period asks: what is posted, between these two days. Partial on posted,
        // because a draft is not in the ledger yet and no report should be able to find one.
        builder.HasIndex(entry => new { entry.TenantId, entry.EntryDate })
            .HasFilter("status = 'Posted'")
            .HasDatabaseName("ix_journal_entries_tenant_posted_date");
    }
}

/// <summary>Maps <see cref="AccountingPeriod"/> onto <c>finance.accounting_periods</c>.</summary>
public sealed class AccountingPeriodConfiguration : IEntityTypeConfiguration<AccountingPeriod>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<AccountingPeriod> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("accounting_periods");

        builder.HasKey(period => period.Id);

        builder.Property(period => period.Id)
            .HasConversion(id => id.Value, value => new AccountingPeriodId(value))
            .ValueGeneratedNever();

        builder.Property(period => period.Version)
            .IsRowVersion()
            .HasColumnName("xmin")
            .HasColumnType("xid");

        builder.Property(period => period.TenantId).IsRequired();
        builder.Property(period => period.Year).IsRequired();
        builder.Property(period => period.Month).IsRequired();

        builder.Property(period => period.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(period => period.ClosedAtUtc);

        builder.Property(period => period.ReopenedReason)
            .HasMaxLength(AccountingPeriod.MaxReasonLength);

        // The ordinal and the two dates are the year and month arranged differently. Storing them
        // would create three more copies of one fact, and one of them would eventually be stale.
        builder.Ignore(period => period.Ordinal);
        builder.Ignore(period => period.From);
        builder.Ignore(period => period.To);
        builder.Ignore(period => period.IsOpen);

        builder.Property(period => period.CreatedAtUtc).IsRequired();
        builder.Property(period => period.CreatedBy).HasMaxLength(120).IsRequired();
        builder.Property(period => period.ModifiedBy).HasMaxLength(120);

        // Two rows for one month would each have their own answer to whether it is closed.
        builder.HasIndex(period => new { period.TenantId, period.Year, period.Month })
            .IsUnique()
            .HasDatabaseName("ux_accounting_periods_tenant_month");
    }
}

/// <summary>Maps <see cref="PostingRule"/> onto <c>finance.posting_rules</c>.</summary>
public sealed class PostingRuleConfiguration : IEntityTypeConfiguration<PostingRule>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PostingRule> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("posting_rules");

        builder.HasKey(rule => rule.Id);

        builder.Property(rule => rule.Id)
            .HasConversion(id => id.Value, value => new PostingRuleId(value))
            .ValueGeneratedNever();

        builder.Property(rule => rule.Version)
            .IsRowVersion()
            .HasColumnName("xmin")
            .HasColumnType("xid");

        builder.Property(rule => rule.TenantId).IsRequired();

        // The fact key, as PostingFacts writes it. Text rather than an enum column, because the
        // catalogue grows every time a module learns to raise something and a stored enum would
        // need a migration to learn the same word.
        builder.Property(rule => rule.FactType).HasMaxLength(80).IsRequired();

        builder.Property(rule => rule.Description)
            .HasMaxLength(PostingRule.MaxDescriptionLength)
            .IsRequired();

        builder.Property(rule => rule.Source)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(rule => rule.IsActive).IsRequired();

        builder.Property(rule => rule.CreatedAtUtc).IsRequired();
        builder.Property(rule => rule.CreatedBy).HasMaxLength(120).IsRequired();
        builder.Property(rule => rule.ModifiedBy).HasMaxLength(120);

        builder.OwnsMany(rule => rule.Lines, line =>
        {
            line.ToTable("posting_rule_lines");
            line.WithOwner().HasForeignKey("posting_rule_id");

            line.HasKey(item => item.Id);

            line.Property(item => item.Id)
                .HasConversion(id => id.Value, value => new PostingRuleLineId(value))
                .HasColumnName("id")
                .ValueGeneratedNever();

            line.Property(item => item.TenantId).IsRequired();

            line.Property(item => item.AmountKey).HasMaxLength(40).IsRequired();

            line.Property(item => item.Side)
                .HasConversion<string>()
                .HasMaxLength(10)
                .IsRequired();

            line.Property(item => item.AccountCode)
                .HasMaxLength(Account.MaxCodeLength)
                .IsRequired();

            line.Property(item => item.Narrative).HasMaxLength(PostingRule.MaxNarrativeLength);
        });

        builder.Navigation(rule => rule.Lines)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        // One rule per fact. Two would each post their own version of it and the ledger would
        // carry the same sale twice.
        builder.HasIndex(rule => new { rule.TenantId, rule.FactType })
            .IsUnique()
            .HasDatabaseName("ux_posting_rules_tenant_fact");
    }
}

/// <summary>Maps <see cref="FactPosting"/> onto <c>finance.fact_postings</c>.</summary>
public sealed class FactPostingConfiguration : IEntityTypeConfiguration<FactPosting>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<FactPosting> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("fact_postings");

        builder.HasKey(posting => posting.Id);

        builder.Property(posting => posting.Id)
            .HasConversion(id => id.Value, value => new FactPostingId(value))
            .ValueGeneratedNever();

        builder.Property(posting => posting.Version)
            .IsRowVersion()
            .HasColumnName("xmin")
            .HasColumnType("xid");

        builder.Property(posting => posting.TenantId).IsRequired();

        builder.Property(posting => posting.FactType).HasMaxLength(80).IsRequired();

        builder.Property(posting => posting.Reference)
            .HasMaxLength(FactPosting.MaxReferenceLength)
            .IsRequired();

        builder.Property(posting => posting.OccurredOn).IsRequired();

        builder.Property(posting => posting.Description)
            .HasMaxLength(JournalEntry.MaxDescriptionLength)
            .IsRequired();

        builder.Property(posting => posting.CurrencyCode).HasMaxLength(3).IsRequired();

        builder.Property(posting => posting.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(posting => posting.JournalEntryId)
            .HasConversion(
                id => id!.Value.Value,
                value => new JournalEntryId(value));

        builder.Property(posting => posting.Reason).HasMaxLength(FactPosting.MaxReasonLength);

        builder.Property(posting => posting.PostedAtUtc);

        builder.Ignore(posting => posting.Currency);
        builder.Ignore(posting => posting.IsWaiting);

        builder.Property(posting => posting.CreatedAtUtc).IsRequired();
        builder.Property(posting => posting.CreatedBy).HasMaxLength(120).IsRequired();
        builder.Property(posting => posting.ModifiedBy).HasMaxLength(120);

        builder.OwnsMany(posting => posting.Amounts, amount =>
        {
            amount.ToTable("fact_posting_amounts");
            amount.WithOwner().HasForeignKey("fact_posting_id");

            amount.HasKey(item => item.Id);

            amount.Property(item => item.Id)
                .HasConversion(id => id.Value, value => new FactAmountId(value))
                .HasColumnName("id")
                .ValueGeneratedNever();

            amount.Property(item => item.TenantId).IsRequired();

            amount.Property(item => item.Key).HasMaxLength(40).IsRequired();

            amount.OwnsOne(item => item.Amount, money =>
            {
                money.Property(value => value.Amount)
                    .HasColumnName("amount")
                    .HasPrecision(18, 4)
                    .IsRequired();

                money.Property(value => value.Currency).AsCurrency("currency");
            });

            amount.Navigation(item => item.Amount).IsRequired();
        });

        builder.Navigation(posting => posting.Amounts)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        // One row per fact per document. This is the idempotence key: the outbox delivers at
        // least once, and without the constraint a redelivered invoice posts a second sale.
        builder.HasIndex(posting => new { posting.TenantId, posting.FactType, posting.Reference })
            .IsUnique()
            .HasDatabaseName("ux_fact_postings_tenant_fact_reference");

        // The waiting list, oldest first. Partial, because the rows that matter on a screen are
        // the few that are stuck and not the years of ones that posted.
        builder.HasIndex(posting => new { posting.TenantId, posting.OccurredOn })
            .HasFilter("status = 'Waiting'")
            .HasDatabaseName("ix_fact_postings_tenant_waiting");
    }
}
