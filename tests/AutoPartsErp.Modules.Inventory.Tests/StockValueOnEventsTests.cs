using AutoPartsErp.Modules.Inventory.Domain;
using AutoPartsErp.Modules.Inventory.Domain.Stock;
using AutoPartsErp.Modules.Inventory.Domain.Stock.Events;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Inventory.Tests;

/// <summary>
/// What the stock events tell the rest of the system about money.
/// <para>
/// This module has always known what a movement was worth — the moving average is computed here
/// and stamped on the movement row — and it has always kept it to itself. The events carried a
/// quantity and a document number, so a general ledger listening to them could tell that stock had
/// left and not what it had cost. Cost of sale, shrinkage and price variance were all stuck behind
/// that one omission.
/// </para>
/// </summary>
public sealed class StockValueOnEventsTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 18, 9, 0, 0, TimeSpan.Zero);

    /// <summary>An issue says what left the shelf, what it cost, and against what kind of document.</summary>
    [Fact]
    public void An_issue_carries_the_cost_that_left_the_shelf()
    {
        StockItem item = Stocked(10, 250.00m);
        item.ClearDomainEvents();

        Result<StockMovement> issued = item.Issue(
            4m, Reference(ReferenceType.SalesOrder, "SO-2026-01188"), Now);

        issued.IsSuccess.Should().BeTrue();

        StockIssuedDomainEvent raised = item.DomainEvents
            .OfType<StockIssuedDomainEvent>()
            .Single();

        // Ten units cost 250,00, so four of them cost 100,00.
        raised.CostValue.Should().Be(100.00m);
        raised.CurrencyCode.Should().Be("EUR");
        raised.ReferenceType.Should().Be(nameof(ReferenceType.SalesOrder));
        raised.MovementId.Should().Be(issued.Value.Id);
        raised.Quantity.Should().Be(4m);
    }

    /// <summary>
    /// A transfer out is stock leaving too, and it says so — but it says which kind of document it
    /// left against, which is the whole point. Two shelves the company still owns is not a cost.
    /// </summary>
    [Fact]
    public void A_transfer_out_says_it_is_a_transfer()
    {
        StockItem item = Stocked(10, 250.00m);
        item.ClearDomainEvents();

        item.Dispatch(4m, Reference(ReferenceType.StockTransfer, "TR-2026-00021"), Now)
            .IsSuccess.Should().BeTrue();

        StockIssuedDomainEvent raised = item.DomainEvents
            .OfType<StockIssuedDomainEvent>()
            .Single();

        raised.ReferenceType.Should().Be(nameof(ReferenceType.StockTransfer));
        raised.CostValue.Should().Be(100.00m);
    }

    /// <summary>
    /// A count that found less carries a negative value. A consumer reading the magnitude alone
    /// would post a shrinkage as a windfall.
    /// </summary>
    [Fact]
    public void A_count_that_found_less_carries_a_negative_value()
    {
        StockItem item = Stocked(10, 250.00m);
        item.ClearDomainEvents();

        item.AdjustTo(8m, Reference(ReferenceType.StockCount, "CNT-2026-0004"), Now)
            .IsSuccess.Should().BeTrue();

        StockAdjustedDomainEvent raised = item.DomainEvents
            .OfType<StockAdjustedDomainEvent>()
            .Single();

        raised.Delta.Should().Be(-2m);
        raised.Value.Should().Be(-50.00m);
        raised.CurrencyCode.Should().Be("EUR");
    }

    /// <summary>And one that found more carries a positive one, at the same average.</summary>
    [Fact]
    public void A_count_that_found_more_carries_a_positive_value()
    {
        StockItem item = Stocked(10, 250.00m);
        item.ClearDomainEvents();

        item.AdjustTo(12m, Reference(ReferenceType.StockCount, "CNT-2026-0004"), Now)
            .IsSuccess.Should().BeTrue();

        StockAdjustedDomainEvent raised = item.DomainEvents
            .OfType<StockAdjustedDomainEvent>()
            .Single();

        raised.Delta.Should().Be(2m);
        raised.Value.Should().Be(50.00m);
    }

    /// <summary>
    /// A revaluation announces itself. It is the only fact here with no quantity: the shelf is
    /// worth more than it was and nothing arrived or left.
    /// </summary>
    [Fact]
    public void A_revaluation_announces_the_difference()
    {
        StockItem item = Stocked(10, 250.00m);
        item.ClearDomainEvents();

        Result<StockMovement> revalued = item.Revalue(
            Money.Of(45.20m, Currency.Eur),
            Reference(ReferenceType.SupplierInvoice, "FT 2026/14872"),
            Now);

        revalued.IsSuccess.Should().BeTrue();

        StockRevaluedDomainEvent raised = item.DomainEvents
            .OfType<StockRevaluedDomainEvent>()
            .Single();

        raised.Difference.Should().Be(45.20m);
        raised.CurrencyCode.Should().Be("EUR");
        raised.Reference.Should().Be("FT 2026/14872");
        raised.MovementId.Should().Be(revalued.Value.Id);
    }

    /// <summary>A supplier who charged less moves the shelf the other way, and says so signed.</summary>
    [Fact]
    public void A_revaluation_downwards_carries_a_negative_difference()
    {
        StockItem item = Stocked(10, 250.00m);
        item.ClearDomainEvents();

        item.Revalue(
            Money.Of(-45.20m, Currency.Eur),
            Reference(ReferenceType.SupplierInvoice, "FT 2026/14872"),
            Now).IsSuccess.Should().BeTrue();

        item.DomainEvents.OfType<StockRevaluedDomainEvent>().Single()
            .Difference.Should().Be(-45.20m);
    }

    /// <summary>
    /// A variance of zero raises nothing at all. Most deliveries are invoiced at the price they
    /// were ordered at, and an event for each would bury the ones that matter.
    /// </summary>
    [Fact]
    public void A_variance_of_zero_announces_nothing()
    {
        StockItem item = Stocked(10, 250.00m);
        item.ClearDomainEvents();

        item.Revalue(
            Money.Of(0m, Currency.Eur),
            Reference(ReferenceType.SupplierInvoice, "FT 2026/14872"),
            Now).IsFailure.Should().BeTrue();

        item.DomainEvents.OfType<StockRevaluedDomainEvent>().Should().BeEmpty();
    }

    /// <summary>
    /// One order dispatched in two vans moves the same part twice, and the two movements are
    /// different. Anything keyed on the document number would take the second for a repeat.
    /// </summary>
    [Fact]
    public void Two_issues_on_one_document_are_two_different_movements()
    {
        StockItem item = Stocked(10, 250.00m);
        MovementReference order = Reference(ReferenceType.SalesOrder, "SO-2026-01188");

        Result<StockMovement> first = item.Issue(3m, order, Now);
        Result<StockMovement> second = item.Issue(2m, order, Now.AddHours(4));

        first.Value.Id.Should().NotBe(second.Value.Id);

        item.DomainEvents.OfType<StockIssuedDomainEvent>()
            .Select(raised => raised.MovementId)
            .Should().OnlyHaveUniqueItems();
    }

    private static MovementReference Reference(ReferenceType type, string number) =>
        MovementReference.Create(type, number).Value;

    private static StockItem Stocked(decimal quantity, decimal value)
    {
        StockItem item = StockItem
            .Open(new PartRef(Guid.NewGuid()), WarehouseId.New(), UnitOfMeasure.Each).Value;

        item.ReceiveValued(
            quantity,
            Reference(ReferenceType.GoodsReceipt, "GRN-2026-00042"),
            Now.AddDays(-30),
            Money.Of(value, Currency.Eur)).IsSuccess.Should().BeTrue();

        return item;
    }
}
