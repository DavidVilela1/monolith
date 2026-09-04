using AutoPartsErp.Modules.Pricing.Application.Abstractions;
using AutoPartsErp.Modules.Pricing.Application.Contracts;
using AutoPartsErp.Modules.Pricing.Domain;
using AutoPartsErp.Modules.Pricing.Domain.Customers;
using AutoPartsErp.Modules.Pricing.Domain.PriceLists;
using AutoPartsErp.SharedKernel.Paging;
using Microsoft.EntityFrameworkCore;

namespace AutoPartsErp.Modules.Pricing.Infrastructure.Persistence.ReadStore;

/// <summary>
/// The read side of the Pricing module: projections straight out of the same schema, never
/// through the aggregates.
/// </summary>
public sealed class PricingReadStore : IPricingReadStore
{
    private readonly PricingDbContext _context;

    /// <summary>Initializes the read store.</summary>
    public PricingReadStore(PricingDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<PriceListSummary?> GetListAsync(
        Guid priceListId,
        CancellationToken cancellationToken = default)
    {
        var id = new PriceListId(priceListId);

        ListRow? row = await Project(_context.PriceLists.AsNoTracking().Where(list => list.Id == id))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return row is null ? null : ToSummary(row);
    }

    /// <inheritdoc />
    public async Task<PriceListSummary?> GetListByCodeAsync(
        string code,
        CancellationToken cancellationToken = default)
    {
        string normalized = code?.Trim().ToUpperInvariant() ?? string.Empty;

        ListRow? row = await Project(
                _context.PriceLists.AsNoTracking().Where(list => list.Code == normalized))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return row is null ? null : ToSummary(row);
    }

    /// <inheritdoc />
    public async Task<PagedResult<PriceListSummary>> SearchListsAsync(
        PriceListSearchCriteria criteria,
        PageRequest page,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        ArgumentNullException.ThrowIfNull(page);

        IQueryable<PriceList> query = _context.PriceLists.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(criteria.Term))
        {
            string term = criteria.Term.Trim();
            query = query.Where(list =>
                EF.Functions.ILike(list.Code, $"%{term}%")
                || EF.Functions.ILike(list.Name, $"%{term}%"));
        }

        if (Enum.TryParse(criteria.Kind, ignoreCase: true, out PriceListKind kind)
            && kind != PriceListKind.Unknown)
        {
            query = query.Where(list => list.Kind == kind);
        }

        if (Enum.TryParse(criteria.Status, ignoreCase: true, out PriceListStatus status)
            && status != PriceListStatus.Unknown)
        {
            query = query.Where(list => list.Status == status);
        }

        if (criteria.EffectiveOnly)
        {
            DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
            query = query.Where(list =>
                list.Status == PriceListStatus.Active
                && (list.EffectiveFrom == null || list.EffectiveFrom <= today)
                && (list.EffectiveTo == null || list.EffectiveTo >= today));
        }

        int total = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        List<ListRow> rows = await Project(query)
            .OrderByDescending(list => list.IsDefault)
            .ThenBy(list => list.Code)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PagedResult<PriceListSummary>
        {
            Items = [.. rows.Select(ToSummary)],
            Page = page.Page,
            PageSize = page.PageSize,
            TotalCount = total,
        };
    }

    /// <inheritdoc />
    public async Task<PagedResult<PriceListEntryDto>> ListPricesAsync(
        Guid priceListId,
        PageRequest page,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);

        var id = new PriceListId(priceListId);

        IQueryable<PriceListEntry> query = _context.PriceListEntries
            .AsNoTracking()
            .Where(entry => entry.PriceListId == id);

