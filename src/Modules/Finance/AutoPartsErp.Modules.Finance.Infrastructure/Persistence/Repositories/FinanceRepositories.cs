using System.Globalization;
using AutoPartsErp.Modules.Finance.Domain;
using AutoPartsErp.Modules.Finance.Domain.Customers;
using AutoPartsErp.Modules.Finance.Domain.Receipts;
using AutoPartsErp.Modules.Finance.Domain.Ledger;
using AutoPartsErp.Modules.Finance.Domain.Payables;
using AutoPartsErp.Modules.Finance.Domain.Payments;
using AutoPartsErp.Modules.Finance.Domain.Receivables;
using Microsoft.EntityFrameworkCore;

namespace AutoPartsErp.Modules.Finance.Infrastructure.Persistence.Repositories;

/// <summary>Write-side access to the items on customers' accounts.</summary>
public sealed class OpenItemRepository : IOpenItemRepository
{
    private readonly FinanceDbContext _context;

    /// <summary>Initializes the repository.</summary>
    public OpenItemRepository(FinanceDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public Task<OpenItem?> GetByIdAsync(
        OpenItemId id,
        CancellationToken cancellationToken = default) =>
        _context.OpenItems.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsAsync(OpenItemId id, CancellationToken cancellationToken = default) =>
        _context.OpenItems.AnyAsync(item => item.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<OpenItem?> GetByDocumentAsync(
        DocumentRef documentId,
        CancellationToken cancellationToken = default) =>
        _context.OpenItems.FirstOrDefaultAsync(
            item => item.DocumentId == documentId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<OpenItem>> GetManyAsync(
        IReadOnlyCollection<OpenItemId> ids,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        if (ids.Count == 0)
        {
            return [];
        }

        // One query rather than one per identifier. An allocation screen sends a dozen of these
        // at a time, and a loop of round trips is how a settlement that should take milliseconds
        // takes a second.
        List<OpenItem> items = await _context.OpenItems
            .Where(item => ids.Contains(item.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return items;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OpenItem>> GetOutstandingForCustomerAsync(
        CustomerRef customerId,
        CancellationToken cancellationToken = default)
    {
        List<OpenItem> items = await _context.OpenItems
            .Where(item => item.CustomerId == customerId)
            .Where(item => item.Status == OpenItemStatus.Open
                || item.Status == OpenItemStatus.PartiallySettled)
            .OrderBy(item => item.DueDate)
            .ThenBy(item => item.DocumentDate)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return items;
    }

    /// <inheritdoc />
    public void Add(OpenItem aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.OpenItems.Add(aggregate);
    }

    /// <inheritdoc />
    public void Remove(OpenItem aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.OpenItems.Remove(aggregate);
    }
}

/// <summary>
/// Write-side access to receipts.
/// <para>
/// No <c>Include</c> for the allocations: they are an owned collection and EF loads them with
/// their receipt. There is no way to load half a receipt, which here is not a convenience but a
/// requirement — the allocated total is checked against them on every match.
/// </para>
/// </summary>
public sealed class ReceiptRepository : IReceiptRepository
{
    private const string NumberPrefix = "RC";

    /// <summary>Identifies this run of numbers in the module's counter table.</summary>
    private const string NumberKey = "receipt";

    private readonly FinanceDbContext _context;

    /// <summary>Initializes the repository.</summary>
    public ReceiptRepository(FinanceDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public Task<Receipt?> GetByIdAsync(
        ReceiptId id,
        CancellationToken cancellationToken = default) =>
        _context.Receipts.FirstOrDefaultAsync(receipt => receipt.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsAsync(ReceiptId id, CancellationToken cancellationToken = default) =>
        _context.Receipts.AnyAsync(receipt => receipt.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<Receipt?> GetByNumberAsync(
        string number,
        CancellationToken cancellationToken = default)
    {
        string normalized = number?.Trim().ToUpperInvariant() ?? string.Empty;

        return _context.Receipts.FirstOrDefaultAsync(
            receipt => receipt.Number == normalized, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<string> NextReceiptNumberAsync(
        int year,
        CancellationToken cancellationToken = default)
    {
        int next = await _context
            .TakeNextNumberAsync(NumberKey, year, cancellationToken)
            .ConfigureAwait(false);

        return string.Create(CultureInfo.InvariantCulture, $"{NumberPrefix}-{year}-{next:D5}");
    }

    /// <inheritdoc />
    public void Add(Receipt aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.Receipts.Add(aggregate);
    }

    /// <inheritdoc />
    public void Remove(Receipt aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.Receipts.Remove(aggregate);
    }
}

/// <summary>Write-side access to customers' payment terms.</summary>
public sealed class CustomerTermsRepository : ICustomerTermsRepository
{
    private readonly FinanceDbContext _context;

    /// <summary>Initializes the repository.</summary>
    public CustomerTermsRepository(FinanceDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public Task<CustomerTerms?> GetByIdAsync(
        CustomerRef id,
        CancellationToken cancellationToken = default) =>
        _context.CustomerTerms.FirstOrDefaultAsync(terms => terms.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsAsync(CustomerRef id, CancellationToken cancellationToken = default) =>
        _context.CustomerTerms.AnyAsync(terms => terms.Id == id, cancellationToken);

    /// <inheritdoc />
    public void Add(CustomerTerms aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.CustomerTerms.Add(aggregate);
    }

    /// <inheritdoc />
    public void Remove(CustomerTerms aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.CustomerTerms.Remove(aggregate);
    }
}

/// <summary>Write-side access to the purchase ledger.</summary>
public sealed class PayableItemRepository : IPayableItemRepository
{
    private readonly FinanceDbContext _context;

    /// <summary>Initializes the repository.</summary>
    public PayableItemRepository(FinanceDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public Task<PayableItem?> GetByIdAsync(
        PayableItemId id,
        CancellationToken cancellationToken = default) =>
        _context.PayableItems.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsAsync(PayableItemId id, CancellationToken cancellationToken = default) =>
        _context.PayableItems.AnyAsync(item => item.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<PayableItem?> GetByDocumentAsync(
        SupplierInvoiceRef supplierInvoiceId,
        CancellationToken cancellationToken = default) =>
        _context.PayableItems.FirstOrDefaultAsync(
            item => item.SupplierInvoiceId == supplierInvoiceId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<PayableItem>> GetOutstandingForAsync(
        SupplierRef supplierId,
        CancellationToken cancellationToken = default)
    {
        List<PayableItem> items = await _context.PayableItems
            .Where(item => item.SupplierId == supplierId)
            .Where(item => item.Status == PayableItemStatus.Open
                || item.Status == PayableItemStatus.PartiallySettled)
            .OrderBy(item => item.DueDate)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return items;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PayableItem>> GetDueByAsync(
        DateOnly on,
        CancellationToken cancellationToken = default)
    {
        List<PayableItem> items = await _context.PayableItems
            .Where(item => item.DueDate <= on)
            .Where(item => item.Status == PayableItemStatus.Open
                || item.Status == PayableItemStatus.PartiallySettled)
            .OrderBy(item => item.DueDate)
            .ThenBy(item => item.SupplierCode)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return items;
    }

    /// <inheritdoc />
    public void Add(PayableItem aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.PayableItems.Add(aggregate);
    }

    /// <inheritdoc />
    public void Remove(PayableItem aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.PayableItems.Remove(aggregate);
    }
}

/// <summary>Write-side access to money paid to suppliers.</summary>
public sealed class SupplierPaymentRepository : ISupplierPaymentRepository
{
    private const string NumberPrefix = "SP";

    /// <summary>Identifies this run of numbers in the module's counter table.</summary>
    private const string NumberKey = "supplier-payment";

    private readonly FinanceDbContext _context;

    /// <summary>Initializes the repository.</summary>
    public SupplierPaymentRepository(FinanceDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public Task<SupplierPayment?> GetByIdAsync(
        SupplierPaymentId id,
        CancellationToken cancellationToken = default) =>
        _context.SupplierPayments.FirstOrDefaultAsync(
            payment => payment.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsAsync(
        SupplierPaymentId id,
        CancellationToken cancellationToken = default) =>
        _context.SupplierPayments.AnyAsync(payment => payment.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<SupplierPayment?> GetByNumberAsync(
        string number,
        CancellationToken cancellationToken = default)
    {
        string normalized = number?.Trim().ToUpperInvariant() ?? string.Empty;

        return _context.SupplierPayments.FirstOrDefaultAsync(
            payment => payment.Number == normalized, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<string> NextPaymentNumberAsync(
        int year,
        CancellationToken cancellationToken = default)
    {
        int next = await _context
            .TakeNextNumberAsync(NumberKey, year, cancellationToken)
            .ConfigureAwait(false);

        return string.Create(CultureInfo.InvariantCulture, $"{NumberPrefix}-{year}-{next:D5}");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SupplierPayment>> GetUnallocatedForAsync(
        SupplierRef supplierId,
        CancellationToken cancellationToken = default)
    {
        List<SupplierPayment> payments = await _context.SupplierPayments
            .Where(payment => payment.SupplierId == supplierId)
            .Where(payment => payment.Status == PaymentStatus.Unallocated
                || payment.Status == PaymentStatus.PartiallyAllocated)
            .OrderBy(payment => payment.PaidOn)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return payments;
    }

    /// <inheritdoc />
    public void Add(SupplierPayment aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.SupplierPayments.Add(aggregate);
    }

    /// <inheritdoc />
    public void Remove(SupplierPayment aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.SupplierPayments.Remove(aggregate);
    }
}

/// <summary>Write-side access to the chart of accounts.</summary>
public sealed class AccountRepository : IAccountRepository
{
    private readonly FinanceDbContext _context;

    /// <summary>Initializes the repository.</summary>
    public AccountRepository(FinanceDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public Task<Account?> GetByIdAsync(AccountId id, CancellationToken cancellationToken = default) =>
        _context.Accounts.FirstOrDefaultAsync(account => account.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsAsync(AccountId id, CancellationToken cancellationToken = default) =>
        _context.Accounts.AnyAsync(account => account.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<Account?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        string normalized = Normalize(code);

        return _context.Accounts.FirstOrDefaultAsync(
            account => account.Code == normalized, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> CodeExistsAsync(
        string code,
        AccountId? excluding = null,
        CancellationToken cancellationToken = default)
    {
        string normalized = Normalize(code);

        return _context.Accounts
            .Where(account => account.Code == normalized)
            .Where(account => excluding == null || account.Id != excluding.Value)
            .AnyAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, Account>> GetByCodesAsync(
        IReadOnlyCollection<string> codes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(codes);

        if (codes.Count == 0)
        {
            return new Dictionary<string, Account>(StringComparer.Ordinal);
        }

        List<string> normalized = [.. codes.Select(Normalize).Distinct(StringComparer.Ordinal)];

        List<Account> accounts = await _context.Accounts
            .Where(account => normalized.Contains(account.Code))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return accounts.ToDictionary(account => account.Code, StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Account>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        List<Account> accounts = await _context.Accounts
            .OrderBy(account => account.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return accounts;
    }

    /// <inheritdoc />
    public void Add(Account aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.Accounts.Add(aggregate);
    }

    /// <inheritdoc />
    public void Remove(Account aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.Accounts.Remove(aggregate);
    }

    private static string Normalize(string? code) => code?.Trim().ToUpperInvariant() ?? string.Empty;
}

/// <summary>
/// Write-side access to the general ledger.
/// <para>
/// No <c>Include</c> for the lines: they are an owned collection, so EF loads them with the entry.
/// Half a journal entry is an unbalanced one.
/// </para>
/// </summary>
public sealed class JournalEntryRepository : IJournalEntryRepository
{
    private const string NumberPrefix = "JE";

    /// <summary>Identifies this run of numbers in the module's counter table.</summary>
    private const string NumberKey = "journal-entry";

    private readonly FinanceDbContext _context;

    /// <summary>Initializes the repository.</summary>
    public JournalEntryRepository(FinanceDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public Task<JournalEntry?> GetByIdAsync(
        JournalEntryId id,
        CancellationToken cancellationToken = default) =>
        _context.JournalEntries.FirstOrDefaultAsync(entry => entry.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsAsync(JournalEntryId id, CancellationToken cancellationToken = default) =>
        _context.JournalEntries.AnyAsync(entry => entry.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<JournalEntry?> GetByNumberAsync(
        string number,
        CancellationToken cancellationToken = default)
    {
        string normalized = number?.Trim().ToUpperInvariant() ?? string.Empty;

        return _context.JournalEntries.FirstOrDefaultAsync(
            entry => entry.Number == normalized, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<string> NextEntryNumberAsync(
        int year,
        CancellationToken cancellationToken = default)
    {
        int next = await _context
            .TakeNextNumberAsync(NumberKey, year, cancellationToken)
            .ConfigureAwait(false);

        return string.Create(CultureInfo.InvariantCulture, $"{NumberPrefix}-{year}-{next:D5}");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<JournalEntry>> GetPostedBetweenAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        List<JournalEntry> entries = await _context.JournalEntries
            .Where(entry => entry.Status == JournalEntryStatus.Posted)
            .Where(entry => entry.EntryDate >= from && entry.EntryDate <= to)
            .OrderBy(entry => entry.EntryDate)
            .ThenBy(entry => entry.Number)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return entries;
    }

    /// <inheritdoc />
    public void Add(JournalEntry aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.JournalEntries.Add(aggregate);
    }

    /// <inheritdoc />
    public void Remove(JournalEntry aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.JournalEntries.Remove(aggregate);
    }
}

/// <summary>Write-side access to the accounting calendar.</summary>
public sealed class AccountingPeriodRepository : IAccountingPeriodRepository
{
    private readonly FinanceDbContext _context;

    /// <summary>Initializes the repository.</summary>
    public AccountingPeriodRepository(FinanceDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public Task<AccountingPeriod?> GetByIdAsync(
        AccountingPeriodId id,
        CancellationToken cancellationToken = default) =>
        _context.AccountingPeriods.FirstOrDefaultAsync(period => period.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsAsync(
        AccountingPeriodId id,
        CancellationToken cancellationToken = default) =>
        _context.AccountingPeriods.AnyAsync(period => period.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<AccountingPeriod?> GetForAsync(
        DateOnly on,
        CancellationToken cancellationToken = default) =>
        GetForAsync(on.Year, on.Month, cancellationToken);

    /// <inheritdoc />
    public Task<AccountingPeriod?> GetForAsync(
        int year,
        int month,
        CancellationToken cancellationToken = default) =>
        _context.AccountingPeriods.FirstOrDefaultAsync(
            period => period.Year == year && period.Month == month, cancellationToken);

    /// <inheritdoc />
    public async Task<bool> EveryEarlierIsClosedAsync(
        int year,
        int month,
        CancellationToken cancellationToken = default)
    {
        int key = AccountingPeriod.Key(year, month);

        // Written out rather than through Ordinal, which is computed and has no column. The
        // arithmetic is the same one the aggregate does, and it is here because the database has
        // to be able to filter on it.
        bool anyEarlierOpen = await _context.AccountingPeriods
            .Where(period => (period.Year * 12) + period.Month < key)
            .Where(period => period.Status == PeriodStatus.Open)
            .AnyAsync(cancellationToken)
            .ConfigureAwait(false);

        return !anyEarlierOpen;
    }

    /// <inheritdoc />
    public Task<bool> AnyLaterIsClosedAsync(
        int year,
        int month,
        CancellationToken cancellationToken = default)
    {
        int key = AccountingPeriod.Key(year, month);

        return _context.AccountingPeriods
            .Where(period => (period.Year * 12) + period.Month > key)
            .Where(period => period.Status == PeriodStatus.Closed)
            .AnyAsync(cancellationToken);
    }

    /// <inheritdoc />
    public void Add(AccountingPeriod aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.AccountingPeriods.Add(aggregate);
    }

    /// <inheritdoc />
    public void Remove(AccountingPeriod aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.AccountingPeriods.Remove(aggregate);
    }
}

/// <summary>Reads and writes the mapping from facts to account codes.</summary>
public sealed class PostingRuleRepository : IPostingRuleRepository
{
    private readonly FinanceDbContext _context;

    /// <summary>Initializes the repository.</summary>
    public PostingRuleRepository(FinanceDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public Task<PostingRule?> GetByIdAsync(
        PostingRuleId id,
        CancellationToken cancellationToken = default) =>
        _context.PostingRules
            .Include(rule => rule.Lines)
            .FirstOrDefaultAsync(rule => rule.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsAsync(
        PostingRuleId id,
        CancellationToken cancellationToken = default) =>
        _context.PostingRules.AnyAsync(rule => rule.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<PostingRule?> GetForFactAsync(
        string factType,
        CancellationToken cancellationToken = default) =>
        _context.PostingRules
            .Include(rule => rule.Lines)
            .FirstOrDefaultAsync(rule => rule.FactType == factType, cancellationToken);

    /// <inheritdoc />
    public Task<bool> IsMappedAsync(
        string factType,
        CancellationToken cancellationToken = default) =>
        _context.PostingRules.AnyAsync(rule => rule.FactType == factType, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<PostingRule>> GetAllAsync(
        CancellationToken cancellationToken = default) =>
        await _context.PostingRules
            .Include(rule => rule.Lines)
            .OrderBy(rule => rule.FactType)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void Add(PostingRule aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.PostingRules.Add(aggregate);
    }

    /// <inheritdoc />
    public void Remove(PostingRule aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.PostingRules.Remove(aggregate);
    }
}
