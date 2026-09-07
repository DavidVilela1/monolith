namespace AutoPartsErp.ModuleContracts.Partners;

/// <summary>
/// Who a company is for the purpose of putting them on a legal document.
/// <para>
/// A second, deliberately separate contract from <see cref="IPartnerDirectory"/>, which says in
/// its own remarks that it does not hand out tax numbers and that a module needing them should
/// have a reason and its own contract. This is that module and this is that contract: Invoicing
/// cannot issue an invoice without the customer's NIF, because the law requires the document to
/// identify them.
/// </para>
/// <para>
/// Keeping them apart is not ceremony. Purchasing asks the trading directory a hundred times a
/// day and has no business receiving anybody's tax number in the answer; the fewer places a NIF
/// travels to, the fewer places it can leak from. Two contracts, two reasons to be told, and a
/// grep for who reads billing identities returns exactly the module that prints them.
/// </para>
/// </summary>
public interface IBillingPartyDirectory
{
    /// <summary>
    /// One company's billing identity, or null when no such partner exists in this tenant.
    /// </summary>
    /// <param name="partnerId">The partner.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<BillingParty?> GetAsync(Guid partnerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Several at once, keyed by identifier, with the ones that do not exist simply absent.
    /// <para>
    /// For the SAF-T export, which needs the customer master record behind every document in a
    /// period — a busy month at a trade counter is a few thousand documents against a few hundred
    /// customers, and asking one at a time would be a few hundred round trips to build one file.
    /// </para>
    /// </summary>
    /// <param name="partnerIds">The partners to look up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyDictionary<Guid, BillingParty>> GetManyAsync(
        IReadOnlyCollection<Guid> partnerIds,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A company as a document names them.
/// <para>
/// The identity fields — name and tax number — are snapshotted onto a document when it is drawn
/// and never read again: a customer who renames next year does not change an invoice issued this
/// year. The address is different. It is master data rather than document data, and the SAF-T
/// customer table is explicitly a master file, so an export reads it live and reports where they
/// are now.
/// </para>
/// </summary>
/// <param name="PartnerId">The partner.</param>
/// <param name="Code">Their short code.</param>
/// <param name="LegalName">Their registered name, as it goes on the document.</param>
/// <param name="TaxNumber">
/// Their tax number without the country prefix, or null when none is on file. Null is legal and
/// ordinary: a walk-in customer at a trade counter has no NIF recorded, and a simplified invoice
/// does not need one.
/// </param>
/// <param name="TaxCountryCode">The ISO two-letter country their tax number belongs to.</param>
/// <param name="IsTaxNumberVerified">
/// True when the number has been checked against the tax authority rather than merely typed. It
/// does not gate anything here — an unverified NIF still goes on the document — but it is the
/// difference between a rejected SAF-T file being a surprise and being predictable.
/// </param>
/// <param name="IsCustomer">True when we sell to them, and so may invoice them.</param>
/// <param name="BillingAddress">
/// Where they are registered, or null when no billing address is on file. An export substitutes
/// the tax authority's own placeholder rather than omitting the element, because the element is
/// mandatory and a missing one fails the file.
/// </param>
public sealed record BillingParty(
    Guid PartnerId,
    string Code,
    string LegalName,
    string? TaxNumber,
    string TaxCountryCode,
    bool IsTaxNumberVerified,
    bool IsCustomer,
    BillingPartyAddress? BillingAddress);

/// <summary>A registered address, flattened to the four fields a tax filing asks for.</summary>
/// <param name="AddressDetail">Street and number, on one line.</param>
/// <param name="City">The town.</param>
/// <param name="PostalCode">The postcode, in whatever shape that country uses.</param>
/// <param name="CountryCode">
/// ISO two-letter. Not necessarily the same as the country of the tax number: a Spanish company
/// with a Portuguese NIF is a normal thing at a border trade counter.
/// </param>
public sealed record BillingPartyAddress(
    string AddressDetail,
    string City,
    string PostalCode,
    string CountryCode);
