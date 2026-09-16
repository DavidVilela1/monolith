using AutoPartsErp.IntegrationEvents.Inventory;
using AutoPartsErp.Modules.Finance.Application.EventHandlers;
using AutoPartsErp.Modules.Finance.Application.Ledger;
using AutoPartsErp.Modules.Finance.Domain;
using AutoPartsErp.Modules.Finance.Domain.Ledger;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Finance.Tests;

/// <summary>
/// What reaches the ledger when stock moves.
/// <para>
/// The last three facts that had nowhere to go: the cost of a sale, a price variance, and stock
/// written off. Each of them was computed correctly by Inventory and kept there, because the event
/// that announced it carried a quantity and no money.
/// </para>
/// </summary>
public sealed class StockPostingTests
{
    private static readonly DateTimeOffset InMarch =
        new(2026, 3, 18, 14, 30, 0, TimeSpan.Zero);

    /// <summary>Stock leaving against a sales order is a cost of sale.</summary>
    [Theory]
    [InlineData("SalesOrder")]
    [InlineData("CounterSale")]
    public async Task Stock_issued_against_a_customer_document_posts_a_cost_of_sale(string type)
    {
        var poster = new FakePoster();

        await new PostCostOfSaleOnStockIssued(poster)
            .HandleAsync(Issued(type, 100.00m));

        Fact posted = poster.Facts.Single();
        posted.FactType.Should().Be(PostingFacts.CostOfSale);
        posted.OccurredOn.Should().Be(new DateOnly(2026, 3, 18));
        posted.Amounts[PostingFacts.Value].Amount.Should().Be(100.00m);
    }

    /// <summary>
    /// A transfer to the other branch is two shelves the company still owns. Posting it would book
    /// a cost of sale every time a van went across town.
    /// </summary>
    [Theory]
    [InlineData("StockTransfer")]
    [InlineData("SupplierReturn")]
    [InlineData("Unknown")]
    public async Task Stock_leaving_for_anything_else_posts_nothing(string type)
    {
        var poster = new FakePoster();

        await new PostCostOfSaleOnStockIssued(poster)
            .HandleAsync(Issued(type, 100.00m));

        poster.Facts.Should().BeEmpty();
    }

    /// <summary>A shelf carrying no value has nothing to post, and zero is not a fact.</summary>
    [Fact]
    public async Task An_issue_worth_nothing_posts_nothing()
    {
        var poster = new FakePoster();
        var handler = new PostCostOfSaleOnStockIssued(poster);

        await handler.HandleAsync(Issued("SalesOrder", null));
        await handler.HandleAsync(Issued("SalesOrder", 0m));

        poster.Facts.Should().BeEmpty();
    }

    /// <summary>
    /// A customer's return takes the cost back off, as the same fact with a negative value — which
    /// is what the rule's side-flipping is for. Without it the margin falls a little with every
    /// return and never recovers.
    /// </summary>
    [Fact]
    public async Task A_customer_return_reverses_the_cost_of_sale()
    {
        var poster = new FakePoster();

        await new ReverseCostOfSaleOnCustomerReturn(poster).HandleAsync(
            new StockReceivedIntegrationEvent(
                Guid.NewGuid(), Guid.NewGuid(), 1m, "RET-2026-00031", "CustomerReturn",
                Guid.NewGuid(), 25.00m, "EUR", Guid.NewGuid())
            { OccurredAtUtc = InMarch });

        Fact posted = poster.Facts.Single();
        posted.FactType.Should().Be(PostingFacts.CostOfSale);
        posted.Amounts[PostingFacts.Value].Amount.Should().Be(-25.00m);
    }

    /// <summary>Goods arriving from a supplier are a purchase, and the supplier's invoice posts it.</summary>
    [Fact]
    public async Task A_supplier_delivery_does_not_reverse_a_cost_of_sale()
    {
        var poster = new FakePoster();

        await new ReverseCostOfSaleOnCustomerReturn(poster).HandleAsync(
            new StockReceivedIntegrationEvent(
                Guid.NewGuid(), Guid.NewGuid(), 10m, "GRN-2026-00042", "GoodsReceipt",
                Guid.NewGuid(), 250.00m, "EUR", Guid.NewGuid())
            { OccurredAtUtc = InMarch });

        poster.Facts.Should().BeEmpty();
    }

