using AutoPartsErp.Modules.Inventory.Application.Counting;
using AutoPartsErp.Modules.Inventory.Domain;
using AutoPartsErp.Modules.Inventory.Domain.Counting;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Inventory.Tests;

/// <summary>
/// Finding a count sheet again after somebody made it.
/// <para>
/// A stocktake is two people and three days. Until this existed a sheet was only reachable by an
/// identifier whoever created it had written down, so losing the identifier was losing the
/// stocktake — and nothing on any screen would have said so.
/// </para>
/// </summary>
public sealed class ListStockCountsTests
{
    private static readonly DateOnly CountedOn = new(2026, 9, 30);
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 11, 0, 0, TimeSpan.Zero);

    /// <summary>The list says how much of each sheet is still to do.</summary>
    [Fact]
    public async Task A_sheet_says_how_many_lines_are_still_blank()
    {
        StockCount sheet = NewSheet("SC-2026-00014");
        StockCountLineId first = sheet
            .AddLine(Part(), "BP-1188", "Brake pads", Quantity.Each(4)).Value;
        sheet.AddLine(Part(), "OF-4471", "Oil filter", Quantity.Each(10));
        sheet.AddLine(Part(), "SP-0021", "Spark plug", Quantity.Each(6));

        sheet.RecordCount(first, Quantity.Each(3), "ana", Now).IsSuccess.Should().BeTrue();

        StockCountSummary summary = (await Handle(new FakeCounts(sheet))).Single();

        summary.Number.Should().Be("SC-2026-00014");
        summary.Status.Should().Be(nameof(StockCountStatus.Open));
        summary.TotalLines.Should().Be(3);
        summary.CountedLines.Should().Be(1);
        summary.UncountedLines.Should().Be(2);
        summary.CountedOn.Should().Be(CountedOn);
    }

    /// <summary>A sheet nobody has touched is all still to do.</summary>
    [Fact]
    public async Task An_untouched_sheet_is_all_uncounted()
    {
        StockCount sheet = NewSheet("SC-2026-00015");
        sheet.AddLine(Part(), "BP-1188", "Brake pads", Quantity.Each(4));

        StockCountSummary summary = (await Handle(new FakeCounts(sheet))).Single();

        summary.CountedLines.Should().Be(0);
        summary.UncountedLines.Should().Be(1);
    }

    /// <summary>The status asked for is the status the repository is asked for.</summary>
    [Fact]
    public async Task A_status_filter_reaches_the_repository()
    {
        var counts = new FakeCounts(NewSheet("SC-2026-00014"));

        await Handle(counts, new ListStockCountsQuery(Status: "submitted"));

        counts.AskedForStatus.Should().Be(StockCountStatus.Submitted);
    }

    /// <summary>
    /// A status nobody recognizes returns nothing rather than everything, the way every other
    /// filter in this system does. Quietly ignoring one is how a person reads the wrong list and
    /// believes it.
    /// </summary>
    [Theory]
    [InlineData("nonsense")]
    [InlineData("Unknown")]
    public async Task An_unrecognized_status_returns_nothing(string status)
    {
        var counts = new FakeCounts(NewSheet("SC-2026-00014"));

        IReadOnlyList<StockCountSummary> summaries =
            await Handle(counts, new ListStockCountsQuery(Status: status));

        summaries.Should().BeEmpty();
        counts.WasAsked.Should().BeFalse();
    }

    /// <summary>No status means every status.</summary>
    [Fact]
    public async Task No_status_asks_for_all_of_them()
    {
        var counts = new FakeCounts(NewSheet("SC-2026-00014"));

        await Handle(counts);

        counts.WasAsked.Should().BeTrue();
        counts.AskedForStatus.Should().BeNull();
    }

    /// <summary>
    /// A caller asking for ten thousand sheets gets two hundred. A list screen is a list screen,
    /// and the clamp is what stops one being a table scan.
    /// </summary>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(50, 50)]
    [InlineData(10_000, 200)]
    public async Task The_page_size_is_clamped(int asked, int expected)
    {
        var counts = new FakeCounts(NewSheet("SC-2026-00014"));

        await Handle(counts, new ListStockCountsQuery(Take: asked));

        counts.AskedForTake.Should().Be(expected);
    }

    private static async Task<IReadOnlyList<StockCountSummary>> Handle(
        FakeCounts counts,
        ListStockCountsQuery? query = null)
    {
        Result<IReadOnlyList<StockCountSummary>> result =
            await new ListStockCountsQueryHandler(counts)
                .HandleAsync(query ?? new ListStockCountsQuery());

        result.IsSuccess.Should().BeTrue();

        return result.Value;
    }

    private static PartRef Part() => new(Guid.NewGuid());

    private static StockCount NewSheet(string number) =>
        StockCount.Open(number, WarehouseId.New(), CountedOn).Value;

    private sealed class FakeCounts : IStockCountRepository
    {
        private readonly StockCount _sheet;

        public FakeCounts(StockCount sheet)
        {
            _sheet = sheet;
        }

        public bool WasAsked { get; private set; }

        public StockCountStatus? AskedForStatus { get; private set; }

        public int AskedForTake { get; private set; }

        public Task<IReadOnlyList<StockCount>> ListAsync(
            WarehouseId? warehouseId,
            StockCountStatus? status,
            int take,
            CancellationToken cancellationToken = default)
        {
            WasAsked = true;
            AskedForStatus = status;
            AskedForTake = take;

            return Task.FromResult<IReadOnlyList<StockCount>>([_sheet]);
        }

        public Task<StockCount?> GetByIdAsync(
            StockCountId id, CancellationToken cancellationToken = default) =>
            Task.FromResult<StockCount?>(_sheet.Id == id ? _sheet : null);

        public Task<StockCount?> GetWithLinesAsync(
            StockCountId id, CancellationToken cancellationToken = default) =>
            GetByIdAsync(id, cancellationToken);

        public Task<bool> ExistsAsync(
            StockCountId id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_sheet.Id == id);

        public Task<string> NextCountNumberAsync(
            int year, CancellationToken cancellationToken = default) =>
            Task.FromResult($"SC-{year}-00001");

        public void Add(StockCount aggregate)
        {
        }

        public void Remove(StockCount aggregate)
        {
        }
    }
}
