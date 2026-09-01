using AutoPartsErp.Modules.Purchasing.Domain;
using AutoPartsErp.Modules.Purchasing.Domain.Replenishment;
using AutoPartsErp.Modules.Purchasing.Domain.Replenishment.Events;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Purchasing.Tests;

/// <summary>
/// The consumer of Inventory's reorder-point signal. What matters most here is that it is safe
/// to run twice: an at-least-once bus will redeliver, and stock crosses its reorder point every
/// time somebody picks the last few off the shelf.
/// </summary>
public sealed class ReplenishmentSuggestionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 9, 30, 0, TimeSpan.Zero);
    private static readonly WarehouseRef Warehouse = new(Guid.NewGuid());

    private static ReplenishmentSuggestion NewSuggestion(
        decimal available = 2m,
        decimal reorderPoint = 10m,
        decimal suggested = 40m) =>
        ReplenishmentSuggestion.Open(new PartRef(Guid.NewGuid()), Warehouse, available, reorderPoint, suggested, Now)
            .Value;

    [Fact]
    public void A_new_suggestion_is_open_and_waiting_for_a_buyer()
    {
        ReplenishmentSuggestion suggestion = NewSuggestion();

        suggestion.Status.Should().Be(SuggestionStatus.Open);
        suggestion.IsOpen.Should().BeTrue();
        suggestion.PurchaseOrderId.Should().BeNull();
        suggestion.RaisedAtUtc.Should().Be(Now);
        suggestion.LastSeenAtUtc.Should().Be(Now);
    }

    [Fact]
    public void Opening_one_raises_an_event()
    {
        ReplenishmentSuggestion suggestion = NewSuggestion();

        suggestion.DomainEvents.OfType<ReplenishmentSuggestedDomainEvent>().Should().ContainSingle();
    }

    [Fact]
    public void The_shortfall_is_how_far_below_the_trigger_it_has_fallen()
    {
        NewSuggestion(available: 2m, reorderPoint: 10m).Shortfall.Should().Be(8m);
    }

    [Fact]
    public void Suggesting_an_order_for_nothing_is_refused()
    {
        Result<ReplenishmentSuggestion> suggestion = ReplenishmentSuggestion
            .Open(new PartRef(Guid.NewGuid()), Warehouse, 2m, 10m, 0m, Now);

        suggestion.IsFailure.Should().BeTrue();
        suggestion.Error.Code.Should().Be("purchasing.suggestion.quantity_not_positive");
    }

    [Fact]
    public void A_suggestion_needs_a_part()
    {
        ReplenishmentSuggestion
            .Open(PartRef.Empty, Warehouse, 2m, 10m, 40m, Now)
            .Error.Code.Should().Be("purchasing.suggestion.part_required");
    }

    [Fact]
    public void A_suggestion_needs_a_warehouse()
    {
        ReplenishmentSuggestion
            .Open(new PartRef(Guid.NewGuid()), WarehouseRef.Empty, 2m, 10m, 40m, Now)
            .Error.Code.Should().Be("purchasing.suggestion.warehouse_required");
    }

    [Fact]
    public void A_repeat_signal_refreshes_the_reading()
    {
        ReplenishmentSuggestion suggestion = NewSuggestion(available: 2m, suggested: 40m);

        suggestion.Refresh(1m, 10m, 45m, Now.AddMinutes(5)).IsSuccess.Should().BeTrue();

        suggestion.QuantityAvailable.Should().Be(1m);
        suggestion.SuggestedQuantity.Should().Be(45m);
        suggestion.LastSeenAtUtc.Should().Be(Now.AddMinutes(5));
        suggestion.RaisedAtUtc.Should().Be(Now);
    }

    [Fact]
    public void Refreshing_is_silent_so_the_list_does_not_become_noise()
    {
        ReplenishmentSuggestion suggestion = NewSuggestion();
        suggestion.ClearDomainEvents();

        suggestion.Refresh(1m, 10m, 45m, Now.AddMinutes(5));

        suggestion.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Rolling_one_into_an_order_marks_it_dealt_with()
    {
        ReplenishmentSuggestion suggestion = NewSuggestion();
        PurchaseOrderId orderId = PurchaseOrderId.New();

        suggestion.MarkOrdered(orderId).IsSuccess.Should().BeTrue();

        suggestion.Status.Should().Be(SuggestionStatus.Ordered);
        suggestion.IsOpen.Should().BeFalse();
        suggestion.PurchaseOrderId.Should().Be(orderId);
        suggestion.DomainEvents.OfType<ReplenishmentOrderedDomainEvent>().Should().ContainSingle();
    }

    [Fact]
    public void One_that_has_been_ordered_is_no_longer_refreshed()
    {
        ReplenishmentSuggestion suggestion = NewSuggestion();
        suggestion.MarkOrdered(PurchaseOrderId.New());

        Result refreshed = suggestion.Refresh(1m, 10m, 45m, Now.AddMinutes(5));

        refreshed.IsFailure.Should().BeTrue();
        refreshed.Error.Code.Should().Be("purchasing.suggestion.not_open");
    }

    [Fact]
    public void One_that_has_been_ordered_cannot_be_ordered_again()
    {
        ReplenishmentSuggestion suggestion = NewSuggestion();
        suggestion.MarkOrdered(PurchaseOrderId.New());

        suggestion.MarkOrdered(PurchaseOrderId.New())
            .Error.Code.Should().Be("purchasing.suggestion.not_open");
    }

    [Fact]
    public void A_buyer_can_dismiss_one_with_a_reason()
    {
        ReplenishmentSuggestion suggestion = NewSuggestion();

        suggestion.Dismiss("Being run down; use up the shelf stock.").IsSuccess.Should().BeTrue();

        suggestion.Status.Should().Be(SuggestionStatus.Dismissed);
        suggestion.DismissedReason.Should().Be("Being run down; use up the shelf stock.");
        suggestion.DomainEvents.OfType<ReplenishmentDismissedDomainEvent>().Should().ContainSingle();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void A_dismissal_needs_a_reason(string? reason)
    {
        ReplenishmentSuggestion suggestion = NewSuggestion();

        Result dismissed = suggestion.Dismiss(reason);

        dismissed.IsFailure.Should().BeTrue();
        dismissed.Error.Code.Should().Be("purchasing.suggestion.dismiss_reason_required");
    }

    [Fact]
    public void A_dismissed_suggestion_stays_dismissed()
    {
        ReplenishmentSuggestion suggestion = NewSuggestion();
        suggestion.Dismiss("Not reordering this one.");

        suggestion.Dismiss("Nor this time.").Error.Code.Should().Be("purchasing.suggestion.not_open");
        suggestion.MarkOrdered(PurchaseOrderId.New()).Error.Code.Should().Be("purchasing.suggestion.not_open");
    }

    [Fact]
    public void A_dismissal_reason_is_trimmed()
    {
        ReplenishmentSuggestion suggestion = NewSuggestion();

        suggestion.Dismiss("   Superseded part.   ");

        suggestion.DismissedReason.Should().Be("Superseded part.");
    }
}
