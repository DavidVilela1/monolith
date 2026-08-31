using AutoPartsErp.Modules.Partners.Domain;
using AutoPartsErp.Modules.Partners.Application.Abstractions;
using AutoPartsErp.Modules.Partners.Application.Contracts;
using AutoPartsErp.Modules.Partners.Domain.Partners;
using AutoPartsErp.SharedKernel.Paging;
using Microsoft.EntityFrameworkCore;

namespace AutoPartsErp.Modules.Partners.Infrastructure.Persistence.ReadStore;

/// <summary>Serves the Partners module's queries.</summary>
public sealed class PartnersReadStore : IPartnersReadStore
{
    private readonly PartnersDbContext _context;

    /// <summary>Initializes the read store.</summary>
    public PartnersReadStore(PartnersDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<PartnerDetail?> GetPartnerAsync(
        Guid partnerId,
        CancellationToken cancellationToken = default)
    {
        var id = new PartnerId(partnerId);

        Partner? partner = await _context.Partners
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            .ConfigureAwait(false);

        return partner is null ? null : MapDetail(partner);
    }

    /// <inheritdoc />
    public async Task<PartnerDetail?> GetPartnerByCodeAsync(
        string code,
        CancellationToken cancellationToken = default)
    {
        string normalized = code?.Trim().ToUpperInvariant() ?? string.Empty;

        Partner? partner = await _context.Partners
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Code == normalized, cancellationToken)
            .ConfigureAwait(false);

        return partner is null ? null : MapDetail(partner);
    }

    /// <inheritdoc />
    public async Task<PagedResult<PartnerSummary>> SearchAsync(
        PartnerSearchCriteria criteria,
        PageRequest page,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        ArgumentNullException.ThrowIfNull(page);

        IQueryable<Partner> query = _context.Partners.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(criteria.Term))
        {
            string term = criteria.Term.Trim();
            string upper = term.ToUpperInvariant();

            query = query.Where(partner =>
                EF.Functions.Like(partner.Code, $"{upper}%")
                || EF.Functions.ILike(partner.LegalName, $"%{term}%")
                || (partner.TradingName != null && EF.Functions.ILike(partner.TradingName, $"%{term}%"))
                || EF.Functions.Like(partner.TaxNumber.Value, $"{upper}%"));
        }

        if (criteria.IsCustomer == true)
        {
            query = query.Where(partner => (partner.Roles & PartnerRoles.Customer) != 0);
        }

        if (criteria.IsSupplier == true)
        {
            query = query.Where(partner => (partner.Roles & PartnerRoles.Supplier) != 0);
        }

        if (!string.IsNullOrWhiteSpace(criteria.Status)
            && Enum.TryParse(criteria.Status, ignoreCase: true, out PartnerStatus status))
        {
            query = query.Where(partner => partner.Status == status);
        }

        int total = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        if (total == 0)
        {
            return PagedResult<PartnerSummary>.Empty(page.Page, page.PageSize);
        }

        List<Partner> rows = await query
            .OrderBy(partner => partner.Code)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<PartnerSummary> items = [.. rows.Select(partner => new PartnerSummary(
            partner.Id.Value,
            partner.Code,
            partner.TradingName ?? partner.LegalName,
            partner.TaxNumber.Formatted,
            DescribeRoles(partner.Roles),
            partner.Status.ToString(),
            partner.BillingAddress?.City,
            partner.CustomerTerms?.CreditLimit.Amount))];

        return PagedResult<PartnerSummary>.Create(items, page.Page, page.PageSize, total);
    }

    private static string DescribeRoles(PartnerRoles roles) => roles switch
    {
        PartnerRoles.None => "None",
        PartnerRoles.Customer => "Customer",
        PartnerRoles.Supplier => "Supplier",
        _ => "Customer, Supplier",
    };

    private static PartnerDetail MapDetail(Partner partner) => new()
    {
        Id = partner.Id.Value,
        Code = partner.Code,
        LegalName = partner.LegalName,
        TradingName = partner.TradingName,
        TaxCountryCode = partner.TaxNumber.CountryCode,
        TaxNumber = partner.TaxNumber.Value,
        TaxNumberVerified = partner.TaxNumber.IsVerified,
        Roles = DescribeRoles(partner.Roles),
        Status = partner.Status.ToString(),
        HoldReason = partner.HoldReason,
        CanTakeNewOrders = partner.CanTakeNewOrders,
        CanPlacePurchaseOrders = partner.CanPlacePurchaseOrders,
        CreditLimit = partner.CustomerTerms?.CreditLimit.Amount,
        CreditCurrency = partner.CustomerTerms?.CreditLimit.Currency.Code,
        PaymentDueInDays = partner.CustomerTerms?.PaymentTerms.DueInDays,
        PaymentEndOfMonth = partner.CustomerTerms?.PaymentTerms.EndOfMonth,
        PaymentMethod = partner.CustomerTerms?.PaymentTerms.Method.ToString(),
        PriceListCode = partner.CustomerTerms?.PriceListCode,
        SupplierLeadTimeDays = partner.SupplierTerms?.LeadTimeDays,
        OurAccountNumber = partner.SupplierTerms?.OurAccountNumber,
        Addresses = [.. partner.Addresses.Select(address => new AddressDto(
            address.Kind.ToString(),
            address.Line1,
            address.Line2,
            address.Postcode,
            address.City,
            address.CountryCode,
            address.Notes))],
        Contacts = [.. partner.Contacts.Select(contact => new ContactDto(
            contact.Name,
            contact.Role,
            contact.Email,
            contact.Phone,
            contact.IsPrimary))],
    };
}
