namespace AutoPartsErp.ModuleContracts.Pricing;

/// <summary>
/// What Pricing will tell another module a part costs, on demand.
/// <para>
/// The fourth of these contracts, and the one that finishes the job the other three started: a
/// sales line now names a part and a quantity, and everything else about it — what it is called,
/// how it is counted, whether there is stock, and now what it costs — comes from the module that
/// owns the answer.
/// </para>
/// <para>
/// Read-only, and it does not hold anything. A price quoted here and a price on a confirmed order
/// are two different facts: the document snapshots what it was told, because a price list moving
/// next Tuesday must not rewrite what a customer agreed to today.
/// </para>
/// </summary>
public interface IPriceProvider
{
    /// <summary>
    /// What a customer pays for a part at a quantity, or null when nothing prices it.
    /// <para>
    /// Null covers several situations that look the same to a caller and different to whoever has
    /// to fix them — a part nobody has priced, a customer whose agreement expired, a quantity
    /// below the smallest pack. Ask Pricing's own endpoint when the difference matters; it
    /// answers with the reason.
    /// </para>
    /// </summary>
    /// <param name="partId">The part.</param>
    /// <param name="quantity">How many are being bought. Quantity breaks depend on it.</param>
    /// <param name="customerId">The customer, or null for a walk-in with no account.</param>
    /// <param name="on">The day being priced for, or null for today.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<PartPrice?> GetAsync(
        Guid partId,
        decimal quantity,
        Guid? customerId = null,
        DateOnly? on = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The least a line for this customer may make, as a percentage of what they pay, or null
    /// when nothing anywhere sets a floor.
    /// <para>
    /// Resolved from their agreement's list, falling back to the company figure. Null is not
    /// zero, and the difference is the whole point: zero says "never below cost", which is a
    /// decision somebody made, and null says nobody has made one. An installation that has never
    /// heard of margin floors gets null and nothing is refused — which is what upgrading into
    /// this feature has to feel like, because clearing obsolete stock at a loss is a real thing a
    /// distributor does and a floor nobody chose should not stop it.
    /// </para>
    /// <para>
    /// Of revenue, like every other margin figure in this system — a part bought at 10 and sold
    /// at 15 makes 33.33, not 50.
    /// </para>
    /// </summary>
    /// <param name="customerId">The customer, or null for a walk-in with no account.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<decimal?> GetMinimumMarginPercentAsync(
        Guid? customerId = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A price, flattened, with enough of its reasoning to put on a document.
/// <para>
/// <paramref name="NetUnitPrice"/> is the number a line is raised at. The rest is there so the
/// document can say where it came from — three weeks later somebody will ask, and re-deriving it
/// means re-running rules that may have moved since.
/// </para>
/// </summary>
/// <param name="PartId">The part.</param>
/// <param name="Quantity">The quantity it was priced at.</param>
/// <param name="CurrencyCode">The currency. The caller checks it against the document's own.</param>
/// <param name="GrossUnitPrice">The list price, before the customer's own discount.</param>
/// <param name="DiscountPercent">What their agreement takes off it.</param>
/// <param name="NetUnitPrice">What they actually pay per unit.</param>
/// <param name="PriceListId">The list the price came from.</param>
/// <param name="PriceListCode">Its code, for the document to name.</param>
/// <param name="AppliedBreakQuantity">The quantity the applied break starts at.</param>
public sealed record PartPrice(
    Guid PartId,
    decimal Quantity,
    string CurrencyCode,
    decimal GrossUnitPrice,
    decimal DiscountPercent,
    decimal NetUnitPrice,
    Guid PriceListId,
    string PriceListCode,
    decimal AppliedBreakQuantity);
