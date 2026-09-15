using AutoPartsErp.Modules.Finance.Application.Ledger;
using AutoPartsErp.Modules.Finance.Domain;
using AutoPartsErp.Modules.Finance.Domain.Ledger;
using AutoPartsErp.SharedKernel.Abstractions;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Finance.Tests;

/// <summary>
/// Handing a financial fact to the ledger.
/// <para>
/// The piece that stops the general ledger being quietly incomplete. A fact with nothing to map it
/// has to go somewhere: dropping it leaves a ledger missing a month of sales that says so nowhere,
/// and every report built on it still looks right. So the fact is kept, with the sentence
/// explaining why it is stuck, and it waits.
/// </para>
/// </summary>
public sealed class LedgerPosterTests
{
    private static readonly DateOnly InMarch = new(2026, 3, 18);

    /// <summary>The ordinary case: a mapped fact reaches the ledger and balances.</summary>
    [Fact]
    public async Task A_mapped_fact_becomes_a_balanced_entry()
    {
        World world = World.WithSupplierRule();

        Result<JournalEntryId?> posted = await world.PostSupplierInvoiceAsync();

        posted.IsSuccess.Should().BeTrue();
        posted.Value.Should().NotBeNull();

        JournalEntry entry = world.Entries.Added.Single();
        entry.EntryDate.Should().Be(InMarch);
        entry.Source.Should().Be(JournalSource.Purchases);
        entry.Reference.Should().Be("BP/FA 1/2026");
        entry.IsBalanced.Should().BeTrue();
        entry.TotalDebits.Amount.Should().Be(814.75m);
        entry.Lines.Should().HaveCount(3);

        FactPosting fact = world.Facts.Added.Single();
        fact.Status.Should().Be(PostingStatus.Posted);
        fact.JournalEntryId.Should().Be(entry.Id);
        fact.PostedAtUtc.Should().NotBeNull();

        world.UnitOfWork.Saves.Should().Be(1);
    }

    /// <summary>
    /// A fact nobody has mapped is kept and waits. This is the normal state of a system being
    /// installed, and losing them meanwhile is the failure this whole object exists to prevent.
    /// </summary>
    [Fact]
    public async Task An_unmapped_fact_waits_and_says_why()
    {
        World world = World.WithNoRules();

        Result<JournalEntryId?> posted = await world.PostSupplierInvoiceAsync();

        // Succeeds: not posting is not an error here.
        posted.IsSuccess.Should().BeTrue();
        posted.Value.Should().BeNull();
        world.Entries.Added.Should().BeEmpty();

        FactPosting fact = world.Facts.Added.Single();
        fact.Status.Should().Be(PostingStatus.Waiting);
        fact.IsWaiting.Should().BeTrue();
        fact.Reason.Should().NotBeNullOrWhiteSpace();
        fact.Reason.Should().Contain(PostingFacts.SupplierInvoiceSettled);

        // And nothing about the fact itself was lost.
        fact.AmountsByKey()[PostingFacts.Gross].Amount.Should().Be(814.75m);
        fact.OccurredOn.Should().Be(InMarch);
    }

    /// <summary>
    /// The backlog posts as soon as the rule exists, and the fact remembers that it waited.
    /// </summary>
    [Fact]
    public async Task A_waiting_fact_posts_once_the_rule_is_written()
    {
        World world = World.WithNoRules();
        await world.PostSupplierInvoiceAsync();

        FactPosting fact = world.Facts.Added.Single();
        string? whyItWaited = fact.Reason;

        world.Rules.Add(World.SupplierRule());

        Result retried = await world.Poster.TryPostAsync(fact);

        retried.IsSuccess.Should().BeTrue();
        fact.Status.Should().Be(PostingStatus.Posted);
        world.Entries.Added.Should().ContainSingle();

        // Kept deliberately: "this waited because nothing mapped it" is what explains a late entry.
        fact.Reason.Should().Be(whyItWaited);
    }

