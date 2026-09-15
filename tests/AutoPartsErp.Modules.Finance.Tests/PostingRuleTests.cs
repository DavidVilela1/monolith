using AutoPartsErp.Modules.Finance.Domain;
using AutoPartsErp.Modules.Finance.Domain.Ledger;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Finance.Tests;

/// <summary>
/// The mapping from a fact to the account codes it lands on.
/// <para>
/// The seam everything upstream has been waiting for. A sale was invoiced, stock left at a cost, a
/// supplier charged more than the receipt was booked at — each of those is a real financial fact
/// that has been landing nowhere, because where it belongs is the accountant's decision and not a
/// thing a programmer can guess. A rule is how that decision gets into the system.
/// </para>
/// </summary>
public sealed class PostingRuleTests
{
    private static Money Eur(decimal amount) => Money.Of(amount, Currency.Eur);

    /// <summary>The ordinary case: a supplier invoice split between stock and deductible VAT.</summary>
    [Fact]
    public void A_rule_turns_the_amounts_of_a_fact_into_the_lines_of_an_entry()
    {
        PostingRule rule = SupplierInvoice();

        Result<IReadOnlyList<PostingInstruction>> applied = rule.Apply(
            new Dictionary<string, Money>(StringComparer.Ordinal)
            {
                [PostingFacts.Net] = Eur(662.40m),
                [PostingFacts.Vat] = Eur(152.35m),
                [PostingFacts.Gross] = Eur(814.75m),
            });

        applied.IsSuccess.Should().BeTrue();
        applied.Value.Should().HaveCount(3);

        applied.Value[0].AccountCode.Should().Be("31");
        applied.Value[0].Side.Should().Be(EntrySide.Debit);
        applied.Value[0].Amount.Amount.Should().Be(662.40m);

        applied.Value[1].AccountCode.Should().Be("2432");
        applied.Value[1].Side.Should().Be(EntrySide.Debit);

        applied.Value[2].AccountCode.Should().Be("221");
        applied.Value[2].Side.Should().Be(EntrySide.Credit);
        applied.Value[2].Amount.Amount.Should().Be(814.75m);

        // The two sides meet, which is what the entry will insist on when it is posted.
        applied.Value
            .Where(instruction => instruction.Side == EntrySide.Debit)
            .Sum(instruction => instruction.Amount.Amount)
            .Should().Be(814.75m);
    }

    /// <summary>
    /// A negative amount flips its side and posts the absolute value. The rest of the ledger
    /// insists a line is positive and the side carries the direction, and a price variance can
    /// honestly go either way — so the rule is written for one direction and the arithmetic takes
    /// care of the other.
    /// </summary>
    [Fact]
    public void A_negative_amount_lands_on_the_other_side()
    {
        PostingRule rule = PriceVariance();

        Result<IReadOnlyList<PostingInstruction>> up = rule.Apply(Value(Eur(45.20m)));
        Result<IReadOnlyList<PostingInstruction>> down = rule.Apply(Value(Eur(-45.20m)));

        up.Value[0].Side.Should().Be(EntrySide.Debit);
        up.Value[0].Amount.Amount.Should().Be(45.20m);
        up.Value[1].Side.Should().Be(EntrySide.Credit);

        down.Value[0].Side.Should().Be(EntrySide.Credit);
        down.Value[0].Amount.Amount.Should().Be(45.20m);
        down.Value[1].Side.Should().Be(EntrySide.Debit);
    }

    /// <summary>
    /// A zero amount posts nothing. An exempt sale carries no VAT, and a line reading "0,00 to VAT
    /// payable" is noise on every exempt document the company ever issues.
    /// </summary>
    [Fact]
    public void A_zero_amount_produces_no_line()
    {
        PostingRule rule = SupplierInvoice();

        Result<IReadOnlyList<PostingInstruction>> applied = rule.Apply(
            new Dictionary<string, Money>(StringComparer.Ordinal)
            {
                [PostingFacts.Net] = Eur(300m),
                [PostingFacts.Vat] = Eur(0m),
                [PostingFacts.Gross] = Eur(300m),
            });

        applied.Value.Should().HaveCount(2);
        applied.Value.Should().NotContain(instruction => instruction.AccountCode == "2432");
    }

