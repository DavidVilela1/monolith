using AutoPartsErp.SharedKernel.Authorization;

namespace AutoPartsErp.Web;

/// <summary>
/// One entry in the sidebar.
/// </summary>
/// <param name="Label">What it says.</param>
/// <param name="Icon">Its Phosphor name.</param>
/// <param name="Controller">The controller it opens, or null when the screen is not built yet.</param>
/// <param name="Permission">
/// The permission somebody needs to see it at all. Null means everybody signed in.
/// </param>
public sealed record NavItem(
    string Label,
    string Icon,
    string? Controller,
    string? Permission)
{
    /// <summary>Whether the screen behind this entry exists.</summary>
    public bool IsBuilt => Controller is not null;
}

/// <summary>A titled run of sidebar entries.</summary>
/// <param name="Title">The heading above them.</param>
/// <param name="Items">The entries.</param>
public sealed record NavGroup(string Title, IReadOnlyList<NavItem> Items);

/// <summary>
/// The sidebar, written down once.
/// <para>
/// Entries whose screen is not built yet are listed rather than hidden, and rendered as text
/// instead of a link. Hiding them would make the menu shrink and grow as the system is built,
/// which reads as things disappearing; linking them would be a menu that leads to a 404. Showing
/// the shape of the finished system and saying plainly which parts are not here yet is the
/// honest third option.
/// </para>
/// </summary>
public static class Navigation
{
    /// <summary>The groups, in the order they appear.</summary>
    public static IReadOnlyList<NavGroup> Groups { get; } =
    [
        new NavGroup("Operação",
        [
            new NavItem("Painel", "gauge", "Home", Permission: null),
            new NavItem("Vendas", "shopping-cart", Controller: null, Permissions.Sales.Read),
            new NavItem("Peças", "nut", "Parts", Permissions.Catalog.Read),
            new NavItem("Compras", "truck", Controller: null, Permissions.Purchasing.Read),
            new NavItem("Stock", "stack", Controller: null, Permissions.Inventory.Read),
            new NavItem("Faturação", "file-text", Controller: null, Permissions.Invoicing.Read),
        ]),
        new NavGroup("Gestão",
        [
            new NavItem("Parceiros", "users", Controller: null, Permissions.Partners.Read),
            new NavItem("Preços e rappel", "tag", Controller: null, Permissions.Pricing.Read),
            new NavItem("Contabilidade", "bank", Controller: null, Permissions.Finance.Read),
            new NavItem("Definições", "gear", Controller: null, Permissions.Access.Read),
        ]),
    ];
}
