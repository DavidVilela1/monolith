using AutoPartsErp.Modules.Catalog.Domain.Parts.Events;
using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Catalog.Domain.Parts;

/// <summary>
/// A sellable article in the catalogue, and the aggregate root every other module points at.
/// <para>
/// Everything downstream depends on this object being right: Inventory holds quantities of it,
/// Purchasing orders it, Sales prices and invoices it, Finance values it. That is why the
/// interesting rules live here as behaviour rather than in a service layer, and why the
/// collections are exposed read-only. There is no way to reach past a part and change its
/// cross-references or fitments behind its back.
/// </para>
/// </summary>
public sealed class Part : AggregateRoot<PartId>, IAuditable, ISoftDeletable, ITenantScoped
{
    private readonly List<CrossReference> _crossReferences = [];
    private readonly List<Fitment> _fitments = [];

    private Part(
        PartId id,
        Sku sku,
        PartNumber manufacturerPartNumber,
        BrandId brandId,
        CategoryId categoryId,
        string name,
        UnitOfMeasure stockUnit)
        : base(id)
    {
        Sku = sku;
        ManufacturerPartNumber = manufacturerPartNumber;
        BrandId = brandId;
        CategoryId = categoryId;
        Name = name;
        StockUnit = stockUnit;
        Status = PartStatus.Draft;
        Package = PackageSpec.Unspecified;
        CreatedBy = string.Empty;
    }

    /// <summary>Required by EF Core materialization.</summary>
#pragma warning disable CS8618 // EF Core assigns every property during materialization.
    private Part()
    {
    }
#pragma warning restore CS8618

    /// <summary>The distributor's own stock keeping unit. Unique within a tenant.</summary>
    public Sku Sku { get; private set; }

    /// <summary>The brand's own number for this part. Unique per brand within a tenant.</summary>
    public PartNumber ManufacturerPartNumber { get; private set; }

    /// <summary>The brand that makes the part.</summary>
    public BrandId BrandId { get; private set; }

    /// <summary>Where the part sits in the product hierarchy.</summary>
    public CategoryId CategoryId { get; private set; }

    /// <summary>Short description, as it appears on picking lists and invoices.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Long description for the webshop and catalogue.</summary>
    public string? Description { get; private set; }

    /// <summary>
    /// The unit stock is counted in. Immutable once the part goes live, because every
    /// quantity ever recorded against it is expressed in this unit.
    /// </summary>
    public UnitOfMeasure StockUnit { get; private set; } = UnitOfMeasure.Each;

    /// <summary>Where the part sits in its commercial life.</summary>
    public PartStatus Status { get; private set; } = PartStatus.Draft;

    /// <summary>Shipping characteristics of one selling unit.</summary>
    public PackageSpec Package { get; private set; } = PackageSpec.Unspecified;

    /// <summary>
    /// True for remanufactured parts sold against a returnable core: starters, alternators,
    /// calipers, turbochargers. The customer pays a deposit and gets it back when the old
    /// unit comes in, which means the core is a stock item in its own right.
    /// </summary>
    public bool RequiresCoreReturn { get; private set; }

    /// <summary>The refundable deposit charged when <see cref="RequiresCoreReturn"/> is true.</summary>
    public Money? CoreCharge { get; private set; }

    /// <summary>The part that replaces this one, once it has been discontinued.</summary>
    public PartId? SupersededByPartId { get; private set; }

    /// <summary>Foreign numbers that resolve to this part.</summary>
    public IReadOnlyCollection<CrossReference> CrossReferences => _crossReferences.AsReadOnly();

    /// <summary>Vehicle applications this part fits.</summary>
    public IReadOnlyCollection<Fitment> Fitments => _fitments.AsReadOnly();

