using AutoPartsErp.ModuleContracts.Catalog;
using AutoPartsErp.Modules.Purchasing.Application.Contracts;
using AutoPartsErp.Web.Models;

namespace AutoPartsErp.Web.Tests;

/// <summary>
/// The buyer's queue, read onto a screen.
/// <para>
/// Purchasing says what has run low and what nobody has decided about; Catalog says what each
/// part is called; Inventory says which warehouse is which. Three modules, joined here, none of
/// them knowing the others exist.
/// </para>
/// </summary>
public sealed class SuggestionRowTests
{
    private static readonly Guid Pads = Guid.NewGuid();
    private static readonly Guid Disc = Guid.NewGuid();
    private static readonly Guid Leiria = Guid.NewGuid();

    /// <summary>
    /// Stock already on its way counts towards the shortfall.
    /// <para>
    /// Four available, six on order, a point of eight: nothing is missing, because six are
    /// coming. A screen that subtracted only what is on the shelf would show this as short by
    /// four and have a buyer order it a second time — and the second order arrives, and the
    /// shelf now holds twice what anybody wanted.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(4, 6, 8, 0)]
    [InlineData(4, 0, 8, 4)]
    [InlineData(0, 2, 10, 8)]
    public void What_is_already_coming_counts(
        decimal available, decimal onOrder, decimal point, decimal expected)
    {
        Row(available, onOrder, point).Shortfall.Should().Be(expected);
    }

    /// <summary>
    /// A part comfortably above its line is short of nothing, not of a negative amount.
    /// <para>
    /// Purchasing's own figure is a plain subtraction and goes below zero. Zero is what a person
    /// means by "nothing missing", and a column showing −4 invites somebody to work out what a
    /// negative shortage is.
    /// </para>
    /// </summary>
    [Fact]
    public void A_part_above_its_line_is_short_of_nothing()
    {
        Row(available: 12m, onOrder: 0m, point: 8m).Shortfall.Should().Be(0m);
    }

    /// <summary>A shelf in the red is short by more than its reorder point, which is real.</summary>
    [Fact]
    public void A_negative_shelf_is_short_by_more_than_the_point()
    {
        Row(available: -3m, onOrder: 0m, point: 8m).Shortfall.Should().Be(11m);
    }

    /// <summary>Only an open suggestion is the buyer's to act on.</summary>
    [Theory]
    [InlineData("Open", true)]
    [InlineData("Ordered", false)]
    [InlineData("Dismissed", false)]
    public void Only_an_open_suggestion_is_actionable(string status, bool expected)
    {
        Suggestion(Pads, status: status).Named().IsOpen.Should().Be(expected);
    }

    /// <summary>The catalogue's words and the warehouse's code land on the right rows.</summary>
    [Fact]
    public void The_other_modules_name_the_rows()
    {
        IReadOnlyList<SuggestionRow> rows = SuggestionRow.Combine(
            [Suggestion(Pads), Suggestion(Disc)],
            new Dictionary<Guid, PartDescriptor>
            {
                [Pads] = Descriptor(Pads, "PST-FR-8802", "Pastilhas travão dianteiras"),
                [Disc] = Descriptor(Disc, "DSC-FR-5510", "Disco travão diant. 288mm"),
            },
            new Dictionary<Guid, string> { [Leiria] = "ARM-01" });

        rows[0].Sku.Should().Be("PST-FR-8802");
        rows[0].WarehouseCode.Should().Be("ARM-01");
        rows[1].Name.Should().Be("Disco travão diant. 288mm");
    }