    /// <summary>A count posts what it found, signed the way the quantity is.</summary>
    [Theory]
    [InlineData(-2, -50.00)]
    [InlineData(2, 50.00)]
    public async Task A_count_posts_what_it_found(decimal delta, decimal value)
    {
        var poster = new FakePoster();

        await new PostAdjustmentOnStockAdjusted(poster).HandleAsync(
            new StockAdjustedIntegrationEvent(
                Guid.NewGuid(), Guid.NewGuid(), delta, value, "EUR", "CNT-2026-0004",
                Guid.NewGuid(), Guid.NewGuid())
            { OccurredAtUtc = InMarch });

        Fact posted = poster.Facts.Single();
        posted.FactType.Should().Be(PostingFacts.StockAdjustment);
        posted.Amounts[PostingFacts.Value].Amount.Should().Be(value);
    }

    /// <summary>A price variance reaches the ledger, in whichever direction it went.</summary>
    [Theory]
    [InlineData(45.20)]
    [InlineData(-45.20)]
    public async Task A_price_variance_posts_the_difference(decimal difference)
    {
        var poster = new FakePoster();

        await new PostVarianceOnStockRevalued(poster).HandleAsync(
            new StockRevaluedIntegrationEvent(
                Guid.NewGuid(), Guid.NewGuid(), difference, "EUR", "FT 2026/14872",
                Guid.NewGuid(), Guid.NewGuid())
            { OccurredAtUtc = InMarch });

        Fact posted = poster.Facts.Single();
        posted.FactType.Should().Be(PostingFacts.PriceVariance);
        posted.Amounts[PostingFacts.Value].Amount.Should().Be(difference);
    }

    /// <summary>
    /// A transfer that arrived short is stock the company owned at breakfast and does not own at
    /// lunch. One transfer is one shrinkage, so its number alone is the key.
    /// </summary>
    [Fact]
    public async Task A_transfer_that_arrived_short_posts_the_shrinkage()
    {
        var poster = new FakePoster();

        await new PostShrinkageOnTransferClosedShort(poster).HandleAsync(
            new StockTransferClosedShortIntegrationEvent(
                Guid.NewGuid(), "TR-2026-00021", Guid.NewGuid(), Guid.NewGuid(),
                "Two boxes never arrived.", 138.80m, "EUR", Guid.NewGuid())
            { OccurredAtUtc = InMarch });

        Fact posted = poster.Facts.Single();
        posted.FactType.Should().Be(PostingFacts.StockShrinkage);
        posted.Reference.Should().Be("TR-2026-00021");
        posted.Amounts[PostingFacts.Value].Amount.Should().Be(138.80m);
        posted.Description.Should().Contain("never arrived");
    }

    /// <summary>
    /// Two issues of one part on one document are two facts. The movement is what tells them
    /// apart; the document number alone would have the second look like the first arriving again,
    /// and the record of facts would swallow it.
    /// </summary>
    [Fact]
    public async Task Two_issues_on_one_document_are_two_facts()
    {
        var poster = new FakePoster();
        var handler = new PostCostOfSaleOnStockIssued(poster);

        await handler.HandleAsync(Issued("SalesOrder", 75.00m));
        await handler.HandleAsync(Issued("SalesOrder", 50.00m));

        poster.Facts.Should().HaveCount(2);
        poster.Facts.Select(fact => fact.Reference).Should().OnlyHaveUniqueItems();

        // And both name the same document, so a person can still find them together.
        poster.Facts.Should().OnlyContain(fact => fact.Reference.StartsWith("SO-2026-01188"));
    }

    private static StockIssuedIntegrationEvent Issued(string referenceType, decimal? cost) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            4m,
            "SO-2026-01188",
            referenceType,
            Guid.NewGuid(),
            cost,
            "EUR",
            Guid.NewGuid())
        { OccurredAtUtc = InMarch };

    private sealed record Fact(
        string FactType,
        string Reference,
        DateOnly OccurredOn,
        string Description,
        IReadOnlyDictionary<string, Money> Amounts);

    private sealed class FakePoster : ILedgerPoster
    {
        public List<Fact> Facts { get; } = [];

        public Task<Result<JournalEntryId?>> PostFactAsync(
            string factType,
            string reference,
            DateOnly occurredOn,
            string description,
            IReadOnlyDictionary<string, Money> amounts,
            CancellationToken cancellationToken = default)
        {
            Facts.Add(new Fact(factType, reference, occurredOn, description, amounts));

            return Task.FromResult(Result.Success<JournalEntryId?>(null));
        }

        public Task<Result> TryPostAsync(
            FactPosting posting, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success());
    }
}
