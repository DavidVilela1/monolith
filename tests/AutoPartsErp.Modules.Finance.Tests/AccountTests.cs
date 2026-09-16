using AutoPartsErp.Modules.Finance.Domain.Ledger;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Finance.Tests;

/// <summary>
/// The chart of accounts, and the one fact every report built on it depends on.
/// </summary>
public sealed class AccountTests
{
    /// <summary>
    /// The side an account grows on is what makes a balance readable: a trial balance shows one
    /// column, and whether a figure on it means "more" or "less" comes from here.
    /// </summary>
    [Theory]
    [InlineData(AccountType.Asset, EntrySide.Debit)]
    [InlineData(AccountType.Expense, EntrySide.Debit)]
    [InlineData(AccountType.Liability, EntrySide.Credit)]
    [InlineData(AccountType.Equity, EntrySide.Credit)]
    [InlineData(AccountType.Income, EntrySide.Credit)]
    public void An_account_grows_on_the_side_its_kind_says(AccountType type, EntrySide side)
    {
        Account.NormalSideFor(type).Should().Be(side);
    }

    /// <summary>
    /// The account and the read side give the same answer, because it is the same method. The
    /// trial balance never loads an account and used to work this out for itself; a second copy is
    /// a copy that can disagree, and this one would disagree in a direction nobody checks.
    /// </summary>
    [Theory]
    [InlineData(AccountType.Asset)]
    [InlineData(AccountType.Liability)]
    [InlineData(AccountType.Equity)]
    [InlineData(AccountType.Income)]
    [InlineData(AccountType.Expense)]
    public void The_account_and_the_rule_agree(AccountType type)
    {
        Account account = Account.Open("1", "Whatever", type).Value;

        account.NormalSide.Should().Be(Account.NormalSideFor(type));
    }

    /// <summary>
    /// Every kind this system recognizes has an answer. A new one added without deciding its side
    /// would fall to the default and be wrong on every report at once, so the list is walked
    /// rather than spelled out.
    /// </summary>
    [Fact]
    public void Every_kind_of_account_has_a_side()
    {
        AccountType[] real =
        [
            .. Enum.GetValues<AccountType>().Where(type => type != AccountType.Unknown),
        ];

        real.Should().HaveCount(5);

        foreach (AccountType type in real)
        {
            Account.NormalSideFor(type).Should().NotBe(EntrySide.Unknown);
        }
    }

    /// <summary>A group account exists to be summed, and takes no entries of its own.</summary>
    [Fact]
    public void A_group_account_takes_no_postings()
    {
        Account group = Account
            .Open("2", "Trade payables", AccountType.Liability, allowsPosting: false).Value;

        group.AllowsPosting.Should().BeFalse();
        group.CanTakePostings.Should().BeFalse();
    }

    /// <summary>An account out of use stops taking entries and keeps everything it ever took.</summary>
    [Fact]
    public void An_inactive_account_takes_no_postings()
    {
        Account account = Account.Open("31", "Inventory", AccountType.Asset).Value;

        account.CanTakePostings.Should().BeTrue();

        account.Deactivate();

        account.IsActive.Should().BeFalse();
        account.CanTakePostings.Should().BeFalse();

        account.Reactivate();

        account.CanTakePostings.Should().BeTrue();
    }
}