    /// <summary>
    /// Purchasing's order survives being named.
    /// <para>
    /// The list arrives sorted by how far under the line each part has fallen, and that order is
    /// the point of the screen: the first row is the one to deal with first. A dictionary lookup
    /// that quietly reordered it would turn a prioritized queue into an arbitrary list, and
    /// nothing on screen would say so.
    /// </para>
    /// </summary>
    [Fact]
    public void Purchasings_order_survives()
    {
        IReadOnlyList<SuggestionRow> rows = SuggestionRow.Combine(
            [Suggestion(Disc), Suggestion(Pads)],
            new Dictionary<Guid, PartDescriptor>
            {
                [Pads] = Descriptor(Pads, "PST-FR-8802", "Pastilhas"),
                [Disc] = Descriptor(Disc, "DSC-FR-5510", "Disco"),
            },
            new Dictionary<Guid, string> { [Leiria] = "ARM-01" });

        rows[0].Sku.Should().Be("DSC-FR-5510");
        rows[1].Sku.Should().Be("PST-FR-8802");
    }

    /// <summary>
    /// A suggestion whose part the catalogue no longer has still renders.
    /// <para>
    /// A shelf that ran low is a fact whatever the catalogue thinks. The row carries the
    /// identifier where the reference would be, so whoever sees it can go and find out why —
    /// rather than the row vanishing and the shortage with it.
    /// </para>
    /// </summary>
    [Fact]
    public void A_part_with_no_catalogue_entry_still_renders()
    {
        IReadOnlyList<SuggestionRow> rows = SuggestionRow.Combine(
            [Suggestion(Pads)],
            new Dictionary<Guid, PartDescriptor>(),
            new Dictionary<Guid, string>());

        rows.Should().HaveCount(1);
        rows[0].Sku.Should().Be(Pads.ToString());
        rows[0].Name.Should().Be("Sem ficha no catálogo");
        rows[0].WarehouseCode.Should().Be("—");
    }

    /// <summary>An empty queue combines to an empty list, not a failure.</summary>
    [Fact]
    public void An_empty_queue_combines_to_nothing()
    {
        SuggestionRow.Combine(
            [], new Dictionary<Guid, PartDescriptor>(), new Dictionary<Guid, string>())
            .Should().BeEmpty();
    }

    /// <summary>
    /// The list defaults to what is still to be decided, not to everything ever suggested.
    /// <para>
    /// This screen is a work queue. Opening it on every suggestion ever raised, dismissals
    /// included, is opening it on a list nobody can work from.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(null, "Open")]
    [InlineData("", "Open")]
    [InlineData("   ", "Open")]
    [InlineData("Dismissed", "Dismissed")]
    public void The_queue_defaults_to_what_is_undecided(string? asked, string expected)
    {
        new SuggestionSearchForm { Status = asked }.EffectiveStatus.Should().Be(expected);
    }

    private static SuggestionRow Row(decimal available, decimal onOrder, decimal point) =>
        Suggestion(Pads, available, onOrder, point).Named();

    private static ReplenishmentSuggestionDto Suggestion(
        Guid partId,
        decimal available = 2m,
        decimal onOrder = 0m,
        decimal point = 8m,
        string status = "Open") =>
        new(
            Guid.NewGuid(),
            partId,
            Leiria,
            available,
            onOrder,
            point,
            SuggestedQuantity: 24m,
            Shortfall: point - (available + onOrder),
            status,
            RaisedAtUtc: new DateTimeOffset(2026, 9, 14, 8, 0, 0, TimeSpan.Zero),
            LastSeenAtUtc: new DateTimeOffset(2026, 9, 18, 8, 0, 0, TimeSpan.Zero),
            PurchaseOrderId: null,
            DismissedReason: null);

    private static PartDescriptor Descriptor(Guid partId, string sku, string name) =>
        new(partId, sku, name, "UN", true, true, false, null);
}

/// <summary>Turns a suggestion into a row without repeating the naming in every test.</summary>
internal static class SuggestionTestExtensions
{
    public static SuggestionRow Named(this ReplenishmentSuggestionDto suggestion) =>
        SuggestionRow.From(
            suggestion,
            new PartDescriptor(suggestion.PartId, "PST-FR-8802", "Pastilhas", "UN", true, true, false, null),
            "ARM-01");
}