    /// <inheritdoc />
    public Guid TenantId { get; set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <inheritdoc />
    public string CreatedBy { get; set; } = string.Empty;

    /// <inheritdoc />
    public DateTimeOffset? ModifiedAtUtc { get; set; }

    /// <inheritdoc />
    public string? ModifiedBy { get; set; }

    /// <inheritdoc />
    public bool IsDeleted { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? DeletedAtUtc { get; set; }

    /// <inheritdoc />
    public string? DeletedBy { get; set; }

    /// <summary>
    /// The statuses in which a part may still be sold: live, or being sold down after being
    /// withdrawn from purchasing.
    /// <para>
    /// Exposed as data rather than only as the <see cref="IsSellable"/> predicate because query
    /// code cannot call a computed property - it has to be translated to SQL. Without this, every
    /// read path re-states the rule as its own <c>WHERE</c> clause and they drift apart, which is
    /// exactly how a draft part nobody has finished setting up ends up offered to a customer.
    /// </para>
    /// </summary>
    public static readonly PartStatus[] SellableStatuses = [PartStatus.Active, PartStatus.Discontinued];

    /// <summary>True when the part can appear on a new sales order.</summary>
    public bool IsSellable => Status is PartStatus.Active or PartStatus.Discontinued;

    /// <summary>True when the part can appear on a new purchase order.</summary>
    public bool IsPurchasable => Status == PartStatus.Active;

    /// <summary>
    /// Registers a new part. It starts in <see cref="PartStatus.Draft"/>: nothing can be bought
    /// or sold until someone has checked the setup and activated it.
    /// </summary>
    /// <param name="sku">The distributor's stock keeping unit.</param>
    /// <param name="manufacturerPartNumber">The brand's own number, as printed.</param>
    /// <param name="brandId">The brand.</param>
    /// <param name="categoryId">The product category.</param>
    /// <param name="name">Short description for documents.</param>
    /// <param name="stockUnit">The unit stock is counted in.</param>
    /// <param name="description">Optional long description.</param>
    public static Result<Part> Create(
        Sku sku,
        PartNumber manufacturerPartNumber,
        BrandId brandId,
        CategoryId categoryId,
        string? name,
        UnitOfMeasure stockUnit,
        string? description = null)
    {
        ArgumentNullException.ThrowIfNull(sku);
        ArgumentNullException.ThrowIfNull(manufacturerPartNumber);
        ArgumentNullException.ThrowIfNull(stockUnit);

        if (brandId.IsEmpty)
        {
            return CatalogErrors.Part.BrandRequired;
        }

        if (categoryId.IsEmpty)
        {
            return CatalogErrors.Part.CategoryRequired;
        }

        Result<string> validatedName = ValidateName(name);
        if (validatedName.IsFailure)
        {
            return Result.Failure<Part>(validatedName.Error);
        }

        var part = new Part(
            PartId.New(),
            sku,
            manufacturerPartNumber,
            brandId,
            categoryId,
            validatedName.Value,
            stockUnit)
        {
            Description = Clean(description),
        };

        part.Raise(new PartCreatedDomainEvent(part.Id, sku.Value, brandId));

        return part;
    }

    /// <summary>Changes the descriptions shown on documents and in the webshop.</summary>
    public Result Describe(string? name, string? description)
    {
        if (Status == PartStatus.Obsolete)
        {
            return CatalogErrors.Part.ObsoleteIsReadOnly;
        }

        Result<string> validatedName = ValidateName(name);
        if (validatedName.IsFailure)
        {
            return Result.Failure(validatedName.Error);
        }

        Name = validatedName.Value;
        Description = Clean(description);

        return Result.Success();
    }

    /// <summary>Moves the part to a different category.</summary>
    public Result Reclassify(CategoryId categoryId)
    {
        if (Status == PartStatus.Obsolete)
        {
            return CatalogErrors.Part.ObsoleteIsReadOnly;
        }

        if (categoryId.IsEmpty)
        {
            return CatalogErrors.Part.CategoryRequired;
        }

        CategoryId = categoryId;
        return Result.Success();
    }

    /// <summary>Records the part's shipping characteristics.</summary>
    public Result SetPackage(PackageSpec package)
    {
        ArgumentNullException.ThrowIfNull(package);

        if (Status == PartStatus.Obsolete)
        {
            return CatalogErrors.Part.ObsoleteIsReadOnly;
        }

        Package = package;
        return Result.Success();
    }

    /// <summary>
    /// Changes the unit stock is counted in. Only possible while the part is still a draft:
    /// afterwards there are quantities, costs and open orders expressed in the old unit, and
    /// silently reinterpreting them is how a warehouse ends up with 500 litres of oil recorded
    /// as 500 drums.
    /// </summary>
    public Result ChangeStockUnit(UnitOfMeasure unit)
    {
        ArgumentNullException.ThrowIfNull(unit);

        if (Status != PartStatus.Draft)
        {
            return CatalogErrors.Part.StockUnitLocked;
        }

        if (unit == StockUnit)
        {
            return Result.Success();
        }

        string previous = StockUnit.Code;
        StockUnit = unit;
        Raise(new PartStockUnitChangedDomainEvent(Id, previous, unit.Code));

        return Result.Success();
    }

    /// <summary>Marks the part as sold against a returnable core, with the deposit charged.</summary>
    /// <param name="coreCharge">The refundable deposit. Must be greater than zero.</param>
    public Result RequireCoreReturn(Money coreCharge)
    {
        ArgumentNullException.ThrowIfNull(coreCharge);

        if (Status == PartStatus.Obsolete)
        {
            return CatalogErrors.Part.ObsoleteIsReadOnly;
        }

        if (coreCharge.IsNegative || coreCharge.IsZero)
        {
            return CatalogErrors.Part.CoreChargeMustBePositive;
        }

        RequiresCoreReturn = true;
        CoreCharge = coreCharge;

        return Result.Success();
    }

    /// <summary>Removes the core requirement, for example when a brand stops running an exchange programme.</summary>
    public Result ClearCoreReturn()
    {
        if (Status == PartStatus.Obsolete)
        {
            return CatalogErrors.Part.ObsoleteIsReadOnly;
        }

        RequiresCoreReturn = false;
        CoreCharge = null;

        return Result.Success();
    }

    /// <summary>
    /// Makes the part orderable and sellable. Refuses parts that are not set up well enough
    /// to reach the counter: an unnamed part with no fitment data is a support call waiting
    /// to happen.
    /// </summary>
    public Result Activate()
    {
        if (Status == PartStatus.Active)
        {
            return Result.Success();
        }

        if (Status != PartStatus.Draft)
        {
            return CatalogErrors.Part.CannotReactivate;
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            return CatalogErrors.Part.NameRequired;
        }

        if (RequiresCoreReturn && CoreCharge is null)
        {
            return CatalogErrors.Part.CoreChargeRequired;
        }

        Status = PartStatus.Active;
        Raise(new PartActivatedDomainEvent(Id, Sku.Value));

        return Result.Success();
    }

    /// <summary>
    /// Withdraws the part from purchasing while remaining stock is sold down.
    /// </summary>
    /// <param name="supersededBy">The replacement part, when the brand has named one.</param>
    public Result Discontinue(PartId? supersededBy = null)
    {
        if (Status is PartStatus.Draft)
        {
            return CatalogErrors.Part.CannotDiscontinueDraft;
        }

        if (Status is PartStatus.Obsolete)
        {
            return CatalogErrors.Part.ObsoleteIsReadOnly;
        }

        if (supersededBy is { } replacement)
        {
            if (replacement == Id)
            {
                return CatalogErrors.Part.CannotSupersedeItself;
            }

            if (replacement.IsEmpty)
            {
                return CatalogErrors.Part.SupersessionInvalid;
            }
        }

        Status = PartStatus.Discontinued;
        SupersededByPartId = supersededBy;
        Raise(new PartDiscontinuedDomainEvent(Id, supersededBy));

        return Result.Success();
    }

    /// <summary>Retires the part completely. It stays in the database so history still resolves.</summary>
    public Result MakeObsolete()
    {
        if (Status == PartStatus.Obsolete)
        {
            return Result.Success();
        }

        if (Status == PartStatus.Draft)
        {
            return CatalogErrors.Part.CannotObsoleteDraft;
        }

        Status = PartStatus.Obsolete;
        Raise(new PartObsoletedDomainEvent(Id));

        return Result.Success();
    }

    /// <summary>Links a foreign number to this part.</summary>
    public Result AddCrossReference(CrossReference crossReference)
    {
        ArgumentNullException.ThrowIfNull(crossReference);

        if (Status == PartStatus.Obsolete)
        {
            return CatalogErrors.Part.ObsoleteIsReadOnly;
        }

        if (string.Equals(
                crossReference.NormalizedNumber,
                ManufacturerPartNumber.Normalized,
                StringComparison.Ordinal))
        {
            return CatalogErrors.CrossReference.SameAsOwnNumber;
        }

        if (_crossReferences.Exists(existing => existing.IsSameReferenceAs(crossReference)))
        {
            return CatalogErrors.CrossReference.Duplicate;
        }

        _crossReferences.Add(crossReference);
        Raise(new PartCrossReferenceAddedDomainEvent(
            Id, crossReference.Kind, crossReference.NormalizedNumber));

        return Result.Success();
    }

    /// <summary>Removes a previously linked foreign number.</summary>
    public Result RemoveCrossReference(CrossReference crossReference)
    {
        ArgumentNullException.ThrowIfNull(crossReference);

        if (Status == PartStatus.Obsolete)
        {
            return CatalogErrors.Part.ObsoleteIsReadOnly;
        }

        int index = _crossReferences.FindIndex(existing => existing.IsSameReferenceAs(crossReference));
        if (index < 0)
        {
            return CatalogErrors.CrossReference.NotFound;
        }

        _crossReferences.RemoveAt(index);
        return Result.Success();
    }

    /// <summary>Records that this part fits a vehicle application.</summary>
    public Result AddFitment(Fitment fitment)
    {
        ArgumentNullException.ThrowIfNull(fitment);

        if (Status == PartStatus.Obsolete)
        {
            return CatalogErrors.Part.ObsoleteIsReadOnly;
        }

        if (_fitments.Exists(existing => existing.DescribesSameApplicationAs(fitment)))
        {
            return CatalogErrors.Fitment.Duplicate;
        }

        _fitments.Add(fitment);
        Raise(new PartFitmentAddedDomainEvent(
            Id, fitment.Make, fitment.Model, fitment.YearFrom, fitment.YearTo));

        return Result.Success();
    }

    /// <summary>Removes a vehicle application.</summary>
    public Result RemoveFitment(Fitment fitment)
    {
        ArgumentNullException.ThrowIfNull(fitment);

        if (Status == PartStatus.Obsolete)
        {
            return CatalogErrors.Part.ObsoleteIsReadOnly;
        }

        int index = _fitments.FindIndex(existing => existing.DescribesSameApplicationAs(fitment));
        if (index < 0)
        {
            return CatalogErrors.Fitment.NotFound;
        }

        _fitments.RemoveAt(index);
        return Result.Success();
    }

    /// <summary>True when this part fits the supplied vehicle.</summary>
    /// <param name="make">Vehicle manufacturer.</param>
    /// <param name="model">Model designation.</param>
    /// <param name="year">Model year.</param>
    public bool FitsVehicle(string make, string model, int year)
    {
        string normalizedMake = make?.Trim().ToUpperInvariant() ?? string.Empty;
        string normalizedModel = model?.Trim().ToUpperInvariant() ?? string.Empty;

        return _fitments.Exists(fitment =>
            string.Equals(fitment.Make, normalizedMake, StringComparison.Ordinal)
            && string.Equals(fitment.Model, normalizedModel, StringComparison.Ordinal)
            && fitment.CoversYear(year));
    }

    /// <summary>True when any recorded number, own or foreign, matches the supplied text.</summary>
    /// <param name="searchTerm">Raw user input; normalized before comparison.</param>
    public bool MatchesNumber(string? searchTerm)
    {
        string normalized = PartNumber.Normalize(searchTerm);
        if (normalized.Length == 0)
        {
            return false;
        }

        return string.Equals(ManufacturerPartNumber.Normalized, normalized, StringComparison.Ordinal)
            || string.Equals(PartNumber.Normalize(Sku.Value), normalized, StringComparison.Ordinal)
            || _crossReferences.Exists(reference =>
                string.Equals(reference.NormalizedNumber, normalized, StringComparison.Ordinal));
    }

    private static Result<string> ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return CatalogErrors.Part.NameRequired;
        }

        string trimmed = name.Trim();

        return trimmed.Length > 200
            ? CatalogErrors.Part.NameTooLong
            : trimmed;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