    /// <summary>A fact worth nothing at all is not an entry.</summary>
    [Fact]
    public void A_fact_whose_every_amount_is_zero_posts_nothing()
    {
        PostingRule rule = PriceVariance();

        Result<IReadOnlyList<PostingInstruction>> applied = rule.Apply(Value(Eur(0m)));

        applied.IsFailure.Should().BeTrue();
        applied.Error.Code.Should().Be("finance.posting.nothing_to_post");
    }

    /// <summary>
    /// A fact arriving without an amount its rule names posts nothing at all, rather than the half
    /// of itself that did arrive.
    /// </summary>
    [Fact]
    public void A_missing_amount_stops_the_whole_entry()
    {
        PostingRule rule = SupplierInvoice();

        Result<IReadOnlyList<PostingInstruction>> applied = rule.Apply(
            new Dictionary<string, Money>(StringComparer.Ordinal)
            {
                [PostingFacts.Net] = Eur(662.40m),
                [PostingFacts.Gross] = Eur(814.75m),
            });

        applied.IsFailure.Should().BeTrue();
        applied.Error.Code.Should().Be("finance.posting.amount_missing");
    }

    /// <summary>An entry that balanced across two currencies would not balance at all.</summary>
    [Fact]
    public void Amounts_in_two_currencies_are_refused()
    {
        PostingRule rule = SupplierInvoice();

        Result<IReadOnlyList<PostingInstruction>> applied = rule.Apply(
            new Dictionary<string, Money>(StringComparer.Ordinal)
            {
                [PostingFacts.Net] = Eur(662.40m),
                [PostingFacts.Vat] = Money.Of(152.35m, Currency.Usd),
                [PostingFacts.Gross] = Eur(814.75m),
            });

        applied.IsFailure.Should().BeTrue();
        applied.Error.Code.Should().Be("finance.posting.mixed_currencies");
    }

    /// <summary>A rule taken out of use stops posting, and says so rather than posting nothing.</summary>
    [Fact]
    public void An_inactive_rule_refuses_instead_of_posting_nothing()
    {
        PostingRule rule = PriceVariance();
        rule.Deactivate();

        Result<IReadOnlyList<PostingInstruction>> applied = rule.Apply(Value(Eur(45.20m)));

        applied.IsFailure.Should().BeTrue();
        applied.Error.Code.Should().Be("finance.posting.rule_inactive");

        rule.Activate();
        rule.Apply(Value(Eur(45.20m))).IsSuccess.Should().BeTrue();
    }

    /// <summary>A rule that names a fact and sends it nowhere.</summary>
    [Fact]
    public void A_rule_with_no_lines_refuses()
    {
        PostingRule rule = PostingRule
            .Define(PostingFacts.CostOfSale, "Cost of sale", JournalSource.Inventory).Value;

        Result<IReadOnlyList<PostingInstruction>> applied = rule.Apply(Value(Eur(100m)));

        applied.IsFailure.Should().BeTrue();
        applied.Error.Code.Should().Be("finance.posting.rule_has_no_lines");
    }

    /// <summary>Mapping a fact this system never raises would look configured and post nothing.</summary>
    [Fact]
    public void A_fact_nobody_raises_cannot_be_mapped()
    {
        Result<PostingRule> rule = PostingRule
            .Define("sales.somebody_made_this_up", "Whatever", JournalSource.Manual);

        rule.IsFailure.Should().BeTrue();
        rule.Error.Code.Should().Be("finance.posting.unknown_fact");
    }

    /// <summary>
    /// The catalogue is what catches the typo at configuration time. A line naming an amount the
    /// fact does not carry would be a rule that silently posts nothing.
    /// </summary>
    [Fact]
    public void A_line_naming_an_amount_the_fact_does_not_carry_is_refused()
    {
        PostingRule rule = PostingRule
            .Define(PostingFacts.CostOfSale, "Cost of sale", JournalSource.Inventory).Value;

        Result<PostingRuleLineId> added = rule.AddLine("tax", EntrySide.Debit, "2432");

        added.IsFailure.Should().BeTrue();
        added.Error.Code.Should().Be("finance.posting.unknown_amount");

        // And the message says what it does carry, so nobody has to go and read the source.
        added.Error.Description.Should().Contain(PostingFacts.Value);
    }

