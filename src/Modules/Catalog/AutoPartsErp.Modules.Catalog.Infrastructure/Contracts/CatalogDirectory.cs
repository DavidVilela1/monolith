using AutoPartsErp.ModuleContracts.Catalog;
using AutoPartsErp.Modules.Catalog.Domain;
using AutoPartsErp.Modules.Catalog.Domain.Parts;
using AutoPartsErp.Modules.Catalog.Infrastructure.Persistence;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace AutoPartsErp.Modules.Catalog.Infrastructure.Contracts;

/// <summary>
/// Catalog's answer to "what is this part, and may I still trade it?".
/// <para>
/// Reads columns, not aggregates. A part carries its cross-references and its fitments, and a
/// five-line order does not need fifty vehicle applications loaded in order to print five
/// descriptions.
/// </para>
/// <para>
/// The two status rules come from <see cref="Part.SellableStatuses"/> and
/// <see cref="Part.PurchasableStatuses"/> rather than being restated here. A projection cannot
/// call the aggregate's computed properties, so the alternative is a second copy of "active, or
/// discontinued and being sold down" living in an adapter — and that copy is what would still be
/// saying yes a year after somebody changed the rule on the aggregate.
/// </para>
/// <para>
/// The tenant filter applies automatically, so a caller can only ever be told about its own
/// company's catalogue.
/// </para>
/// </summary>
public sealed class CatalogDirectory : ICatalogDirectory
{
    private readonly CatalogDbContext _context;

    /// <summary>Initializes the adapter.</summary>
    public CatalogDirectory(CatalogDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<PartDescriptor?> GetAsync(
        Guid partId,
        CancellationToken cancellationToken = default)
    {
        var id = new PartId(partId);

        var row = await _context.Parts
            .AsNoTracking()
            .Where(part => part.Id == id)
            .Select(part => new
            {
                part.Id,
                part.Sku,
                part.Name,
                part.StockUnit,
                part.Status,
                part.RequiresCoreReturn,
                part.SupersededByPartId,
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return row is null
            ? null
            : Describe(
                row.Id,
                row.Sku,
                row.Name,
                row.StockUnit,
                row.Status,
                row.RequiresCoreReturn,
                row.SupersededByPartId);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, PartDescriptor>> GetManyAsync(
        IReadOnlyCollection<Guid> partIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(partIds);

        if (partIds.Count == 0)
        {
            return new Dictionary<Guid, PartDescriptor>();
        }

        List<PartId> ids = [.. partIds.Distinct().Select(id => new PartId(id))];

        var rows = await _context.Parts
            .AsNoTracking()
            .Where(part => ids.Contains(part.Id))
            .Select(part => new
            {
                part.Id,
                part.Sku,
                part.Name,
                part.StockUnit,
                part.Status,
                part.RequiresCoreReturn,
                part.SupersededByPartId,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.ToDictionary(
            row => row.Id.Value,
            row => Describe(
                row.Id,
                row.Sku,
                row.Name,
                row.StockUnit,
                row.Status,
                row.RequiresCoreReturn,
                row.SupersededByPartId));
    }

    /// <inheritdoc />
    public async Task<PartDescriptor?> FindBySkuAsync(
        string sku,
        CancellationToken cancellationToken = default)
    {
        // Through Sku.Create rather than ToUpperInvariant, so this lookup normalizes exactly the
        // way the value object does. A SKU that could never have been stored cannot match
        // anything, so an invalid one is "not found" rather than an error - the caller asked a
        // question and the answer is no.
        Result<Sku> normalized = Sku.Create(sku);

        if (normalized.IsFailure)
        {
            return null;
        }

        Sku wanted = normalized.Value;

        var row = await _context.Parts
            .AsNoTracking()
            .Where(part => part.Sku == wanted)
            .Select(part => new
            {
                part.Id,
                part.Sku,
                part.Name,
                part.StockUnit,
                part.Status,
                part.RequiresCoreReturn,
                part.SupersededByPartId,
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return row is null
            ? null
            : Describe(
                row.Id,
                row.Sku,
                row.Name,
                row.StockUnit,
                row.Status,
                row.RequiresCoreReturn,
                row.SupersededByPartId);
    }

    private static PartDescriptor Describe(
        PartId id,
        Sku sku,
        string name,
        UnitOfMeasure stockUnit,
        PartStatus status,
        bool requiresCoreReturn,
        PartId? supersededBy) =>
        new(
            id.Value,
            sku.Value,
            name,
            stockUnit.Code,
            Part.SellableStatuses.Contains(status),
            Part.PurchasableStatuses.Contains(status),
            requiresCoreReturn,
            supersededBy?.Value);
}
