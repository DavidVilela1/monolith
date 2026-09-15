using AutoPartsErp.Modules.Finance.Domain;
using AutoPartsErp.Modules.Finance.Domain.Customers;
using AutoPartsErp.Modules.Finance.Domain.Receipts;
using AutoPartsErp.Modules.Finance.Domain.Ledger;
using AutoPartsErp.Modules.Finance.Domain.Payables;
using AutoPartsErp.Modules.Finance.Domain.Payments;
using AutoPartsErp.Modules.Finance.Domain.Receivables;
using AutoPartsErp.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AutoPartsErp.Modules.Finance.Infrastructure.Persistence;

/// <summary>
/// The Finance module's database context, scoped to the <c>finance</c> schema.
/// <para>
/// It maps no Invoicing table. A document is a bare Guid here, and its number is a string copied
/// at the moment the item was raised — the same reason Invoicing snapshots a customer's name.
/// What the customer owes is a fact about the day the document was issued, and a join would let
/// something upstream quietly rewrite a statement that has already been sent.
/// </para>
/// </summary>
public sealed class FinanceDbContext : ModuleDbContext, IFinanceUnitOfWork
{
    /// <summary>The PostgreSQL schema this context owns.</summary>
    public const string SchemaName = "finance";

    /// <summary>Initializes the context.</summary>
    /// <param name="options">EF Core options, supplied by the container.</param>
    /// <param name="dependencies">
    /// Shared plumbing: the tenant, the domain event dispatcher and the outbox. Optional so the
    /// design-time tooling can build the model with no container behind it.
    /// </param>
    public FinanceDbContext(
        DbContextOptions<FinanceDbContext> options,
        ModuleDbContextDependencies? dependencies = null)
        : base(options, dependencies)
    {
    }

    /// <summary>The documents on customers' accounts.</summary>
    public DbSet<OpenItem> OpenItems => Set<OpenItem>();

    /// <summary>The purchase ledger: what the company owes its suppliers.</summary>
    public DbSet<PayableItem> PayableItems => Set<PayableItem>();

    /// <summary>Money that left, with what each payment settled.</summary>
    public DbSet<SupplierPayment> SupplierPayments => Set<SupplierPayment>();

    /// <summary>The chart of accounts.</summary>
    public DbSet<Account> Accounts => Set<Account>();

    /// <summary>The general ledger, with the lines of each entry.</summary>
    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();

    /// <summary>Money received, with what it paid.</summary>
    public DbSet<Receipt> Receipts => Set<Receipt>();

    /// <summary>What each customer's payment terms are.</summary>
    public DbSet<CustomerTerms> CustomerTerms => Set<CustomerTerms>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FinanceDbContext).Assembly);

        // No soft-delete filter on any of the three, deliberately. A sales ledger is a record of
        // money owed and money received, and hiding a row behind a query filter is the same thing
        // as losing it. A document that is no longer owed is Cancelled, and a cancelled item stays
        // in every list it was ever in.
        modelBuilder.Entity<OpenItem>()
            .HasQueryFilter(item => item.TenantId == CurrentTenantId);

        modelBuilder.Entity<PayableItem>()
            .HasQueryFilter(item => item.TenantId == CurrentTenantId);

        modelBuilder.Entity<SupplierPayment>()
            .HasQueryFilter(payment => payment.TenantId == CurrentTenantId);

        modelBuilder.Entity<Account>()
            .HasQueryFilter(account => account.TenantId == CurrentTenantId);

        modelBuilder.Entity<JournalEntry>()
            .HasQueryFilter(entry => entry.TenantId == CurrentTenantId);

        modelBuilder.Entity<Receipt>()
            .HasQueryFilter(receipt => receipt.TenantId == CurrentTenantId);

        modelBuilder.Entity<CustomerTerms>()
            .HasQueryFilter(terms => terms.TenantId == CurrentTenantId);

        base.OnModelCreating(modelBuilder);
    }
}
