namespace AutoPartsErp.Modules.Catalog.Domain.Parts;

/// <summary>
/// Where a part sits in its commercial life.
/// <para>
/// The transitions are deliberately one-way: <c>Draft → Active → Discontinued → Obsolete</c>.
/// A part that has been sold can never go back to Draft, because purchase orders, stock ledgers
/// and invoices already point at it and their history has to stay meaningful.
/// </para>
/// </summary>
public enum PartStatus
{
    /// <summary>Unspecified. Never persisted.</summary>
    Unknown = 0,

    /// <summary>
    /// Being set up. Not orderable, not sellable, and still fully editable, including its
    /// stocking unit. This is the only state in which structural changes are safe.
    /// </summary>
    Draft = 1,

    /// <summary>Live: can be purchased, stocked, quoted and sold.</summary>
    Active = 2,

    /// <summary>
    /// No longer purchased, but remaining stock is still sold and it is still supported for
    /// returns and warranty. Usually points at a superseding part.
    /// </summary>
    Discontinued = 3,

    /// <summary>
    /// Dead. Not sellable at all, kept only so historical documents still resolve.
    /// </summary>
    Obsolete = 4,
}
