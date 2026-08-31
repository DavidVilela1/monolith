using AutoPartsErp.Modules.Partners.Application.Contracts;
using AutoPartsErp.SharedKernel.Paging;

namespace AutoPartsErp.Modules.Partners.Application.Abstractions;

/// <summary>The read side of the Partners module.</summary>
public interface IPartnersReadStore
{
    /// <summary>Loads one partner in full, or null when it does not exist.</summary>
    Task<PartnerDetail?> GetPartnerAsync(Guid partnerId, CancellationToken cancellationToken = default);

    /// <summary>Loads one partner by code, the way the counter looks one up.</summary>
    Task<PartnerDetail?> GetPartnerByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches partners by code, name or tax number.
    /// </summary>
    /// <param name="criteria">What to look for.</param>
    /// <param name="page">Which page to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<PagedResult<PartnerSummary>> SearchAsync(
        PartnerSearchCriteria criteria,
        PageRequest page,
        CancellationToken cancellationToken = default);
}

/// <summary>What to look for when searching partners.</summary>
public sealed record PartnerSearchCriteria
{
    /// <summary>Free text, matched against code, legal name, trading name and tax number.</summary>
    public string? Term { get; init; }

    /// <summary>Restrict to customers.</summary>
    public bool? IsCustomer { get; init; }

    /// <summary>Restrict to suppliers.</summary>
    public bool? IsSupplier { get; init; }

    /// <summary>Restrict to one status: Active, OnHold or Closed.</summary>
    public string? Status { get; init; }
}