    /// <summary>The same amount on the same account and side twice would not balance.</summary>
    [Fact]
    public void The_same_line_twice_is_refused()
    {
        PostingRule rule = PriceVariance();

        Result<PostingRuleLineId> again = rule.AddLine(
            PostingFacts.Value, EntrySide.Debit, "31");

        again.IsFailure.Should().BeTrue();
        again.Error.Code.Should().Be("finance.posting.duplicate_line");
    }

    /// <summary>The same amount split across two accounts is a split, and is allowed.</summary>
    [Fact]
    public void The_same_amount_may_land_on_two_different_accounts()
    {
        PostingRule rule = PriceVariance();

        rule.AddLine(PostingFacts.Value, EntrySide.Debit, "312").IsSuccess.Should().BeTrue();
        rule.Lines.Should().HaveCount(3);
    }

    /// <summary>Account codes are folded to upper case, the way the chart holds them.</summary>
    [Fact]
    public void An_account_code_is_stored_the_way_the_chart_holds_it()
    {
        PostingRule rule = PostingRule
            .Define(PostingFacts.CostOfSale, "Cost of sale", JournalSource.Inventory).Value;

        rule.AddLine(PostingFacts.Value, EntrySide.Debit, "  61a  ").IsSuccess.Should().BeTrue();

        rule.Lines.Single().AccountCode.Should().Be("61A");
    }

    /// <summary>A removed line stops being posted.</summary>
    [Fact]
    public void A_removed_line_stops_being_posted()
    {
        PostingRule rule = PriceVariance();
        PostingRuleLineId first = rule.Lines.First().Id;

        rule.RemoveLine(first).IsSuccess.Should().BeTrue();
        rule.Lines.Should().HaveCount(1);
        rule.RemoveLine(first).Error.Code.Should().Be("finance.posting.line_not_found");
    }

    /// <summary>Every fact in the catalogue carries at least one amount to post.</summary>
    [Fact]
    public void Every_fact_in_the_catalogue_carries_something()
    {
        PostingFacts.All.Should().NotBeEmpty();

        foreach (string fact in PostingFacts.All)
        {
            PostingFacts.IsKnown(fact).Should().BeTrue();
            PostingFacts.AmountKeysFor(fact).Should().NotBeEmpty();
        }
    }

    /// <summary>A fact nobody recognizes carries nothing, so a rule for it maps nothing.</summary>
    [Fact]
    public void An_unknown_fact_carries_nothing()
    {
        PostingFacts.IsKnown("nonsense").Should().BeFalse();
        PostingFacts.IsKnown(null).Should().BeFalse();
        PostingFacts.AmountKeysFor("nonsense").Should().BeEmpty();
        PostingFacts.Carries("nonsense", PostingFacts.Net).Should().BeFalse();
    }

    private static Dictionary<string, Money> Value(Money amount) =>
        new(StringComparer.Ordinal) { [PostingFacts.Value] = amount };

    private static PostingRule SupplierInvoice()
    {
        PostingRule rule = PostingRule.Define(
            PostingFacts.SupplierInvoiceSettled,
            "Supplier invoice",
            JournalSource.Purchases).Value;

        rule.AddLine(PostingFacts.Net, EntrySide.Debit, "31").IsSuccess.Should().BeTrue();
        rule.AddLine(PostingFacts.Vat, EntrySide.Debit, "2432").IsSuccess.Should().BeTrue();
        rule.AddLine(PostingFacts.Gross, EntrySide.Credit, "221").IsSuccess.Should().BeTrue();

        return rule;
    }

    private static PostingRule PriceVariance()
    {
        PostingRule rule = PostingRule.Define(
            PostingFacts.PriceVariance,
            "Supplier price variance",
            JournalSource.Inventory).Value;

        rule.AddLine(PostingFacts.Value, EntrySide.Debit, "31").IsSuccess.Should().BeTrue();
        rule.AddLine(PostingFacts.Value, EntrySide.Credit, "221").IsSuccess.Should().BeTrue();

        return rule;
    }
}
