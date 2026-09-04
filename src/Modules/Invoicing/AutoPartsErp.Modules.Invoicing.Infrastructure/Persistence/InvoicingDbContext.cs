using AutoPartsErp.Modules.Invoicing.Domain;
using AutoPartsErp.Modules.Invoicing.Domain.Invoices;
using AutoPartsErp.Modules.Invoicing.Domain.Series;
using AutoPartsErp.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace AutoPartsErp.Modules.Invoicing.Infrastructure.Persistence;

/// <summary>
/// The Invoicing module's database context, scoped to the <c>invoicing</c> schema.
/// <para>
/// It maps no Sales table and no Partners table. A customer, a part and a sales order are bare
/// Guid columns here — and unusually, that is not only a module-boundary decision. A document is
/// a legal record of what was true on the day, so it snapshots the customer's name and tax
/// number rather than joining to them. A customer renamed in 2028 must not change an invoice
/// issued in 2026.
/// </para>
/// </summary>
public sealed class InvoicingDbContext : ModuleDbContext, IInvoicingUnitOfWork
{
    /// <summary>The PostgreSQL schema this context owns.</summary>
    public const string SchemaName = "invoicing";

    /// <summary>Initializes the context.</summary>
    /// <param name="options">EF Core options, supplied by the container.</param>
    /// <param name="dependencies">
    /// Shared plumbing: the tenant, the domain event dispatcher and the outbox. Optional so the
    /// design-time tooling can build the model with no container behind it.
    /// </param>
    public InvoicingDbContext(
        DbContextOptions<InvoicingDbContext> options,
        ModuleDbContextDependencies? dependencies = null)
        : base(options, dependencies)
    {
    }

    /// <summary>The registered runs of document numbers.</summary>
    public DbSet<DocumentSeries> DocumentSeries => Set<DocumentSeries>();

    /// <summary>The documents themselves, with their lines.</summary>
    public DbSet<Invoice> Invoices => Set<Invoice>();

    /// <inheritdoc />
    public async Task<IInvoicingTransaction> BeginAsync(CancellationToken cancellationToken = default)
    {
        IDbContextTransaction transaction = await Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        return new EfInvoicingTransaction(transaction);
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(InvoicingDbContext).Assembly);

        // No soft-delete filter on either of these, deliberately. A document is never archived
        // and never deleted: it has a number in a gapless series that was reported to the tax
        // authority, and hiding it behind a query filter is the same thing as losing it. Voiding
        // is a status, and a voided document stays in every list it was ever in.
        modelBuilder.Entity<DocumentSeries>()
            .HasQueryFilter(series => series.TenantId == CurrentTenantId);

        modelBuilder.Entity<Invoice>()
            .HasQueryFilter(invoice => invoice.TenantId == CurrentTenantId);

        base.OnModelCreating(modelBuilder);
    }

    private sealed class EfInvoicingTransaction : IInvoicingTransaction
    {
        private readonly IDbContextTransaction _transaction;

        public EfInvoicingTransaction(IDbContextTransaction transaction)
        {
            _transaction = transaction;
        }

        public Task CommitAsync(CancellationToken cancellationToken = default) =>
            _transaction.CommitAsync(cancellationToken);

        // Disposing an uncommitted transaction rolls it back, which is what should happen when
        // anything between taking a number and saving the document fails. The number goes back
        // and there is no gap.
        public ValueTask DisposeAsync() => _transaction.DisposeAsync();
    }
}
