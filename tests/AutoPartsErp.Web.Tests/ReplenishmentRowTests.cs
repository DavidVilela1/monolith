using AutoPartsErp.ModuleContracts.Catalog;
using AutoPartsErp.Modules.Inventory.Application.Contracts;
using AutoPartsErp.Web.Models;

namespace AutoPartsErp.Web.Tests;

/// <summary>
/// The replenishment panel: what has fallen to its reorder point, with the catalogue's words on it.
/// <para>
/// Inventory knows the shelves and nothing about what anything is called; Catalog knows the names
/// and nothing about the shelves. The rows are joined here, and what has to be right is the
/// arithmetic on the shortfall and the order the list arrives in.
/// </para>
/// </summary>
public sealed class ReplenishmentRowTests
{
    private static readonly Guid Pads = Guid.NewGuid();
    private static readonly Guid Disc = Guid.NewGuid();

    /// <summary>
    /// Stock already on its way counts towards the shortfall.
    /// <para>
    /// This is the one that matters. Four available, six on order, a point of eight: nothing is
    /// short, because six are coming. A panel that subtracted only what is on the shelf would
    /// show this as short by four and have a buyer order it a second time — and the second order
    /// arrives, and now the shelf holds twice what anybody wanted.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(4, 6, 8, 0)]
    [InlineData(4, 0, 8, 4)]
    [InlineData(0, 2, 10, 8)]
    [InlineData(12, 0, 8, 0)]
    public void What_is_already_coming_counts(
        decimal available, decimal onOrder, decimal point, decimal expected)
    {
        Row(available, onOrder, point).Shortfall.Should().Be(expected);
    }

    /// <summary>
    /// A shelf in the red is short by the whole reorder point and not by more. Negative available
    /// stock is real — a warehouse can be configured to allow it — and the number to order is
    /// still the number that gets back to the line, not a larger one invented by the arithmetic.
    /// </summary>
    [Fact]
    public void A_negative_shelf_is_short_by_more_than_the_point()
    {
        Row(available: -3m, onOrder: 0m, point: 8m).Shortfall.Should().Be(11m);
    }

    /// <summary>A part with no reorder point is not short of anything.</summary>
    [Fact]
    public void No_reorder_point_is_no_shortfall()
    {
        Row(available: 0m, onOrder: 0m, point: null).Shortfall.Should().Be(0m);
    }

    /// <summary>The catalogue's words land on the right rows.</summary>
    [Fact]
    public void The_catalogue_names_the_parts()
    {
        IReadOnlyList<ReplenishmentRow> rows = ReplenishmentRow.Combine(
            [Balance(Pads, 1m), Balance(Disc, 2m)],
            new Dictionary<Guid, PartDescriptor>
            {
                [Pads] = Descriptor(Pads, "PST-FR-8802", "Pastilhas travão dianteiras"),
                [Disc] = Descriptor(Disc, "DSC-FR-5510", "Disco travão diant. 288mm"),
            });

        rows[0].Sku.Should().Be("PST-FR-8802");
        rows[1].Name.Should().Be("Disco travão diant. 288mm");
    }

    /// <summary>
    /// Inventory's order survives being named.
    /// <para>
    /// The list arrives sorted by how far under the line each part has fallen, and that order is
    /// the whole point of the panel: the first row is the one to deal with first. A dictionary
    /// lookup that quietly reordered it would turn a prioritized list into an arbitrary one, and
    /// nothing on screen would say so.
    /// </para>
    /// </summary>
    [Fact]
    public void Inventorys_order_survives()
    {
        IReadOnlyList<ReplenishmentRow> rows = ReplenishmentRow.Combine(
            [Balance(Disc, 2m), Balance(Pads, 1m)],
            new Dictionary<Guid, PartDescriptor>
            {
                [Pads] = Descriptor(Pads, "PST-FR-8802", "Pastilhas"),
                [Disc] = Descriptor(Disc, "DSC-FR-5510", "Disco"),
            });

        rows[0].Sku.Should().Be("DSC-FR-5510");
        rows[1].Sku.Should().Be("PST-FR-8802");
    }

    /// <summary>
    /// A shelf with stock on it is a fact whatever the catalogue thinks.
    /// <para>
    /// Inventory can hold a balance for a part Catalog no longer has. The row still renders, with
    /// the identifier where the reference would be, so that whoever sees it can go and find out
    /// why — rather than the row silently vanishing and the shortage with it.
    /// </para>
    /// </summary>
    [Fact]
    public void A_part_with_no_catalogue_entry_still_renders()
    {
        IReadOnlyList<ReplenishmentRow> rows = ReplenishmentRow.Combine(
            [Balance(Pads, 1m)], new Dictionary<Guid, PartDescriptor>());

        rows.Should().HaveCount(1);
        rows[0].Sku.Should().Be(Pads.ToString());
        rows[0].Name.Should().Be("Sem ficha no catálogo");
    }

    /// <summary>Nothing short is an empty panel, not a failure.</summary>
    [Fact]
    public void Nothing_short_combines_to_nothing()
    {
        ReplenishmentRow.Combine([], new Dictionary<Guid, PartDescriptor>()).Should().BeEmpty();
    }

    private static ReplenishmentRow Row(decimal available, decimal onOrder, decimal? point) =>
        ReplenishmentRow.From(
            Balance(Pads, available, onOrder, point),
            Descriptor(Pads, "PST-FR-8802", "Pastilhas"));

    private static StockBalance Balance(
        Guid partId, decimal available, decimal onOrder = 0m, decimal? point = 8m) => new()
        {
            StockItemId = Guid.NewGuid(),
            PartId = partId,
            WarehouseId = Guid.NewGuid(),
            WarehouseCode = "ARM-01",
            WarehouseName = "Leiria",
            Unit = "UN",
            OnHand = available,
            Reserved = 0m,
            Available = available,
            OnOrder = onOrder,
            ReorderPoint = point,
            ReorderQuantity = 24m,
            NeedsReplenishment = true,
            StockValue = 0m,
            ValueCurrency = "EUR",
        };

    private static PartDescriptor Descriptor(Guid partId, string sku, string name) =>
        new(partId, sku, name, "UN", true, true, false, null);
}
