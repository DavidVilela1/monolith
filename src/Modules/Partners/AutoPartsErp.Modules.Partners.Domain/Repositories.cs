using AutoPartsErp.Modules.Partners.Domain.Partners;
using AutoPartsErp.SharedKernel.Abstractions;

namespace AutoPartsErp.Modules.Partners.Domain;

/// <summary>Write-side access to partners.</summary>
public interface IPartnerRepository : IRepository<Partner, PartnerId>
{
    /// <summary>Loads a partner by code, or null when there is no such partner.</summary>
    Task<Partner?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>True when the code is already taken.</summary>
    Task<bool> CodeExistsAsync(
        string code,
        PartnerId? excludingPartnerId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// True when another partner already carries this tax number.
    /// <para>
    /// Worth checking, because the duplicate it prevents is the expensive kind: the same company
    /// entered twice, once as a customer and once as a supplier, with two credit conversations
    /// and no way to net the balances.
    /// </para>
    /// </summary>
    Task<bool> TaxNumberExistsAsync(
        string countryCode,
        string taxNumber,
        PartnerId? excludingPartnerId = null,
        CancellationToken cancellationToken = default);
}

/// <summary>The Partners module's unit of work.</summary>
public interface IPartnersUnitOfWork : IUnitOfWork;
