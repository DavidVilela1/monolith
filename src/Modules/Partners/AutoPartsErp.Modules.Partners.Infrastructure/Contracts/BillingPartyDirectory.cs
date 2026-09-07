using AutoPartsErp.ModuleContracts.Partners;
using AutoPartsErp.Modules.Partners.Domain;
using AutoPartsErp.Modules.Partners.Domain.Partners;
using AutoPartsErp.Modules.Partners.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AutoPartsErp.Modules.Partners.Infrastructure.Contracts;

/// <summary>
/// Partners' answer to "who are they, for the purpose of putting them on an invoice?".
/// <para>
/// The whole aggregate rather than a projection, in the end, and for a reason worth writing down:
/// the billing address is the one address out of several that <see cref="Partner.BillingAddress"/>
/// picks by kind, and picking it in a query would be a second copy of that rule. The cost is the
/// addresses and contacts collections coming along; <c>AsSingleQuery</c> keeps that to one round
/// trip rather than three, exactly as the trading directory next door does.
/// </para>
/// </summary>
public sealed class BillingPartyDirectory : IBillingPartyDirectory
{
    private readonly PartnersDbContext _context;

    /// <summary>Initializes the adapter.</summary>
    public BillingPartyDirectory(PartnersDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<BillingParty?> GetAsync(
        Guid partnerId,
        CancellationToken cancellationToken = default)
    {
        var id = new PartnerId(partnerId);

        Partner? partner = await _context.Partners
            .AsNoTracking()
            .AsSingleQuery()
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken)
            .ConfigureAwait(false);

        return partner is null ? null : Map(partner);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, BillingParty>> GetManyAsync(
        IReadOnlyCollection<Guid> partnerIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(partnerIds);

        if (partnerIds.Count == 0)
        {
            return new Dictionary<Guid, BillingParty>();
        }

        // Distinct first: a month of documents against one good customer arrives here as the same
        // identifier a thousand times, and an IN list with a thousand copies of one Guid is a
        // query plan nobody wants to read.
        List<PartnerId> ids = [.. partnerIds.Distinct().Select(value => new PartnerId(value))];

        List<Partner> partners = await _context.Partners
            .AsNoTracking()
            .AsSingleQuery()
            .Where(partner => ids.Contains(partner.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return partners.ToDictionary(partner => partner.Id.Value, Map);
    }

    private static BillingParty Map(Partner partner)
    {
        Address? billing = partner.BillingAddress;

        return new BillingParty(
            partner.Id.Value,
            partner.Code,
            partner.LegalName,

            // An empty tax number is stored as an empty string and travels as null, because the
            // contract's "null means none on file" is a fact about the partner and "" is a fact
            // about the column. A consumer testing for null should not also have to test for "".
            string.IsNullOrWhiteSpace(partner.TaxNumber.Value) ? null : partner.TaxNumber.Value,
            partner.TaxNumber.CountryCode,
            partner.TaxNumber.IsVerified,
            partner.IsCustomer,
            billing is null
                ? null
                : new BillingPartyAddress(
                    // Two lines flattened to one, because every tax filing this feeds has a single
                    // AddressDetail field and joining them here beats each consumer inventing its
                    // own separator.
                    string.IsNullOrWhiteSpace(billing.Line2)
                        ? billing.Line1
                        : $"{billing.Line1}, {billing.Line2}",
                    billing.City,
                    billing.Postcode,
                    billing.CountryCode));
    }
}
