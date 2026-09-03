using AutoPartsErp.ModuleContracts.Partners;
using AutoPartsErp.Modules.Partners.Domain;
using AutoPartsErp.Modules.Partners.Domain.Partners;
using AutoPartsErp.Modules.Partners.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AutoPartsErp.Modules.Partners.Infrastructure.Contracts;

/// <summary>
/// Partners' answer to "may we trade with them, and what do we call them?".
/// <para>
/// The two <c>Can…</c> flags are the aggregate's own properties, not a rule reimplemented here.
/// <c>Partner.CanPlacePurchaseOrders</c> already knows that it means "a supplier, and not on
/// hold"; a second copy of that rule in an adapter is how two parts of a system start
/// disagreeing about whether somebody is allowed to buy.
/// </para>
/// </summary>
public sealed class PartnerDirectory : IPartnerDirectory
{
    private readonly PartnersDbContext _context;

    /// <summary>Initializes the adapter.</summary>
    public PartnerDirectory(PartnersDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<PartnerTradingStatus?> GetAsync(
        Guid partnerId,
        CancellationToken cancellationToken = default)
    {
        var id = new PartnerId(partnerId);

        // The whole aggregate rather than a projection, because the two Can... flags are
        // computed on it and a projection would mean reimplementing them here - a second copy
        // of "a supplier, and not on hold" is how two parts of a system start disagreeing.
        //
        // AsSingleQuery because this context splits queries by default, and the aggregate owns
        // addresses and contacts: without it, one directory lookup is three round trips for two
        // collections nobody asked for.
        Partner? partner = await _context.Partners
            .AsNoTracking()
            .AsSingleQuery()
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken)
            .ConfigureAwait(false);

        return partner is null
            ? null
            : new PartnerTradingStatus(
                partner.Id.Value,
                partner.Code,
                partner.LegalName,
                partner.IsCustomer,
                partner.IsSupplier,
                partner.CanTakeNewOrders,
                partner.CanPlacePurchaseOrders);
    }
}
