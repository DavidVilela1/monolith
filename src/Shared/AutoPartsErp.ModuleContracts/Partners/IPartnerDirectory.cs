namespace AutoPartsErp.ModuleContracts.Partners;

/// <summary>
/// What Partners will tell another module about a company, on demand.
/// <para>
/// Purchasing has been taking a supplier's identity and code on trust since it was written,
/// because there was no way to ask. This is the way to ask. It answers one question — may we
/// trade with them, in this direction, right now — and returns the two strings a document needs
/// to print. It does not expose addresses, contacts, tax numbers or terms; a module that needs
/// those should have a reason and its own contract.
/// </para>
/// <para>
/// Note what Sales does <i>not</i> do with this: it keeps its own customer account, fed by
/// events, because the counter asks that question on every keystroke and a synchronous call per
/// keystroke is a different kind of mistake. A projection and a query contract are both right
/// answers to different frequencies.
/// </para>
/// </summary>
public interface IPartnerDirectory
{
    /// <summary>
    /// One partner, or null when no such partner exists in this tenant.
    /// </summary>
    /// <param name="partnerId">The partner.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<PartnerTradingStatus?> GetAsync(Guid partnerId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Whether a company can be traded with, and what to print on the document.
/// </summary>
/// <param name="PartnerId">The partner.</param>
/// <param name="Code">Their short code.</param>
/// <param name="LegalName">Their registered name, as it goes on a document.</param>
/// <param name="IsCustomer">True when we sell to them.</param>
/// <param name="IsSupplier">True when we buy from them.</param>
/// <param name="CanTakeNewOrders">True when a sales order may be taken: a customer, not on hold.</param>
/// <param name="CanPlacePurchaseOrders">True when a purchase order may be raised: a supplier, not on hold.</param>
public sealed record PartnerTradingStatus(
    Guid PartnerId,
    string Code,
    string LegalName,
    bool IsCustomer,
    bool IsSupplier,
    bool CanTakeNewOrders,
    bool CanPlacePurchaseOrders);
