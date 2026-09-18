using AutoPartsErp.ModuleContracts.Inventory;
using AutoPartsErp.Modules.Catalog.Application.Contracts;
using AutoPartsErp.Modules.Inventory.Application.Contracts;
using AutoPartsErp.Web.Models;

namespace AutoPartsErp.Web.Tests;

/// <summary>
/// Putting a catalogue page and a warehouse's stock side by side.
/// <para>
/// Two modules answer two questions and this joins the answers in memory. The alternative — one
/// query across both schemas — would be shorter and would be the moment the modular monolith
/// stopped being one. What has to be right here is the mismatch: parts Inventory has never heard
/// of, and stock for parts that are not on this page.
/// </para>
/// </summary>
public sealed class PartSearchResultsTests
{
    private static readonly Guid Pads = Guid.NewGuid();
    private static readonly Guid Disc = Guid.NewGuid();
    private static readonly Guid Fluid = Guid.NewGuid();
    private static readonly Guid Warehouse = Guid.NewGuid();

    /// <summary>Every part gets a row, in the order the catalogue gave them.</summary>
    [Fact]
    public void Every_part_keeps_its_place()
    {
        IReadOnlyList<PartRow> rows = PartSearchResults.Combine(
            [Part(Pads, "PST-FR-8802"), Part(Disc, "DSC-FR-5510"), Part(Fluid, "LIQ-FR-DOT4")],
            Stock(Pads, onHand: 6m, reserved: 2m));

        rows.Should().HaveCount(3);
        rows[0].Part.Sku.Should().Be("PST-FR-8802");
        rows[1].Part.Sku.Should().Be("DSC-FR-5510");
        rows[2].Part.Sku.Should().Be("LIQ-FR-DOT4");
    }

    /// <summary>The figures land on the part they belong to.</summary>
    [Fact]
    public void Stock_lands_on_its_own_part()
    {
        IReadOnlyList<PartRow> rows = PartSearchResults.Combine(
            [Part(Pads, "PST-FR-8802"), Part(Disc, "DSC-FR-5510")],
            Stock(Disc, onHand: 14m, reserved: 4m));

        rows[0].Stock.Should().BeNull();
        rows[1].Stock.Should().NotBeNull();
        rows[1].Stock!.OnHand.Should().Be(14m);
        rows[1].Stock!.Available.Should().Be(10m);
    }

    /// <summary>
    /// A part Inventory has no record of comes back with nothing, not with zero.
    /// <para>
    /// This is the one that matters. Zero means "none left" and stops a sale; nothing means
    /// "never counted", which is the ordinary state of a part still in draft. Collapsing the two
    /// would have a counter turn away a customer for a part sitting on the shelf.
    /// </para>
    /// </summary>
    [Fact]
    public void A_part_inventory_never_heard_of_has_no_figures_rather_than_zero()
    {
        IReadOnlyList<PartRow> rows = PartSearchResults.Combine(
            [Part(Fluid, "LIQ-FR-DOT4")],
            new Dictionary<Guid, StockAvailability>());

        rows.Should().HaveCount(1);
        rows[0].Stock.Should().BeNull();
    }

    /// <summary>
    /// Stock for something that is not on this page is dropped rather than appended. Inventory is
    /// free to answer with more than it was asked about; the page is the page.
    /// </summary>
    [Fact]
    public void Stock_for_a_part_that_is_not_on_the_page_is_ignored()
    {
        IReadOnlyList<PartRow> rows = PartSearchResults.Combine(
            [Part(Pads, "PST-FR-8802")],
            Stock(Disc, onHand: 14m, reserved: 0m));

        rows.Should().HaveCount(1);
        rows[0].Stock.Should().BeNull();
    }

    /// <summary>An empty page is an empty list, not a failure.</summary>
    [Fact]
    public void An_empty_page_combines_to_nothing()
    {
        PartSearchResults.Combine([], Stock(Pads, 1m, 0m)).Should().BeEmpty();
    }

    /// <summary>The warehouse asked for is the warehouse used, when it exists.</summary>
    [Fact]
    public void The_warehouse_asked_for_is_used()
    {
        Guid second = Guid.NewGuid();

        PartSearchResults.ChooseWarehouse(second, [Place(Warehouse, "ARM-01"), Place(second, "ARM-02")])
            .Should().Be(second);
    }

    /// <summary>
    /// A warehouse nobody recognizes falls back to the first rather than to none.
    /// <para>
    /// A stale link or a typed identifier would otherwise render a catalogue with empty stock
    /// columns and nothing saying why, which reads as "we have none of any of this".
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_unknown_warehouse_falls_back_to_the_first(bool askForNothing)
    {
        Guid? asked = askForNothing ? null : Guid.NewGuid();

        PartSearchResults.ChooseWarehouse(asked, [Place(Warehouse, "ARM-01")])
            .Should().Be(Warehouse);
    }

    /// <summary>With no warehouses at all there is nothing to count in, and the screen says so.</summary>
    [Fact]
    public void No_warehouses_means_no_choice()
    {
        PartSearchResults.ChooseWarehouse(Warehouse, []).Should().BeNull();
    }

    private static PartSummary Part(Guid id, string sku) => new()
    {
        Id = id,
        Sku = sku,
        ManufacturerPartNumber = "0986494104",
        BrandCode = "BOS",
        BrandName = "Bosch",
        CategoryName = "Travagem",
        Name = "Pastilhas travão",
        StockUnit = "UN",
        Status = "Active",
        RequiresCoreReturn = false,
    };

    private static Dictionary<Guid, StockAvailability> Stock(
        Guid partId, decimal onHand, decimal reserved) =>
        new()
        {
            [partId] = new StockAvailability(
                partId, Warehouse, onHand, reserved, onHand - reserved, "UN"),
        };

    private static WarehouseDto Place(Guid id, string code) =>
        new(id, code, "Leiria", "Main", true, false, false, 0);
}
