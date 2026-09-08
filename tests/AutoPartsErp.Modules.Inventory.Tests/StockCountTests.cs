using AutoPartsErp.Modules.Inventory.Domain;
using AutoPartsErp.Modules.Inventory.Domain.Counting;
using AutoPartsErp.Modules.Inventory.Domain.Counting.Events;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Inventory.Tests;

/// <summary>
/// The count sheet: what the system said, what somebody found, and who accepted the difference.
/// <para>
/// Before this, correcting stock was one command — type a number, the balance becomes that
/// number, done. That is the mechanism by which stock quietly disappears from a distributor:
/// nothing to review, no snapshot to tell a real difference from stock that legitimately moved,
/// and the person who miscounted signs off their own miscount. Each test below is one of those
/// gaps closed.
/// </para>
/// </summary>
public sealed class StockCountTests
{
    private static readonly DateOnly CountedOn = new(2026, 9, 8);
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_new_sheet_is_open_and_says_where_it_is_counting()
    {
        StockCount sheet = NewSheet();

        sheet.Status.Should().Be(StockCountStatus.Open);
        sheet.IsOpen.Should().BeTrue();
        sheet.Lines.Should().BeEmpty();
        sheet.DomainEvents.Should().ContainItemsAssignableTo<StockCountOpenedDomainEvent>();
    }

    /// <summary>
    /// The snapshot is the sheet's reason to exist as a document. Without it, a difference found
    /// at posting time cannot be told apart from stock that moved while the aisle was being
    /// walked — and "the count was wrong" and "somebody sold four on Tuesday" are different
    /// problems with different people to talk to.
    /// </summary>
    [Fact]
    public void A_line_records_what_the_system_believed_when_the_sheet_was_opened()
    {
        StockCount sheet = NewSheet();

        sheet.AddLine(Part(), "BP-1188", "Brake pad set", Quantity.Each(12))
            .IsSuccess.Should().BeTrue();

        StockCountLine line = sheet.Lines.Single();
        line.SystemQuantity.Should().Be(Quantity.Each(12));
        line.CountedQuantity.Should().BeNull();
        line.IsCounted.Should().BeFalse();
        line.Variance.Should().BeNull();
    }

    [Fact]
    public void Counting_a_line_records_the_figure_the_finder_and_the_moment()
    {
        (StockCount sheet, StockCountLineId lineId) = SheetWithLine(12);

        sheet.RecordCount(lineId, Quantity.Each(9), "ana", Now).IsSuccess.Should().BeTrue();

        StockCountLine line = sheet.Lines.Single();
        line.CountedQuantity.Should().Be(Quantity.Each(9));
        line.CountedBy.Should().Be("ana");
        line.CountedAtUtc.Should().Be(Now);
        line.Variance!.Value.Should().Be(-3m);
        line.HasVariance.Should().BeTrue();
        sheet.HasVariances.Should().BeTrue();
    }

    /// <summary>
    /// The single most important distinction in the whole aggregate. Zero means the shelf was
    /// empty; null means nobody looked. A design that used zero for both writes off every part
    /// the counter did not reach before going home, and the write-off looks exactly like a real
    /// count.
    /// </summary>
    [Fact]
    public void An_empty_shelf_and_an_unvisited_shelf_are_different_answers()
    {
        StockCount sheet = NewSheet();
        StockCountLineId empty = sheet.AddLine(Part(), "BP-1188", "Brake pads", Quantity.Each(4)).Value;
        sheet.AddLine(Part(), "OF-4471", "Oil filter", Quantity.Each(4));

        sheet.RecordCount(empty, Quantity.Each(0), "ana", Now);

        StockCountLine counted = sheet.Lines.First(line => line.Id == empty);
        counted.IsCounted.Should().BeTrue();
        counted.CountedQuantity!.Value.Should().Be(0m);
        counted.Variance!.Value.Should().Be(-4m);

        StockCountLine untouched = sheet.Lines.First(line => line.Id != empty);
        untouched.IsCounted.Should().BeFalse();
        untouched.Variance.Should().BeNull();

        sheet.CountedLines.Should().Be(1);
        sheet.UncountedLines.Should().Be(1);
    }

