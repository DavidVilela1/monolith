namespace AutoPartsErp.Modules.Pricing.Domain.PriceLists;

/// <summary>
/// What a price list is for.
/// <para>
/// The kind is not decoration: it decides what happens when two lists could both answer the same
/// question. A promotion beats a customer's own agreement, which beats the standard list, and
/// that ordering is the whole reason a distributor can run a February campaign without editing
/// four hundred customer agreements and then editing them all back in March.
/// </para>
/// </summary>
public enum PriceListKind
{
    /// <summary>Unspecified. Never persisted.</summary>
    Unknown = 0,

    /// <summary>
    /// The list everyone falls back to. Exactly one may be the default, and it is what a walk-in
    /// customer with no account pays.
    /// </summary>
    Standard = 1,

    /// <summary>
    /// Negotiated for one customer or a group of them. Assigned through a
    /// <see cref="Customers.CustomerPricing"/> agreement rather than by naming the customer here,
    /// so one list can serve a whole buying group.
    /// </summary>
    Customer = 2,

    /// <summary>
    /// A campaign with a start and an end. Beats everything else while it is running, and stops
    /// applying on its own when it expires — nobody has to remember to turn it off.
    /// </summary>
    Promotion = 3,
}
