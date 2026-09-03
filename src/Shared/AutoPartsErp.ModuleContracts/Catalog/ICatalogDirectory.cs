namespace AutoPartsErp.ModuleContracts.Catalog;

/// <summary>
/// What Catalog will tell another module about a part, on demand.
/// <para>
/// Until now every module that put a part on a document asked its caller to type the SKU, the
/// description and the unit alongside the part's id. That is three chances to disagree with the
/// catalogue on every line, and a document is forever: an invoice printed with the wrong unit is
/// wrong for as long as anybody keeps it. The caller knows which part it means — it is sending
/// the id — so it should not also have to say what that part is called.
/// </para>
/// <para>
/// The values that come back are still snapshotted onto the document, deliberately. A part
/// renamed in 2027 must not silently rewrite an order confirmed in 2026. This contract is where
/// the snapshot is <i>taken</i>, not a replacement for taking one.
/// </para>
/// </summary>
public interface ICatalogDirectory
{
    /// <summary>
    /// What the catalogue knows about a part, or null when it has never heard of it — which in
    /// practice means a stale id, or a part deleted since the caller last looked.
    /// </summary>
    /// <param name="partId">The part.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<PartDescriptor?> GetAsync(Guid partId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The same question for several parts at once.
    /// <para>
    /// One round trip rather than one per line, for the same reason as everywhere else: a
    /// ten-line order asking ten times looks fine on a laptop and does not on a counter.
    /// </para>
    /// </summary>
    /// <param name="partIds">The parts to ask about.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One entry per part the catalogue knows; ids it does not recognize are absent.</returns>
    Task<IReadOnlyDictionary<Guid, PartDescriptor>> GetManyAsync(
        IReadOnlyCollection<Guid> partIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The same, by SKU rather than by id.
    /// <para>
    /// Nobody at a trade counter types a GUID. They read the code off the bin label, and this is
    /// how a front end turns that into something the rest of the system can use. Matching is
    /// case- and whitespace-insensitive, because <c>bp-1234</c> and <c>BP-1234 </c> are the same
    /// part to the person holding it.
    /// </para>
    /// </summary>
    /// <param name="sku">The stock keeping unit.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<PartDescriptor?> FindBySkuAsync(string sku, CancellationToken cancellationToken = default);
}

/// <summary>
/// A part, flattened to the facts another module needs in order to put it on a document.
/// <para>
/// No money. The core deposit is a fact about the part, but an amount would drag a currency and
/// a rounding policy across the boundary and this contract would stop being flat. Whether a core
/// is owed is what a document needs to know; what it costs belongs with pricing.
/// </para>
/// </summary>
/// <param name="PartId">The part.</param>
/// <param name="Sku">The distributor's own code, normalized uppercase.</param>
/// <param name="Name">Short description, as it should appear on a picking list or invoice.</param>
/// <param name="StockUnitCode">
/// The unit every quantity of this part is counted in, e.g. EA, SET, L. Fixed once the part goes
/// live, which is what makes it safe to raise a line in without asking.
/// </param>
/// <param name="IsSellable">Whether the part may go on a new sales order.</param>
/// <param name="IsPurchasable">Whether the part may go on a new purchase order.</param>
/// <param name="RequiresCoreReturn">
/// True for remanufactured parts sold against a returnable core. The document needs to know so
/// the counter asks for the old unit back.
/// </param>
/// <param name="SupersededByPartId">
/// The part that replaces this one, once it has been discontinued. Present so a refusal can say
/// what to sell instead rather than only that this one is finished.
/// </param>
public sealed record PartDescriptor(
    Guid PartId,
    string Sku,
    string Name,
    string StockUnitCode,
    bool IsSellable,
    bool IsPurchasable,
    bool RequiresCoreReturn,
    Guid? SupersededByPartId);