    /// <summary>
    /// The outbox delivers at least once. The second delivery finds the row and does nothing,
    /// which is the difference between one sale in the books and two.
    /// </summary>
    [Fact]
    public async Task The_same_fact_arriving_twice_posts_once()
    {
        World world = World.WithSupplierRule();

        await world.PostSupplierInvoiceAsync();
        Result<JournalEntryId?> again = await world.PostSupplierInvoiceAsync();

        again.IsSuccess.Should().BeTrue();
        world.Entries.Added.Should().ContainSingle();
        world.Facts.Added.Should().ContainSingle();
    }

    /// <summary>
    /// A fact dated inside a month somebody has already reported waits rather than changing what
    /// that month said. It is the same guard the hand-written entry passes through.
    /// </summary>
    [Fact]
    public async Task A_fact_dated_in_a_closed_month_waits()
    {
        World world = World.WithSupplierRule();
        world.Periods.Close(2026, 3);

        Result<JournalEntryId?> posted = await world.PostSupplierInvoiceAsync();

        posted.IsSuccess.Should().BeTrue();
        world.Entries.Added.Should().BeEmpty();

        FactPosting fact = world.Facts.Added.Single();
        fact.IsWaiting.Should().BeTrue();
        fact.Reason.Should().Contain("closed");
    }

    /// <summary>
    /// A rule pointing at an account that does not exist waits with the code in the message,
    /// rather than throwing somewhere nobody is watching.
    /// </summary>
    [Fact]
    public async Task A_rule_naming_an_account_that_does_not_exist_waits()
    {
        World world = World.WithSupplierRule();
        world.Accounts.Forget("2432");

        Result<JournalEntryId?> posted = await world.PostSupplierInvoiceAsync();

        posted.IsSuccess.Should().BeTrue();
        world.Entries.Added.Should().BeEmpty();
        world.Facts.Added.Single().Reason.Should().Contain("2432");
    }

    /// <summary>
    /// A rule that does not balance is caught before a journal number is taken. A ledger with
    /// gaps in its numbering is one an auditor asks about.
    /// </summary>
    [Fact]
    public async Task A_rule_that_does_not_balance_waits_without_burning_a_number()
    {
        World world = World.WithNoRules();

        PostingRule crooked = PostingRule.Define(
            PostingFacts.SupplierInvoiceSettled, "Wrong", JournalSource.Purchases).Value;
        crooked.AddLine(PostingFacts.Net, EntrySide.Debit, "31");
        crooked.AddLine(PostingFacts.Vat, EntrySide.Debit, "2432");

        // No credit at all: the two debits balance against nothing.
        world.Rules.Add(crooked);

        Result<JournalEntryId?> posted = await world.PostSupplierInvoiceAsync();

        posted.IsSuccess.Should().BeTrue();
        world.Entries.Added.Should().BeEmpty();
        world.Entries.NumbersTaken.Should().Be(0);
        world.Facts.Added.Single().IsWaiting.Should().BeTrue();
    }

    /// <summary>A dismissed fact is not posted by a later run of the waiting list.</summary>
    [Fact]
    public async Task A_dismissed_fact_is_not_posted()
    {
        World world = World.WithNoRules();
        await world.PostSupplierInvoiceAsync();

        FactPosting fact = world.Facts.Added.Single();
        fact.Dismiss("Entered by hand in the old system.").IsSuccess.Should().BeTrue();

        world.Rules.Add(World.SupplierRule());

        Result retried = await world.Poster.TryPostAsync(fact);

        retried.IsFailure.Should().BeTrue();
        retried.Error.Code.Should().Be("finance.posting.was_dismissed");
        world.Entries.Added.Should().BeEmpty();

        // And it comes back when somebody decides it should.
        fact.Reinstate().IsSuccess.Should().BeTrue();
        (await world.Poster.TryPostAsync(fact)).IsSuccess.Should().BeTrue();
    }

    private sealed class World
    {
        private World(FakeRules rules)
        {
            Rules = rules;
            Poster = new LedgerPoster(
                Facts, rules, Accounts, Entries, Periods, UnitOfWork, new FixedClock());
        }

        public FakeFacts Facts { get; } = new();

        public FakeRules Rules { get; }

        public FakeAccounts Accounts { get; } = new();

        public FakeEntries Entries { get; } = new();

        public FakePeriods Periods { get; } = new();

        public FakeUnitOfWork UnitOfWork { get; } = new();