    /// <summary>A recount is the normal response to a surprising number, and it wins.</summary>
    [Fact]
    public void Counting_a_line_twice_keeps_the_second_walk()
    {
        (StockCount sheet, StockCountLineId lineId) = SheetWithLine(12);

        sheet.RecordCount(lineId, Quantity.Each(2), "ana", Now);
        sheet.RecordCount(lineId, Quantity.Each(11), "bruno", Now.AddMinutes(20));

        StockCountLine line = sheet.Lines.Single();
        line.CountedQuantity.Should().Be(Quantity.Each(11));
        line.CountedBy.Should().Be("bruno");
    }

    [Fact]
    public void A_part_cannot_be_on_the_same_sheet_twice()
    {
        StockCount sheet = NewSheet();
        PartRef part = Part();

        sheet.AddLine(part, "BP-1188", "Brake pads", Quantity.Each(4));

        sheet.AddLine(part, "BP-1188", "Brake pads", Quantity.Each(4))
            .Error.Code.Should().Be("inventory.count.part_already_on_sheet");
    }

    [Fact]
    public void A_count_in_the_wrong_unit_is_refused()
    {
        (StockCount sheet, StockCountLineId lineId) = SheetWithLine(12);

        sheet.RecordCount(lineId, Quantity.Create(3m, UnitOfMeasure.FromCode("L")).Value, "ana", Now)
            .Error.Code.Should().Be("inventory.count.unit_mismatch");

        sheet.Lines.Single().IsCounted.Should().BeFalse();
    }

    [Fact]
    public void A_negative_count_is_refused()
    {
        (StockCount sheet, StockCountLineId lineId) = SheetWithLine(12);

        sheet.RecordCount(lineId, Quantity.Create(-1m, UnitOfMeasure.Each).Value, "ana", Now)
            .Error.Code.Should().Be("inventory.stock.count_negative");
    }

    /// <summary>
    /// Submitting is a separate step from posting, and that separation is the point of the whole
    /// aggregate. A count that finds €4,000 of stock missing is a decision, not data entry.
    /// </summary>
    [Fact]
    public void Submitting_closes_the_sheet_to_counting_and_records_who_finished()
    {
        (StockCount sheet, StockCountLineId lineId) = SheetWithLine(12);
        sheet.RecordCount(lineId, Quantity.Each(9), "ana", Now);

        sheet.Submit("ana", Now).IsSuccess.Should().BeTrue();

        sheet.Status.Should().Be(StockCountStatus.Submitted);
        sheet.SubmittedBy.Should().Be("ana");
        sheet.IsOpen.Should().BeFalse();

        sheet.RecordCount(lineId, Quantity.Each(10), "ana", Now)
            .Error.Code.Should().Be("inventory.count.not_open");

        sheet.DomainEvents.Should().ContainItemsAssignableTo<StockCountSubmittedDomainEvent>();
    }

    /// <summary>
    /// A sheet where nobody counted anything is not a count. Submitting it would ask a reviewer
    /// to accept differences nobody went looking for — and against an uncounted sheet every
    /// difference is zero, so it would sail through review saying nothing is wrong.
    /// </summary>
    [Fact]
    public void A_sheet_nobody_counted_cannot_be_submitted()
    {
        (StockCount sheet, _) = SheetWithLine(12);

        sheet.Submit("ana", Now).Error.Code.Should().Be("inventory.count.nothing_counted");
    }

    [Fact]
    public void An_empty_sheet_cannot_be_submitted()
    {
        NewSheet().Submit("ana", Now).Error.Code.Should().Be("inventory.count.no_lines");
    }

