namespace AutoPartsErp.Modules.Pricing.Domain.PriceLists;

/// <summary>
/// Where a price list is in its life.
/// <para>
/// The transitions are one-way: <c>Draft → Active → Archived</c>. A list that has ever priced a
/// document cannot go back to draft, because the orders that quoted it are still out there and
/// somebody will eventually ask why they were charged what they were charged.
/// </para>
/// </summary>
public enum PriceListStatus
{
    /// <summary>Unspecified. Never persisted.</summary>
    Unknown = 0,

    /// <summary>
    /// Being built. Prices can be added, changed and removed freely, and nothing quotes from it.
    /// This is the only state in which a list is safe to restructure.
    /// </summary>
    Draft = 1,

    /// <summary>
    /// Live. Quotes come from it, and its prices can still be corrected — a price list is a
    /// standing offer, not a document, and a wrong price has to be fixable on a Tuesday morning.
    /// </summary>
    Active = 2,

    /// <summary>
    /// Withdrawn. Quotes no longer come from it, and it is kept only so a document raised last
    /// year still explains itself.
    /// </summary>
    Archived = 3,
}
