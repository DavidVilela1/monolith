using AutoPartsErp.Modules.Finance.Application.Abstractions;
using AutoPartsErp.Modules.Finance.Application.Contracts;
using AutoPartsErp.Modules.Finance.Domain;
using AutoPartsErp.Modules.Finance.Domain.Ledger;
using AutoPartsErp.Modules.Finance.Domain.Receipts;
using AutoPartsErp.Modules.Finance.Domain.Receivables;
using AutoPartsErp.SharedKernel.Paging;
using AutoPartsErp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace AutoPartsErp.Modules.Finance.Infrastructure.Persistence.ReadStore;

/// <summary>
/// The read side of the Finance module.
/// <para>
/// Projects columns rather than loading aggregates. A statement and an ageing report are the two
/// screens somebody looks at all day, and neither has any use for behaviour.
/// </para>
/// </summary>
public sealed class FinanceReadStore : IFinanceReadStore
{
    private readonly FinanceDbContext _context;

    /// <summary>Initializes the read store.</summary>
    public FinanceReadStore(FinanceDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<CustomerStatement?> GetStatementAsync(
        Guid customerId,
        DateOnly asAt,
        CancellationToken cancellationToken = default)
    {
        var customer = new CustomerRef(customerId);

        var terms = await _context.CustomerTerms
            .AsNoTracking()
            .Where(row => row.Id == customer)
            .Select(row => new
            {
                row.Code,
                row.LegalName,
                CurrencyCode = row.Currency.Code,
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (terms is null)
        {
            return null;
        }

        IReadOnlyList<OpenItemDto> items = await ListOutstandingAsync(
            customerId, asAt, cancellationToken).ConfigureAwait(false);

        decimal balance = items.Sum(item => item.SignedOutstanding);

        // Only debits count as overdue. A credit note nobody has allocated yet is money the
        // company owes, and calling it "overdue" would put it in a chasing report.
        decimal overdue = items
            .Where(item => item.DaysOverdue > 0 && item.SignedOutstanding > 0m)
            .Sum(item => item.SignedOutstanding);

        decimal unallocated = await _context.Receipts
            .AsNoTracking()
            .Where(receipt => receipt.CustomerId == customer)
            .Where(receipt => receipt.Status == ReceiptStatus.Unallocated
                || receipt.Status == ReceiptStatus.PartiallyAllocated)
            .SumAsync(
                receipt => receipt.Amount.Amount - receipt.AllocatedAmount.Amount,
                cancellationToken)
            .ConfigureAwait(false);

        return new CustomerStatement(
            customerId,
            terms.Code,
            terms.LegalName,
            terms.CurrencyCode,
            asAt,
            balance,
            overdue,
            unallocated,
            items);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OpenItemDto>> ListOutstandingAsync(
        Guid customerId,
        DateOnly asAt,
        CancellationToken cancellationToken = default)
    {
        var customer = new CustomerRef(customerId);

        List<OpenItemDto> items = await _context.OpenItems
            .AsNoTracking()
            .Where(item => item.CustomerId == customer)
            .Where(item => item.Status == OpenItemStatus.Open
                || item.Status == OpenItemStatus.PartiallySettled)
            .OrderBy(item => item.DueDate)
            .ThenBy(item => item.DocumentDate)
            .Select(item => new OpenItemDto(
                item.Id.Value,
                item.DocumentId.Value,
                item.DocumentNumber,
                item.Kind.ToString(),
                item.Status.ToString(),
                item.DocumentDate,
                item.DueDate,
                item.OriginalAmount.Amount,
                item.SettledAmount.Amount,
                item.OriginalAmount.Amount - item.SettledAmount.Amount,
                item.Kind == OpenItemKind.CreditNote
                    ? -(item.OriginalAmount.Amount - item.SettledAmount.Amount)
                    : item.OriginalAmount.Amount - item.SettledAmount.Amount,
                item.OriginalAmount.Currency.Code,
                asAt > item.DueDate ? asAt.DayNumber - item.DueDate.DayNumber : 0))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return items;
    }

    /// <inheritdoc />
    public async Task<PagedResult<AgingRow>> GetAgingAsync(
        DateOnly asAt,
        PageRequest page,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);

        // The bucket edges are worked out here and go into the query as four dates. Doing the
        // arithmetic in C# rather than in the expression keeps the SQL to four comparisons
        // against a constant, which the partial index on due_date can actually use.
        DateOnly thirty = asAt.AddDays(-30);
        DateOnly sixty = asAt.AddDays(-60);
        DateOnly ninety = asAt.AddDays(-90);

        IQueryable<OpenItem> outstanding = _context.OpenItems
            .AsNoTracking()
            .Where(item => item.Status == OpenItemStatus.Open
                || item.Status == OpenItemStatus.PartiallySettled);

        // The signed amount is written out in full six times rather than called through a helper.
        // A helper would read better and would not translate: everything inside these expressions
        // has to become SQL, and a method call becomes nothing at all - it throws at runtime, on
        // the one screen a credit controller opens every morning.
        var grouped = outstanding
            .GroupBy(item => item.CustomerId)
            .Select(group => new
            {
                CustomerId = group.Key,
                CurrencyCode = group.Max(item => item.OriginalAmount.Currency.Code),

                NotYetDue = group.Sum(item => item.DueDate >= asAt
                    ? (item.Kind == OpenItemKind.CreditNote ? -1m : 1m)
                        * (item.OriginalAmount.Amount - item.SettledAmount.Amount)
                    : 0m),

                OneToThirty = group.Sum(item => item.DueDate < asAt && item.DueDate >= thirty
                    ? (item.Kind == OpenItemKind.CreditNote ? -1m : 1m)
                        * (item.OriginalAmount.Amount - item.SettledAmount.Amount)
                    : 0m),

                ThirtyOneToSixty = group.Sum(item => item.DueDate < thirty && item.DueDate >= sixty
                    ? (item.Kind == OpenItemKind.CreditNote ? -1m : 1m)
                        * (item.OriginalAmount.Amount - item.SettledAmount.Amount)
                    : 0m),

                SixtyOneToNinety = group.Sum(item => item.DueDate < sixty && item.DueDate >= ninety
                    ? (item.Kind == OpenItemKind.CreditNote ? -1m : 1m)
                        * (item.OriginalAmount.Amount - item.SettledAmount.Amount)
                    : 0m),

                OverNinety = group.Sum(item => item.DueDate < ninety
                    ? (item.Kind == OpenItemKind.CreditNote ? -1m : 1m)
                        * (item.OriginalAmount.Amount - item.SettledAmount.Amount)
                    : 0m),

                Total = group.Sum(item =>
                    (item.Kind == OpenItemKind.CreditNote ? -1m : 1m)
                        * (item.OriginalAmount.Amount - item.SettledAmount.Amount)),

                OldestDueDate = group.Min(item => item.DueDate),
            });

        int total = await grouped.CountAsync(cancellationToken).ConfigureAwait(false);

        if (total == 0)
        {
            return PagedResult<AgingRow>.Empty(page.Page, page.PageSize);
        }

        var rows = await grouped
            .OrderByDescending(row => row.Total)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // The customer's code and name live in another table, and joining them into a grouped
        // query is how a readable projection becomes an unreadable one. Two queries, and the
        // second is over one page's worth of identifiers.
        List<CustomerRef> ids = [.. rows.Select(row => row.CustomerId)];

        var names = await _context.CustomerTerms
            .AsNoTracking()
            .Where(terms => ids.Contains(terms.Id))
            .Select(terms => new { terms.Id, terms.Code, terms.LegalName })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<CustomerRef, (string Code, string Name)> named = names.ToDictionary(
            row => row.Id,
            row => (row.Code, row.LegalName));

        var items = new List<AgingRow>(rows.Count);

        foreach (var row in rows)
        {
            // A customer with a balance and no terms on file is possible: the ledger is fed by
            // Invoicing and the terms by Partners, and nothing orders the two. Blank rather than
            // dropped, because a row missing from an ageing report is money nobody chases.
            named.TryGetValue(row.CustomerId, out (string Code, string Name) terms);

            items.Add(new AgingRow(
                row.CustomerId.Value,
                terms.Code ?? string.Empty,
                terms.Name ?? string.Empty,
                // Max over a group is null-returning as far as the compiler is concerned, even
                // though a group here always has at least one row to take a currency from.
                row.CurrencyCode ?? string.Empty,
                row.NotYetDue,
                row.OneToThirty,
                row.ThirtyOneToSixty,
                row.SixtyOneToNinety,
                row.OverNinety,
                row.Total,
                row.OldestDueDate));
        }

        return PagedResult<AgingRow>.Create(items, page.Page, page.PageSize, total);
    }

    /// <inheritdoc />
    public async Task<ReceiptDto?> GetReceiptAsync(
        Guid receiptId,
        CancellationToken cancellationToken = default)
    {
        var id = new ReceiptId(receiptId);

        ReceiptDto? receipt = await Project(_context.Receipts.AsNoTracking()
                .Where(row => row.Id == id))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return receipt;
    }

    /// <inheritdoc />
    public async Task<PagedResult<ReceiptDto>> SearchReceiptsAsync(
        ReceiptSearchCriteria criteria,
        PageRequest page,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        ArgumentNullException.ThrowIfNull(page);

        IQueryable<Receipt> query = _context.Receipts.AsNoTracking();

        if (criteria.CustomerId is { } customerId)
        {
            var customer = new CustomerRef(customerId);
            query = query.Where(receipt => receipt.CustomerId == customer);
        }

        if (criteria.From is { } from)
        {
            query = query.Where(receipt => receipt.ReceivedOn >= from);
        }

        if (criteria.To is { } to)
        {
            query = query.Where(receipt => receipt.ReceivedOn <= to);
        }

        if (criteria.OnlyUnallocated)
        {
            query = query.Where(receipt => receipt.Status == ReceiptStatus.Unallocated
                || receipt.Status == ReceiptStatus.PartiallyAllocated);
        }

        int total = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        if (total == 0)
        {
            return PagedResult<ReceiptDto>.Empty(page.Page, page.PageSize);
        }

        List<ReceiptDto> items = await Project(
                query.OrderByDescending(receipt => receipt.ReceivedOn)
                    .ThenByDescending(receipt => receipt.Number)
                    .Skip(page.Skip)
                    .Take(page.PageSize))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return PagedResult<ReceiptDto>.Create(items, page.Page, page.PageSize, total);
    }

    /// <summary>
    /// Turns receipts into what a screen shows.
    /// <para>
    /// The customer's name comes from a correlated subquery rather than a join, because it is
    /// optional: terms and the ledger are fed by different modules and nothing orders them, so a
    /// receipt can exist for a customer Finance has not been told about yet.
    /// </para>
    /// </summary>
    private IQueryable<ReceiptDto> Project(IQueryable<Receipt> query) =>
        query.Select(receipt => new ReceiptDto(
            receipt.Id.Value,
            receipt.Number,
            receipt.CustomerId.Value,
            _context.CustomerTerms
                .Where(terms => terms.Id == receipt.CustomerId)
                .Select(terms => terms.LegalName)
                .FirstOrDefault() ?? string.Empty,
            receipt.ReceivedOn,
            receipt.Method.ToString(),
            receipt.Reference,
            receipt.Amount.Amount,
            receipt.AllocatedAmount.Amount,
            receipt.Amount.Amount - receipt.AllocatedAmount.Amount,
            receipt.Amount.Currency.Code,
            receipt.Status.ToString(),
            receipt.Allocations
                .Select(allocation => new ReceiptAllocationDto(
                    allocation.OpenItemId.Value,
                    allocation.DocumentNumber,
                    allocation.Amount.Amount,
                    allocation.AllocatedOn))
                .ToList()));

    /// <inheritdoc />
    public async Task<TrialBalance> GetTrialBalanceAsync(
        DateOnly? from,
        DateOnly to,
        bool includeUnmoved = false,
        CancellationToken cancellationToken = default)
    {
        IQueryable<JournalEntry> posted = _context.JournalEntries
            .AsNoTracking()
            .Where(entry => entry.Status == JournalEntryStatus.Posted)
            .Where(entry => entry.EntryDate <= to);

        if (from is DateOnly start)
        {
            posted = posted.Where(entry => entry.EntryDate >= start);
        }

        // Summed in the database rather than by loading the lines: a year of a busy branch is
        // hundreds of thousands of rows and the answer is one per account. Written as a sum over
        // a CASE rather than as two filtered aggregates, because that is the shape every provider
        // translates the same way.
        var movements = await posted
            .SelectMany(entry => entry.Lines)
            .GroupBy(line => line.AccountId)
            .Select(group => new
            {
                AccountId = group.Key,
                Debits = group.Sum(line =>
                    line.Side == EntrySide.Debit ? line.Amount.Amount : 0m),
                Credits = group.Sum(line =>
                    line.Side == EntrySide.Credit ? line.Amount.Amount : 0m),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<AccountId, (decimal Debits, decimal Credits)> byAccount = movements
            .ToDictionary(row => row.AccountId, row => (row.Debits, row.Credits));

        var accounts = await _context.Accounts
            .AsNoTracking()
            .Where(account => account.AllowsPosting)
            .OrderBy(account => account.Code)
            .Select(account => new
            {
                account.Id,
                account.Code,
                account.Name,
                account.Type,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var rows = new List<TrialBalanceRow>(accounts.Count);
        decimal totalDebits = 0m;
        decimal totalCredits = 0m;

        foreach (var account in accounts)
        {
            (decimal debits, decimal credits) =
                byAccount.GetValueOrDefault(account.Id, (0m, 0m));

            if (!includeUnmoved && debits == 0m && credits == 0m)
            {
                continue;
            }

            totalDebits += debits;
            totalCredits += credits;

            // Signed the way the account grows, so somebody can read the column down instead of
            // doing the subtraction in their head twelve times.
            decimal balance = Account.NormalSideFor(account.Type) == EntrySide.Debit
                ? debits - credits
                : credits - debits;

            rows.Add(new TrialBalanceRow(
                account.Id.Value,
                account.Code,
                account.Name,
                account.Type.ToString(),
                debits,
                credits,
                balance,
                Currency.Default.Code));
        }

        return new TrialBalance(
            from,
            to,
            rows,
            totalDebits,
            totalCredits,
            totalDebits == totalCredits,
            Currency.Default.Code);
    }

    /// <inheritdoc />
    public async Task<AccountStatement?> GetAccountStatementAsync(
        string code,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        string normalized = code?.Trim().ToUpperInvariant() ?? string.Empty;

        var account = await _context.Accounts
            .AsNoTracking()
            .Where(row => row.Code == normalized)
            .Select(row => new { row.Id, row.Code, row.Name, row.Type })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (account is null)
        {
            return null;
        }

        int sign = Account.NormalSideFor(account.Type) == EntrySide.Debit ? 1 : -1;

        // The opening balance is summed from every earlier posting rather than carried forward.
        // Nothing here carries balances forward yet, and a figure summed from the postings is one
        // that cannot disagree with them.
        // Nullable, because SUM over no rows is NULL and an account with nothing before the
        // window is the ordinary case on the first statement anybody prints.
        decimal? before = await _context.JournalEntries
            .AsNoTracking()
            .Where(entry => entry.Status == JournalEntryStatus.Posted)
            .Where(entry => entry.EntryDate < from)
            .SelectMany(entry => entry.Lines)
            .Where(line => line.AccountId == account.Id)
            .SumAsync(
                line => (decimal?)(line.Side == EntrySide.Debit
                    ? line.Amount.Amount
                    : -line.Amount.Amount),
                cancellationToken)
            .ConfigureAwait(false);

        decimal opening = sign * (before ?? 0m);

        var rows = await _context.JournalEntries
            .AsNoTracking()
            .Where(entry => entry.Status == JournalEntryStatus.Posted)
            .Where(entry => entry.EntryDate >= from && entry.EntryDate <= to)
            .SelectMany(
                entry => entry.Lines,
                (entry, line) => new
                {
                    entry.Id,
                    entry.Number,
                    entry.EntryDate,
                    entry.Source,
                    entry.Description,
                    entry.Reference,
                    line.AccountId,
                    line.Narrative,
                    line.Side,
                    Amount = line.Amount.Amount,
                })
            .Where(row => row.AccountId == account.Id)
            .OrderBy(row => row.EntryDate)
            .ThenBy(row => row.Number)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var lines = new List<AccountStatementLine>(rows.Count);
        decimal running = opening;

        foreach (var row in rows)
        {
            bool isDebit = row.Side == EntrySide.Debit;
            running += sign * (isDebit ? row.Amount : -row.Amount);

            lines.Add(new AccountStatementLine(
                row.Id.Value,
                row.Number,
                row.EntryDate,
                row.Source.ToString(),
                row.Description,
                row.Narrative,
                row.Reference,
                isDebit ? row.Amount : 0m,
                isDebit ? 0m : row.Amount,
                running));
        }

        return new AccountStatement(
            account.Id.Value,
            account.Code,
            account.Name,
            account.Type.ToString(),
            from,
            to,
            opening,
            running,
            lines,
            Currency.Default.Code);
    }

    /// <inheritdoc />
    public async Task<PagedResult<JournalEntryRow>> SearchJournalAsync(
        DateOnly from,
        DateOnly to,
        string? source,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        IQueryable<JournalEntry> query = _context.JournalEntries
            .AsNoTracking()
            .Where(entry => entry.Status == JournalEntryStatus.Posted)
            .Where(entry => entry.EntryDate >= from && entry.EntryDate <= to);

        // An unrecognized journal name returns nothing rather than everything. Silently ignoring
        // a filter somebody typed is how a person reads the wrong report and believes it.
        if (!string.IsNullOrWhiteSpace(source))
        {
            if (!Enum.TryParse(source, ignoreCase: true, out JournalSource parsed)
                || parsed == JournalSource.Unknown)
            {
                return PagedResult<JournalEntryRow>.Empty(page, pageSize);
            }

            query = query.Where(entry => entry.Source == parsed);
        }

        int total = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        List<JournalEntryRow> items = await query
            .OrderByDescending(entry => entry.EntryDate)
            .ThenByDescending(entry => entry.Number)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(entry => new JournalEntryRow(
                entry.Id.Value,
                entry.Number,
                entry.EntryDate,
                entry.Source.ToString(),
                entry.Description,
                entry.Reference,
                entry.Lines.Sum(line =>
                    line.Side == EntrySide.Debit ? line.Amount.Amount : 0m),
                entry.Lines.Count,
                entry.CurrencyCode,
                entry.PostedAtUtc))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return PagedResult<JournalEntryRow>.Create(items, page, pageSize, total);
    }

}
