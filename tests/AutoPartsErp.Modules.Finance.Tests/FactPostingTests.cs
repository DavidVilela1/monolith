using AutoPartsErp.Modules.Finance.Domain;
using AutoPartsErp.Modules.Finance.Domain.Ledger;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Finance.Tests;

/// <summary>
/// The record of every fact the ledger was asked to post.
/// <para>
/// It answers two questions with one row: "has this document already been dealt with?", which is
/// what keeps the handlers idempotent when the outbox redelivers, and "what happened and never
/// reached the books?", which is what stops the ledger being quietly incomplete.
/// </para>
/// </summary>
public sealed class FactPostingTests
{
    private static readonly DateOnly InMarch = new(2026, 3, 18);
    private static readonly DateTimeOffset Now = new(2026, 4, 2, 9, 0, 0, TimeSpan.Zero);

    private static Money Eur(decimal amount) => Money.Of(amount, Currency.Eur);

    /// <summary>A recorded fact starts on the waiting list with everything it carried.</summary>
    [Fact]
    public void A_recorded_fact_waits_and_keeps_its_amounts()
    {
        FactPosting fact = Recorded();

        fact.FactType.Should().Be(PostingFacts.SupplierInvoiceSettled);
        fact.Reference.Should().Be("BP/FA 1/2026");
        fact.OccurredOn.Should().Be(InMarch);
        fact.Status.Should().Be(PostingStatus.Waiting);
        fact.IsWaiting.Should().BeTrue();
        fact.JournalEntryId.Should().BeNull();
        fact.CurrencyCode.Should().Be("EUR");

        IReadOnlyDictionary<string, Money> amounts = fact.AmountsByKey();
        amounts.Should().HaveCount(3);
        amounts[PostingFacts.Net].Amount.Should().Be(662.40m);
        amounts[PostingFacts.Gross].Amount.Should().Be(814.75m);
    }

    /// <summary>A fact this system never raises is not a fact to record.</summary>
    [Fact]
    public void A_fact_nobody_raises_is_refused()
    {
        Result<FactPosting> fact = FactPosting.Record(
            "sales.invented", "X", InMarch, "Whatever", Amounts());

        fact.IsFailure.Should().BeTrue();
        fact.Error.Code.Should().Be("finance.posting.unknown_fact");
    }

    /// <summary>
    /// The reference is half the key that stops the same sale being posted twice, so there is no
    /// recording a fact without one.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_fact_without_a_reference_is_refused(string? reference)
    {
        Result<FactPosting> fact = FactPosting.Record(
            PostingFacts.SupplierInvoiceSettled, reference, InMarch, "Whatever", Amounts());

        fact.IsFailure.Should().BeTrue();
        fact.Error.Code.Should().Be("finance.posting.reference_required");
    }

    /// <summary>A fact carrying nothing is not a fact to post.</summary>
    [Fact]
    public void A_fact_carrying_nothing_is_refused()
    {
        Result<FactPosting> fact = FactPosting.Record(
            PostingFacts.SupplierInvoiceSettled,
            "BP/FA 1/2026",
            InMarch,
            "Whatever",
            new Dictionary<string, Money>(StringComparer.Ordinal));

        fact.IsFailure.Should().BeTrue();
        fact.Error.Code.Should().Be("finance.posting.no_amounts");
    }

    /// <summary>Amounts in two currencies could never become one balanced entry.</summary>
    [Fact]
    public void Amounts_in_two_currencies_are_refused()
    {
        Result<FactPosting> fact = FactPosting.Record(
            PostingFacts.SupplierInvoiceSettled,
            "BP/FA 1/2026",
            InMarch,
            "Whatever",
            new Dictionary<string, Money>(StringComparer.Ordinal)
            {
                [PostingFacts.Net] = Eur(662.40m),
                [PostingFacts.Gross] = Money.Of(814.75m, Currency.Usd),
            });

        fact.IsFailure.Should().BeTrue();
        fact.Error.Code.Should().Be("finance.posting.mixed_currencies");
    }

    /// <summary>Once it is in the ledger it carries the entry it produced.</summary>
    [Fact]
    public void A_posted_fact_points_at_its_entry()
    {
        FactPosting fact = Recorded();
        var entryId = JournalEntryId.New();

        fact.Posted(entryId, Now).IsSuccess.Should().BeTrue();

        fact.Status.Should().Be(PostingStatus.Posted);
        fact.IsWaiting.Should().BeFalse();
        fact.JournalEntryId.Should().Be(entryId);
        fact.PostedAtUtc.Should().Be(Now);
    }

