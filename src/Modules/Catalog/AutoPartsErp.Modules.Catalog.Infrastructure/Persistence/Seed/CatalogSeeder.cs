using AutoPartsErp.Modules.Catalog.Domain;
using AutoPartsErp.Modules.Catalog.Domain.Brands;
using AutoPartsErp.Modules.Catalog.Domain.Categories;
using AutoPartsErp.Modules.Catalog.Domain.Parts;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AutoPartsErp.Modules.Catalog.Infrastructure.Persistence.Seed;

/// <summary>
/// Puts a small but realistic slice of a parts catalogue into an empty database.
/// <para>
/// This exists so the API is worth looking at the first time it starts, and so the fitment and
/// cross-reference searches have something to find. It only ever runs against an empty catalogue,
/// and it is wired up for Development only.
/// </para>
/// </summary>
public sealed class CatalogSeeder
{
    private readonly CatalogDbContext _context;
    private readonly ILogger<CatalogSeeder> _logger;

    /// <summary>Initializes the seeder.</summary>
    public CatalogSeeder(CatalogDbContext context, ILogger<CatalogSeeder> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>Seeds the catalogue if, and only if, it is currently empty.</summary>
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (await _context.Parts.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            _logger.LogInformation("Catalogue already contains parts; skipping seed.");
            return;
        }

        Brand bosch = Require(Brand.Create("BOSCH", "Robert Bosch GmbH", isOriginalEquipment: true, "DE"));
        Brand vw = Require(Brand.Create("VW", "Volkswagen AG", isOriginalEquipment: true, "DE"));
        Brand febi = Require(Brand.Create("FEBI", "febi bilstein", isOriginalEquipment: false, "DE"));

        _context.Brands.AddRange(bosch, vw, febi);

        PartCategory braking = Require(PartCategory.Create("BRK", "Braking", null, 10));
        PartCategory brakePads = Require(PartCategory.Create("BRK-PAD", "Brake Pads", braking.Id, 10));
        PartCategory filters = Require(PartCategory.Create("FLT", "Filters", null, 20));
        PartCategory oilFilters = Require(PartCategory.Create("FLT-OIL", "Oil Filters", filters.Id, 10));

        _context.Categories.AddRange(braking, brakePads, filters, oilFilters);

        _context.Parts.AddRange(
            BuildBrakePads(febi.Id, brakePads.Id),
            BuildOilFilter(bosch.Id, oilFilters.Id),
            BuildDraftPart(vw.Id, brakePads.Id));

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Seeded the catalogue with 3 brands, 4 categories and 3 parts.");
    }

    private static Part BuildBrakePads(BrandId brandId, CategoryId categoryId)
    {
        Part part = Require(Part.Create(
            Require(Sku.Create("BP-1188")),
            Require(PartNumber.Create("16 232 70")),
            brandId,
            categoryId,
            "Brake pad set, front axle",
            UnitOfMeasure.Set,
            "Front axle brake pad set including wear sensor."));

        Apply(part.SetPackage(Require(PackageSpec.Create(2.4m, 180m, 120m, 70m))));

        // The number a customer reads off the old part, and the number a competitor prints.
        Apply(part.AddCrossReference(Require(
            CrossReference.Create(CrossReferenceKind.Oem, "5Q0 698 151 A", "VW"))));
        Apply(part.AddCrossReference(Require(
            CrossReference.Create(CrossReferenceKind.Competitor, "0 986 494 659", "BOSCH"))));

        Apply(part.AddFitment(Require(
            Fitment.Create("VOLKSWAGEN", "GOLF VII", 2012, 2020, "CJZA", "FRONT"))));
        Apply(part.AddFitment(Require(
            Fitment.Create("VOLKSWAGEN", "GOLF VII", 2012, 2020, "CRBC", "FRONT"))));
        Apply(part.AddFitment(Require(
            Fitment.Create("SKODA", "OCTAVIA III", 2013, 2019, null, "FRONT"))));

        Apply(part.Activate());

        return part;
    }

    private static Part BuildOilFilter(BrandId brandId, CategoryId categoryId)
    {
        Part part = Require(Part.Create(
            Require(Sku.Create("OF-4501")),
            Require(PartNumber.Create("F 026 407 006")),
            brandId,
            categoryId,
            "Oil filter",
            UnitOfMeasure.Each,
            "Spin-on oil filter with integrated bypass valve."));

        Apply(part.SetPackage(Require(PackageSpec.Create(0.35m, 95m, 95m, 120m))));

        Apply(part.AddCrossReference(Require(
            CrossReference.Create(CrossReferenceKind.Oem, "03C 115 561 H", "VW"))));
        Apply(part.AddCrossReference(Require(
            CrossReference.Create(CrossReferenceKind.Supersedes, "F026407023", "BOSCH", "Replaced 03/2019"))));

        Apply(part.AddFitment(Require(
            Fitment.Create("VOLKSWAGEN", "GOLF VII", 2012, 2020, "CJZA"))));
        Apply(part.AddFitment(Require(
            Fitment.Create("AUDI", "A3 8V", 2012, 2020, "CXSA"))));

        Apply(part.Activate());

        return part;
    }

    private static Part BuildDraftPart(BrandId brandId, CategoryId categoryId)
    {
        // Left as a draft on purpose: it shows what a part looks like before anyone
        // has checked the setup, and gives the activation endpoint something to act on.
        Part part = Require(Part.Create(
            Require(Sku.Create("BC-2210")),
            Require(PartNumber.Create("5Q0615123B")),
            brandId,
            categoryId,
            "Brake caliper, front left (reman)",
            UnitOfMeasure.Each,
            "Remanufactured brake caliper sold against a returnable core."));

        Apply(part.SetPackage(Require(PackageSpec.Create(4.8m, 260m, 180m, 150m))));
        Apply(part.RequireCoreReturn(Money.Of(85m, Currency.Eur)));

        Apply(part.AddFitment(Require(
            Fitment.Create("VOLKSWAGEN", "GOLF VII", 2012, 2020, null, "FRONT LEFT"))));

        return part;
    }

    private static T Require<T>(Result<T> result) =>
        result.IsSuccess
            ? result.Value
            : throw new InvalidOperationException($"Seed data is invalid: {result.Error}");

    private static void Apply(Result result)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"Seed data is invalid: {result.Error}");
        }
    }
}
