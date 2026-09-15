using AutoPartsErp.Modules.Finance.Domain.Ledger;
using AutoPartsErp.Modules.Finance.Domain.Ledger.Events;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Finance.Tests;

/// <summary>
/// The general ledger: a date, a reason, and lines that add up to nothing.
/// <para>
/// This is what the rest of the system has been waiting for. The cost of a sale is stamped on a
/// stock movement and posted nowhere; a shortfall written off in transit carries its value in an
/// event nobody consumes; a supplier's price variance corrects the shelf and leaves the part
/// already sold with nowhere to go. All three are one shape of gap — a real financial fact with no
/// account to land on — and these are the accounts.
/// </para>
/// </summary>
public sealed class JournalEntryTests
{
    private static readonly DateOnly Entered = new(2026, 9, 30);
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 9, 0, 0, TimeSpan.Zero);

    private static Money Eur(decimal amount) => Money.Of(amount, Currency.Eur);

    /// <summary>The ordinary case: a supplier invoice split between stock and deductible VAT.</summary>
    [Fact]
    public void A_balanced_entry_posts_and_the_two_sides_agree()
    {
        JournalEntry entry = Draft();
        Account stock = Posting("31", "Inventory", AccountType.Asset);
        Account vat = Posting("2432", "VAT deductible", AccountType.Asset);
        Account supplier = Posting("221", "Trade payables", AccountType.Liability);

        entry.AddLine(stock, EntrySide.Debit, Eur(662.40m)).IsSuccess.Should().BeTrue();
        entry.AddLine(vat, EntrySide.Debit, Eur(152.35m)).IsSuccess.Should().BeTrue();
        entry.AddLine(supplier, EntrySide.Credit, Eur(814.75m)).IsSuccess.Should().BeTrue();

        entry.TotalDebits.Amount.Should().Be(814.75m);
        entry.TotalCredits.Amount.Should().Be(814.75m);
        entry.IsBalanced.Should().BeTrue();

        entry.Post(Now).IsSuccess.Should().BeTrue();

        entry.Status.Should().Be(JournalEntryStatus.Posted);
        entry.PostedAtUtc.Should().Be(Now);

        JournalEntryPostedDomainEvent posted =
            entry.DomainEvents.OfType<JournalEntryPostedDomainEvent>().Should().ContainSingle().Subject;

        posted.Total.Should().Be(814.75m);
        posted.EntryDate.Should().Be(Entered);
    }

    /// <summary>
    /// An unbalanced ledger is one that no longer proves anything, and every report built on it
    /// inherits the doubt. One cent is enough.
    /// </summary>
    [Fact]
    public void An_entry_a_cent_out_does_not_post_and_says_both_totals()
    {
        JournalEntry entry = Draft();

        entry.AddLine(Posting("31", "Inventory", AccountType.Asset), EntrySide.Debit, Eur(100.00m));
        entry.AddLine(
            Posting("221", "Trade payables", AccountType.Liability), EntrySide.Credit, Eur(99.99m));

        Result posted = entry.Post(Now);

        posted.Error.Code.Should().Be("finance.journal.out_of_balance");
        posted.Error.Description.Should().Contain("100.00").And.Contain("99.99");
        entry.Status.Should().Be(JournalEntryStatus.Draft);
    }

    /// <summary>
    /// Debits balancing debits is arithmetic, not bookkeeping. Caught here rather than by somebody
    /// reading a trial balance in April.
    /// </summary>
    [Fact]
    public void An_entry_with_only_one_side_is_refused()
    {
        JournalEntry entry = Draft();

        entry.AddLine(Posting("31", "Inventory", AccountType.Asset), EntrySide.Debit, Eur(50.00m));
        entry.AddLine(Posting("32", "Goods", AccountType.Asset), EntrySide.Debit, Eur(50.00m));

        entry.Post(Now).Error.Code.Should().Be("finance.journal.one_sided");
    }

    /// <summary>
    /// A balance that is partly its own postings and partly the total of its children is one
    /// nobody can take apart again.
    /// </summary>
    [Fact]
    public void Nothing_posts_to_a_group_account()
    {
        JournalEntry entry = Draft();
        Account group = Account.Open("2", "Payables", AccountType.Liability, allowsPosting: false).Value;

        entry.AddLine(group, EntrySide.Credit, Eur(100.00m))
            .Error.Code.Should().Be("finance.journal.account_is_a_group");
    }

    /// <summary>What is already on a closed account stays; nothing new lands there.</summary>
    [Fact]
    public void Nothing_posts_to_an_account_no_longer_in_use()
    {
        JournalEntry entry = Draft();
        Account account = Posting("31", "Inventory", AccountType.Asset);
        account.Deactivate();

        entry.AddLine(account, EntrySide.Debit, Eur(100.00m))
            .Error.Code.Should().Be("finance.journal.account_inactive");

        account.Reactivate();
        entry.AddLine(account, EntrySide.Debit, Eur(100.00m)).IsSuccess.Should().BeTrue();
    }

    /// <summary>
    /// A credit written as a negative debit is the same fact in a form the other half of the
    /// ledger cannot see, and once one is in the data every sum has to know to look for it.
    /// </summary>
    [Fact]
    public void A_line_is_positive_and_the_side_carries_the_direction()
    {
        JournalEntry entry = Draft();
        Account account = Posting("31", "Inventory", AccountType.Asset);

        entry.AddLine(account, EntrySide.Debit, Eur(-100.00m))
            .Error.Code.Should().Be("finance.journal.amount_not_positive");

        entry.AddLine(account, EntrySide.Debit, Eur(0m))
            .Error.Code.Should().Be("finance.journal.amount_not_positive");

        entry.AddLine(account, EntrySide.Unknown, Eur(100.00m))
            .Error.Code.Should().Be("finance.journal.side_required");
    }

    /// <summary>An entry that balanced across two currencies would not balance at all.</summary>
    [Fact]
    public void An_amount_in_another_currency_never_reaches_the_entry()
    {
        JournalEntry entry = Draft();

        entry.AddLine(
            Posting("31", "Inventory", AccountType.Asset),
            EntrySide.Debit,
            Money.Of(100.00m, Currency.Usd))
            .Error.Code.Should().Be("finance.journal.currency_mismatch");

        entry.Lines.Should().BeEmpty();
    }

    /// <summary>
    /// A posted entry never changes. The month somebody has already reported has to keep saying
    /// what it said, and an edit would silently restate it.
    /// </summary>
    [Fact]
    public void A_posted_entry_takes_no_more_lines_and_posts_no_twice()
    {
        JournalEntry entry = Balanced();
        entry.Post(Now);

        entry.AddLine(Posting("31", "Inventory", AccountType.Asset), EntrySide.Debit, Eur(1.00m))
            .Error.Code.Should().Be("finance.journal.already_posted");

        entry.Post(Now).Error.Code.Should().Be("finance.journal.already_posted");

        entry.RemoveLine(entry.Lines[0].Id)
            .Error.Code.Should().Be("finance.journal.already_posted");
    }

    /// <summary>
    /// The only way to undo a posting. The original keeps saying what it said, and the two
    /// together net to nothing.
    /// </summary>
    [Fact]
    public void A_reversal_mirrors_every_line_onto_the_other_side()
    {
        JournalEntry original = Balanced();
        original.Post(Now);

        JournalEntry reversal = original
            .BuildReversal("JE-2026-00002", Entered.AddDays(2), "Posted to the wrong period").Value;

        reversal.ReversesId.Should().Be(original.Id);
        reversal.Description.Should().Contain("Reversal of").And.Contain("wrong period");
        reversal.Lines.Should().HaveCount(original.Lines.Count);

        reversal.TotalDebits.Amount.Should().Be(original.TotalCredits.Amount);
        reversal.TotalCredits.Amount.Should().Be(original.TotalDebits.Amount);

        reversal.Post(Now).IsSuccess.Should().BeTrue();
    }

    /// <summary>A reversal would otherwise change a period that closed before the mistake was made.</summary>
    [Fact]
    public void A_reversal_cannot_be_dated_before_what_it_reverses()
    {
        JournalEntry original = Balanced();
        original.Post(Now);

        original.BuildReversal("JE-2026-00002", Entered.AddDays(-1), "Wrong period")
            .Error.Code.Should().Be("finance.journal.reversal_before_original");

        original.BuildReversal("JE-2026-00002", Entered, null)
            .Error.Code.Should().Be("finance.journal.reversal_reason_required");
    }

    /// <summary>A draft has nothing to reverse. It is deleted.</summary>
    [Fact]
    public void A_draft_cannot_be_reversed()
    {
        Balanced().BuildReversal("JE-2026-00002", Entered, "Because")
            .Error.Code.Should().Be("finance.journal.not_posted");
    }

    /// <summary>
    /// The side an account grows on is a fact about its kind, not a field. Storing it would let
    /// the two disagree, and an asset growing on the credit side makes every report built on it
    /// wrong in a direction nobody checks.
    /// </summary>
    [Fact]
    public void The_side_an_account_grows_on_comes_from_what_it_measures()
    {
        Posting("31", "Inventory", AccountType.Asset).NormalSide.Should().Be(EntrySide.Debit);
        Posting("62", "Supplies", AccountType.Expense).NormalSide.Should().Be(EntrySide.Debit);
        Posting("221", "Payables", AccountType.Liability).NormalSide.Should().Be(EntrySide.Credit);
        Posting("71", "Sales", AccountType.Income).NormalSide.Should().Be(EntrySide.Credit);
        Posting("51", "Capital", AccountType.Equity).NormalSide.Should().Be(EntrySide.Credit);
    }

    /// <summary>
    /// A trial balance of forty lines all reading "Adjustment" is one nobody can audit, and the
    /// side an account grows on is something nothing downstream can guess.
    /// </summary>
    [Fact]
    public void An_entry_needs_a_number_a_source_and_a_reason()
    {
        Drafting(number: " ").Error.Code.Should().Be("finance.journal.number_required");
        Drafting(source: JournalSource.Unknown).Error.Code.Should().Be("finance.journal.source_required");
        Drafting(description: "  ").Error.Code.Should().Be("finance.journal.description_required");

        Account.Open("31", "Inventory", AccountType.Unknown)
            .Error.Code.Should().Be("finance.account.type_required");
    }

    private static Result<JournalEntry> Drafting(
        string? number = "JE-2026-00001",
        JournalSource source = JournalSource.Purchases,
        string? description = "Supplier invoice FT 2026/14872") =>
        JournalEntry.Draft(number, Entered, source, description, Currency.Eur, "FT 2026/14872");

    private static JournalEntry Draft() => Drafting().Value;

    /// <summary>A two-line entry that balances, for the tests that are about something else.</summary>
    private static JournalEntry Balanced()
    {
        JournalEntry entry = Draft();

        entry.AddLine(Posting("31", "Inventory", AccountType.Asset), EntrySide.Debit, Eur(100.00m));
        entry.AddLine(
            Posting("221", "Trade payables", AccountType.Liability), EntrySide.Credit, Eur(100.00m));

        return entry;
    }

    private static Account Posting(string code, string name, AccountType type) =>
        Account.Open(code, name, type).Value;
}