        int total = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        // Ordered by part id rather than by SKU. Sorting by something a buyer would recognise
        // would mean joining to a table in another module's schema, which this context is not
        // allowed to see - the screen puts the two together instead.
        List<PriceListEntry> rows = await query
            .OrderBy(entry => entry.PartId)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PagedResult<PriceListEntryDto>
        {
            Items = [.. rows.Select(ToDto)],
            Page = page.Page,
            PageSize = page.PageSize,
            TotalCount = total,
        };
    }

    /// <inheritdoc />
    public async Task<PriceListEntryDto?> GetPriceAsync(
        Guid priceListId,
        Guid partId,
        CancellationToken cancellationToken = default)
    {
        var listId = new PriceListId(priceListId);
        var part = new PartRef(partId);

        PriceListEntry? entry = await _context.PriceListEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.PriceListId == listId && item.PartId == part,
                cancellationToken)
            .ConfigureAwait(false);

        return entry is null ? null : ToDto(entry);
    }

    /// <inheritdoc />
    public async Task<CustomerPricingDto?> GetAgreementAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        var customer = new CustomerRef(customerId);

        AgreementRow? row = await ProjectAgreements(
                _context.CustomerAgreements.AsNoTracking()
                    .Where(agreement => agreement.CustomerId == customer))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return row is null ? null : ToAgreement(row);
    }

    /// <inheritdoc />
    public async Task<PagedResult<CustomerPricingDto>> ListAgreementsForListAsync(
        Guid priceListId,
        PageRequest page,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);

        var id = new PriceListId(priceListId);

        IQueryable<CustomerPricing> query = _context.CustomerAgreements
            .AsNoTracking()
            .Where(agreement => agreement.PriceListId == id);

        int total = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        List<AgreementRow> rows = await ProjectAgreements(query)
            .OrderBy(agreement => agreement.CustomerId)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PagedResult<CustomerPricingDto>
        {
            Items = [.. rows.Select(ToAgreement)],
            Page = page.Page,
            PageSize = page.PageSize,
            TotalCount = total,
        };
    }

    // The projections stop at the columns and the mapping to a DTO happens in memory, because
    // almost everything interesting on these rows is value-converted - a PriceListId is a Guid in
    // the database and a struct in C#, and an enum is stored as text. Reaching for ".Value" or
    // ".ToString()" inside a Select is how a query that reads fine turns into one EF cannot
    // translate, or worse, one it translates by fetching the table and filtering in the client.

    private IQueryable<ListRow> Project(IQueryable<PriceList> lists) =>
        lists.Select(list => new ListRow(
            list.Id,
            list.Code,
            list.Name,
            list.CurrencyCode,
            list.Kind,
            list.Status,
            list.EffectiveFrom,
            list.EffectiveTo,
            list.IsDefault,
            _context.PriceListEntries.Count(entry => entry.PriceListId == list.Id)));

    private IQueryable<AgreementRow> ProjectAgreements(IQueryable<CustomerPricing> agreements) =>
        agreements.Select(agreement => new AgreementRow(
            agreement.Id,
            agreement.CustomerId,
            agreement.PriceListId,
            _context.PriceLists
                .Where(list => list.Id == agreement.PriceListId)
                .Select(list => list.Code)
                .FirstOrDefault(),
            agreement.DiscountPercent,
            agreement.EffectiveFrom,
            agreement.EffectiveTo,
            agreement.Note));

    private static PriceListSummary ToSummary(ListRow row) =>
        new(
            row.Id.Value,
            row.Code,
            row.Name,
            row.CurrencyCode,
            row.Kind.ToString(),
            row.Status.ToString(),
            row.EffectiveFrom,
            row.EffectiveTo,
            row.IsDefault,
            row.PricedParts);

    private static CustomerPricingDto ToAgreement(AgreementRow row) =>
        new(
            row.Id.Value,
            row.CustomerId.Value,
            row.PriceListId.Value,
            row.PriceListCode ?? string.Empty,
            row.DiscountPercent,
            row.EffectiveFrom,
            row.EffectiveTo,
            row.Note);

    private static PriceListEntryDto ToDto(PriceListEntry entry) =>
        new(
            entry.Id.Value,
            entry.PartId.Value,
            [.. entry.Breaks.Select(item =>
                new PriceBreakDto(item.MinimumQuantity, item.UnitPrice.Amount))]);

    private sealed record ListRow(
        PriceListId Id,
        string Code,
        string Name,
        string CurrencyCode,
        PriceListKind Kind,
        PriceListStatus Status,
        DateOnly? EffectiveFrom,
        DateOnly? EffectiveTo,
        bool IsDefault,
        int PricedParts);

    private sealed record AgreementRow(
        CustomerPricingId Id,
        CustomerRef CustomerId,
        PriceListId PriceListId,
        string? PriceListCode,
        decimal DiscountPercent,
        DateOnly? EffectiveFrom,
        DateOnly? EffectiveTo,
        string? Note);
}