    /// <summary>
    /// The reviewer's third option. Without it their only choices are to accept a number nobody
    /// believes or cancel the sheet and lose an afternoon of counting.
    /// </summary>
    [Fact]
    public void A_submitted_sheet_can_be_sent_back_for_more_counting()
    {
        StockCount sheet = SubmittedSheet();

        sheet.Reopen().IsSuccess.Should().BeTrue();

        sheet.Status.Should().Be(StockCountStatus.Open);
        sheet.SubmittedBy.Should().BeNull();
        sheet.SubmittedAtUtc.Should().BeNull();
    }

    [Fact]
    public void Posting_records_who_accepted_the_differences_and_closes_the_sheet()
    {
        StockCount sheet = SubmittedSheet();

        sheet.Post("carla", Now.AddHours(1)).IsSuccess.Should().BeTrue();

        sheet.Status.Should().Be(StockCountStatus.Posted);
        sheet.PostedBy.Should().Be("carla");
        sheet.PostedAtUtc.Should().Be(Now.AddHours(1));
        sheet.IsClosed.Should().BeTrue();
        sheet.DomainEvents.Should().ContainItemsAssignableTo<StockCountPostedDomainEvent>();
    }

    /// <summary>
    /// Posting an open sheet would apply figures nobody has finished entering. The order of the
    /// two steps is the control, so skipping one has to be refused rather than tolerated.
    /// </summary>
    [Fact]
    public void A_sheet_that_was_never_submitted_cannot_be_posted()
    {
        (StockCount sheet, StockCountLineId lineId) = SheetWithLine(12);
        sheet.RecordCount(lineId, Quantity.Each(9), "ana", Now);

        sheet.Post("carla", Now).Error.Code.Should().Be("inventory.count.not_submitted");
        sheet.Status.Should().Be(StockCountStatus.Open);
    }

    [Fact]
    public void A_posted_sheet_can_never_be_posted_again()
    {
        StockCount sheet = SubmittedSheet();
        sheet.Post("carla", Now);

        sheet.Post("carla", Now).Error.Code.Should().Be("inventory.count.not_submitted");
        sheet.Reopen().Error.Code.Should().Be("inventory.count.not_submitted");
        sheet.Cancel("Changed my mind").Error.Code.Should().Be("inventory.count.already_closed");
    }

    [Fact]
    public void Abandoning_a_sheet_needs_a_reason()
    {
        (StockCount sheet, _) = SheetWithLine(12);

        sheet.Cancel("  ").Error.Code.Should().Be("inventory.count.cancel_reason_required");
        sheet.Status.Should().Be(StockCountStatus.Open);

        sheet.Cancel("Warehouse move; counting again next week").IsSuccess.Should().BeTrue();
        sheet.Status.Should().Be(StockCountStatus.Cancelled);
        sheet.CancellationReason.Should().Contain("Warehouse move");
    }

    [Fact]
    public void A_sheet_has_to_name_a_number_and_a_warehouse()
    {
        StockCount.Open("  ", WarehouseId.New(), CountedOn)
            .Error.Code.Should().Be("inventory.count.number_required");

        StockCount.Open("SC-2026-00001", WarehouseId.Empty, CountedOn)
            .Error.Code.Should().Be("inventory.stock.warehouse_required");
    }

    private static PartRef Part() => new(Guid.NewGuid());

    private static StockCount NewSheet() =>
        StockCount.Open("SC-2026-00014", WarehouseId.New(), CountedOn).Value;

    private static (StockCount Sheet, StockCountLineId LineId) SheetWithLine(int systemQuantity)
    {
        StockCount sheet = NewSheet();
        StockCountLineId lineId = sheet
            .AddLine(Part(), "BP-1188", "Brake pad set", Quantity.Each(systemQuantity))
            .Value;

        return (sheet, lineId);
    }

    private static StockCount SubmittedSheet()
    {
        (StockCount sheet, StockCountLineId lineId) = SheetWithLine(12);
        sheet.RecordCount(lineId, Quantity.Each(9), "ana", Now);
        sheet.Submit("ana", Now);
        sheet.ClearDomainEvents();

        return sheet;
    }
}