        public LedgerPoster Poster { get; }

        public static World WithNoRules() => new(new FakeRules());

        public static World WithSupplierRule()
        {
            var rules = new FakeRules();
            rules.Add(SupplierRule());

            return new World(rules);
        }

        public static PostingRule SupplierRule()
        {
            PostingRule rule = PostingRule.Define(
                PostingFacts.SupplierInvoiceSettled,
                "Supplier invoice",
                JournalSource.Purchases).Value;

            rule.AddLine(PostingFacts.Net, EntrySide.Debit, "31");
            rule.AddLine(PostingFacts.Vat, EntrySide.Debit, "2432");
            rule.AddLine(PostingFacts.Gross, EntrySide.Credit, "221");

            return rule;
        }

        public Task<Result<JournalEntryId?>> PostSupplierInvoiceAsync() =>
            Poster.PostFactAsync(
                PostingFacts.SupplierInvoiceSettled,
                "BP/FA 1/2026",
                InMarch,
                "Supplier invoice BP/FA 1/2026",
                new Dictionary<string, Money>(StringComparer.Ordinal)
                {
                    [PostingFacts.Net] = Money.Of(662.40m, Currency.Eur),
                    [PostingFacts.Vat] = Money.Of(152.35m, Currency.Eur),
                    [PostingFacts.Gross] = Money.Of(814.75m, Currency.Eur),
                    [PostingFacts.Rebate] = Money.Zero(Currency.Eur),
                });
    }

    private sealed class FakeFacts : IFactPostingRepository
    {
        public List<FactPosting> Added { get; } = [];

        public Task<FactPosting?> GetByIdAsync(
            FactPostingId id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Added.Find(posting => posting.Id == id));

        public Task<bool> ExistsAsync(
            FactPostingId id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Added.Exists(posting => posting.Id == id));

        public Task<FactPosting?> GetForAsync(
            string factType, string reference, CancellationToken cancellationToken = default) =>
            Task.FromResult(Added.Find(posting =>
                posting.FactType == factType && posting.Reference == reference));

        public Task<IReadOnlyList<FactPosting>> GetWaitingAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<FactPosting>>(
                [.. Added.Where(posting => posting.IsWaiting)]);

        public Task<int> CountWaitingAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Added.Count(posting => posting.IsWaiting));

        public void Add(FactPosting aggregate) => Added.Add(aggregate);