    /// <summary>Posting it twice would carry the same sale into the books twice.</summary>
    [Fact]
    public void A_posted_fact_cannot_be_posted_again()
    {
        FactPosting fact = Recorded();
        fact.Posted(JournalEntryId.New(), Now);

        Result again = fact.Posted(JournalEntryId.New(), Now);

        again.IsFailure.Should().BeTrue();
        again.Error.Code.Should().Be("finance.posting.already_posted");
    }

    /// <summary>
    /// The reason it waited survives the posting. "This waited three weeks because nothing mapped
    /// it" is the sentence that explains a late entry.
    /// </summary>
    [Fact]
    public void The_reason_it_waited_survives_being_posted()
    {
        FactPosting fact = Recorded();
        fact.CouldNotPost("Nothing is mapped for purchasing.supplier_invoice_settled.");

        fact.Posted(JournalEntryId.New(), Now).IsSuccess.Should().BeTrue();

        fact.Reason.Should().Be("Nothing is mapped for purchasing.supplier_invoice_settled.");
    }

    /// <summary>A reason longer than the column is cut rather than refused — it is a note.</summary>
    [Fact]
    public void A_very_long_reason_is_kept_as_much_as_fits()
    {
        FactPosting fact = Recorded();

        fact.CouldNotPost(new string('x', FactPosting.MaxReasonLength + 200));

        fact.Reason.Should().HaveLength(FactPosting.MaxReasonLength);
    }

    /// <summary>
    /// Dismissing is not deleting: the row stays with the sentence, because "why is there no entry
    /// for 4471?" has to have an answer.
    /// </summary>
    [Fact]
    public void A_dismissed_fact_keeps_its_reason()
    {
        FactPosting fact = Recorded();

        Result dismissed = fact.Dismiss("  Entered by hand in the old system.  ");

        dismissed.IsSuccess.Should().BeTrue();
        fact.Status.Should().Be(PostingStatus.Dismissed);
        fact.IsWaiting.Should().BeFalse();
        fact.Reason.Should().Be("Entered by hand in the old system.");
    }

    /// <summary>Taking something off the list without saying why explains nothing later.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Dismissing_without_a_reason_is_refused(string? reason)
    {
        FactPosting fact = Recorded();

        Result dismissed = fact.Dismiss(reason);

        dismissed.IsFailure.Should().BeTrue();
        dismissed.Error.Code.Should().Be("finance.posting.dismiss_reason_required");
        fact.IsWaiting.Should().BeTrue();
    }

    /// <summary>What is already in the ledger cannot be waved away.</summary>
    [Fact]
    public void A_posted_fact_cannot_be_dismissed()
    {
        FactPosting fact = Recorded();
        fact.Posted(JournalEntryId.New(), Now);

        Result dismissed = fact.Dismiss("Changed my mind.");

        dismissed.IsFailure.Should().BeTrue();
        dismissed.Error.Code.Should().Be("finance.posting.already_posted");
    }

    /// <summary>A dismissed fact comes back when somebody decides it should.</summary>
    [Fact]
    public void A_dismissed_fact_can_be_put_back_on_the_list()
    {
        FactPosting fact = Recorded();
        fact.Dismiss("Mistake.");

        fact.Reinstate().IsSuccess.Should().BeTrue();
        fact.IsWaiting.Should().BeTrue();

        fact.Reinstate().Error.Code.Should().Be("finance.posting.not_dismissed");
    }

    /// <summary>A dismissed fact is not posted by a later run of the list.</summary>
    [Fact]
    public void A_dismissed_fact_cannot_be_posted()
    {
        FactPosting fact = Recorded();
        fact.Dismiss("Handled elsewhere.");

        Result posted = fact.Posted(JournalEntryId.New(), Now);

        posted.IsFailure.Should().BeTrue();
        posted.Error.Code.Should().Be("finance.posting.was_dismissed");
    }

    private static Dictionary<string, Money> Amounts() =>
        new(StringComparer.Ordinal)
        {
            [PostingFacts.Net] = Eur(662.40m),
            [PostingFacts.Vat] = Eur(152.35m),
            [PostingFacts.Gross] = Eur(814.75m),
        };

    private static FactPosting Recorded() =>
        FactPosting.Record(
            PostingFacts.SupplierInvoiceSettled,
            "BP/FA 1/2026",
            InMarch,
            "Supplier invoice BP/FA 1/2026",
            Amounts()).Value;
}
