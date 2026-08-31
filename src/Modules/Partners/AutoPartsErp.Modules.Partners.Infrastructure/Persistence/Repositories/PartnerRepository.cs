using AutoPartsErp.Modules.Partners.Domain;
using AutoPartsErp.Modules.Partners.Domain.Partners;
using Microsoft.EntityFrameworkCore;

namespace AutoPartsErp.Modules.Partners.Infrastructure.Persistence.Repositories;

/// <summary>Write-side access to partners.</summary>
public sealed class PartnerRepository : IPartnerRepository
{
    private readonly PartnersDbContext _context;

    /// <summary>Initializes the repository.</summary>
    public PartnerRepository(PartnersDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public Task<Partner?> GetByIdAsync(PartnerId id, CancellationToken cancellationToken = default) =>
        _context.Partners.FirstOrDefaultAsync(partner => partner.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsAsync(PartnerId id, CancellationToken cancellationToken = default) =>
        _context.Partners.AnyAsync(partner => partner.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<Partner?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        string normalized = code?.Trim().ToUpperInvariant() ?? string.Empty;
        return _context.Partners.FirstOrDefaultAsync(
            partner => partner.Code == normalized, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> CodeExistsAsync(
        string code,
        PartnerId? excludingPartnerId = null,
        CancellationToken cancellationToken = default)
    {
        string normalized = code?.Trim().ToUpperInvariant() ?? string.Empty;

        IQueryable<Partner> query = _context.Partners.Where(partner => partner.Code == normalized);

        if (excludingPartnerId is { } excluded)
        {
            query = query.Where(partner => partner.Id != excluded);
        }

        return query.AnyAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> TaxNumberExistsAsync(
        string countryCode,
        string taxNumber,
        PartnerId? excludingPartnerId = null,
        CancellationToken cancellationToken = default)
    {
        string country = countryCode?.Trim().ToUpperInvariant() ?? string.Empty;
        string number = taxNumber?.Trim().ToUpperInvariant() ?? string.Empty;

        IQueryable<Partner> query = _context.Partners.Where(partner =>
            partner.TaxNumber.CountryCode == country && partner.TaxNumber.Value == number);

        if (excludingPartnerId is { } excluded)
        {
            query = query.Where(partner => partner.Id != excluded);
        }

        return query.AnyAsync(cancellationToken);
    }

    /// <inheritdoc />
    public void Add(Partner aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.Partners.Add(aggregate);
    }

    /// <inheritdoc />
    public void Remove(Partner aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.Partners.Remove(aggregate);
    }
}