        public void Remove(FactPosting aggregate) => Added.Remove(aggregate);
    }

    private sealed class FakeRules : IPostingRuleRepository
    {
        private readonly List<PostingRule> _rules = [];

        public void Add(PostingRule aggregate) => _rules.Add(aggregate);

        public Task<PostingRule?> GetByIdAsync(
            PostingRuleId id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_rules.Find(rule => rule.Id == id));

        public Task<bool> ExistsAsync(
            PostingRuleId id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_rules.Exists(rule => rule.Id == id));

        public Task<PostingRule?> GetForFactAsync(
            string factType, CancellationToken cancellationToken = default) =>
            Task.FromResult(_rules.Find(rule => rule.FactType == factType));

        public Task<bool> IsMappedAsync(
            string factType, CancellationToken cancellationToken = default) =>
            Task.FromResult(_rules.Exists(rule => rule.FactType == factType));

        public Task<IReadOnlyList<PostingRule>> GetAllAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PostingRule>>(_rules);

        public void Remove(PostingRule aggregate) => _rules.Remove(aggregate);
    }

    private sealed class FakeAccounts : IAccountRepository
    {
        private readonly Dictionary<string, Account> _accounts =
            new(StringComparer.Ordinal)
            {
                ["31"] = Posting("31", "Inventory", AccountType.Asset),
                ["2432"] = Posting("2432", "VAT deductible", AccountType.Asset),
                ["221"] = Posting("221", "Trade payables", AccountType.Liability),
            };

        public void Forget(string code) => _accounts.Remove(code);

        public Task<Account?> GetByIdAsync(
            AccountId id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_accounts.Values.FirstOrDefault(account => account.Id == id));

        public Task<bool> ExistsAsync(
            AccountId id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_accounts.Values.Any(account => account.Id == id));

        public Task<Account?> GetByCodeAsync(
            string code, CancellationToken cancellationToken = default) =>
            Task.FromResult(_accounts.GetValueOrDefault(code));

        public Task<bool> CodeExistsAsync(
            string code,
            AccountId? excluding = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_accounts.ContainsKey(code));

        public Task<IReadOnlyDictionary<string, Account>> GetByCodesAsync(
            IReadOnlyCollection<string> codes, CancellationToken cancellationToken = default)
        {
            var found = new Dictionary<string, Account>(StringComparer.Ordinal);

            foreach (string code in codes)
            {
                if (_accounts.TryGetValue(code, out Account? account))
                {
                    found[code] = account;
                }
            }

            return Task.FromResult<IReadOnlyDictionary<string, Account>>(found);
        }

        public Task<IReadOnlyList<Account>> GetAllAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Account>>([.. _accounts.Values]);

        public void Add(Account aggregate) => _accounts[aggregate.Code] = aggregate;

        public void Remove(Account aggregate) => _accounts.Remove(aggregate.Code);

        private static Account Posting(string code, string name, AccountType type) =>
            Account.Open(code, name, type).Value;
    }

    private sealed class FakeEntries : IJournalEntryRepository
    {
        public List<JournalEntry> Added { get; } = [];

        public int NumbersTaken { get; private set; }

        public Task<JournalEntry?> GetByIdAsync(
            JournalEntryId id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Added.Find(entry => entry.Id == id));

        public Task<bool> ExistsAsync(
            JournalEntryId id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Added.Exists(entry => entry.Id == id));

        public Task<JournalEntry?> GetByNumberAsync(
            string number, CancellationToken cancellationToken = default) =>
            Task.FromResult(Added.Find(entry => entry.Number == number));

        public Task<string> NextEntryNumberAsync(
            int year, CancellationToken cancellationToken = default)
        {
            NumbersTaken++;

            return Task.FromResult($"JE-{year}-{NumbersTaken:D5}");
        }

        public Task<IReadOnlyList<JournalEntry>> GetPostedBetweenAsync(
            DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<JournalEntry>>([.. Added]);

        public void Add(JournalEntry aggregate) => Added.Add(aggregate);

        public void Remove(JournalEntry aggregate) => Added.Remove(aggregate);
    }

    private sealed class FakePeriods : IAccountingPeriodRepository
    {
        private readonly List<AccountingPeriod> _periods = [];

        public void Close(int year, int month)
        {
            AccountingPeriod period = AccountingPeriod.Open(year, month).Value;
            period.Close(
                new DateTimeOffset(year, month, 28, 0, 0, 0, TimeSpan.Zero).AddMonths(1),
                new DateOnly(year, month, 1).AddMonths(1).AddDays(5),
                previousIsClosed: true);

            _periods.Add(period);
        }

        public Task<AccountingPeriod?> GetByIdAsync(
            AccountingPeriodId id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_periods.Find(period => period.Id == id));

        public Task<bool> ExistsAsync(
            AccountingPeriodId id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_periods.Exists(period => period.Id == id));

        public Task<AccountingPeriod?> GetForAsync(
            DateOnly on, CancellationToken cancellationToken = default) =>
            Task.FromResult(_periods.Find(period => period.Covers(on)));

        public Task<AccountingPeriod?> GetForAsync(
            int year, int month, CancellationToken cancellationToken = default) =>
            Task.FromResult(_periods.Find(
                period => period.Year == year && period.Month == month));

        public Task<bool> EveryEarlierIsClosedAsync(
            int year, int month, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> AnyLaterIsClosedAsync(
            int year, int month, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public void Add(AccountingPeriod aggregate) => _periods.Add(aggregate);

        public void Remove(AccountingPeriod aggregate) => _periods.Remove(aggregate);
    }

    private sealed class FakeUnitOfWork : IFinanceUnitOfWork
    {
        public int Saves { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            Saves++;

            return Task.FromResult(0);
        }
    }

    private sealed class FixedClock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => new(2026, 4, 2, 9, 0, 0, TimeSpan.Zero);

        public DateOnly TodayUtc => DateOnly.FromDateTime(UtcNow.UtcDateTime);
    }
}
