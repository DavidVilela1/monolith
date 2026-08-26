using AutoPartsErp.Modules.Catalog.Domain;
using AutoPartsErp.Modules.Catalog.Domain.Parts;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Catalog.Application.Parts.Commands;

/// <summary>
/// Registers a new part in the catalogue. The part starts as a draft and is not orderable
/// until someone activates it.
/// </summary>
/// <param name="Sku">The distributor's stock keeping unit.</param>
/// <param name="ManufacturerPartNumber">The brand's own number, as printed.</param>
/// <param name="BrandId">The brand.</param>
/// <param name="CategoryId">The product category.</param>
/// <param name="Name">Short description for documents.</param>
/// <param name="StockUnitCode">The unit stock is counted in, for example EA or L.</param>
/// <param name="Description">Optional long description.</param>
public sealed record CreatePartCommand(
    string Sku,
    string ManufacturerPartNumber,
    Guid BrandId,
    Guid CategoryId,
    string Name,
    string StockUnitCode,
    string? Description = null) : ICommand<Guid>;

/// <summary>Checks the shape of a <see cref="CreatePartCommand"/> before it reaches the handler.</summary>
public sealed class CreatePartCommandValidator : IValidator<CreatePartCommand>
{
    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(
        CreatePartCommand instance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var failures = new List<ValidationFailure>();

        if (string.IsNullOrWhiteSpace(instance.Sku))
        {
            failures.Add(new ValidationFailure(nameof(instance.Sku), "required", "A SKU is required."));
        }

        if (string.IsNullOrWhiteSpace(instance.ManufacturerPartNumber))
        {
            failures.Add(new ValidationFailure(
                nameof(instance.ManufacturerPartNumber), "required", "A manufacturer part number is required."));
        }

        if (string.IsNullOrWhiteSpace(instance.Name))
        {
            failures.Add(new ValidationFailure(
                nameof(instance.Name), "required", "A part description is required."));
        }

        if (instance.BrandId == Guid.Empty)
        {
            failures.Add(new ValidationFailure(nameof(instance.BrandId), "required", "A brand is required."));
        }

        if (instance.CategoryId == Guid.Empty)
        {
            failures.Add(new ValidationFailure(
                nameof(instance.CategoryId), "required", "A category is required."));
        }

        if (!UnitOfMeasure.TryFromCode(instance.StockUnitCode, out _))
        {
            failures.Add(new ValidationFailure(
                nameof(instance.StockUnitCode),
                "unknown_unit",
                $"'{instance.StockUnitCode}' is not a known unit of measure."));
        }

        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}

/// <summary>
/// Creates the part.
/// <para>
/// The uniqueness checks here are a courtesy that turns a database exception into a clear
/// message; the database still carries unique indexes, because two counter staff can create
/// the same SKU in the same millisecond and only the database can arbitrate that.
/// </para>
/// </summary>
public sealed class CreatePartCommandHandler : ICommandHandler<CreatePartCommand, Guid>
{
    private readonly IPartRepository _parts;
    private readonly IBrandRepository _brands;
    private readonly ICategoryRepository _categories;
    private readonly ICatalogUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public CreatePartCommandHandler(
        IPartRepository parts,
        IBrandRepository brands,
        ICategoryRepository categories,
        ICatalogUnitOfWork unitOfWork)
    {
        _parts = parts;
        _brands = brands;
        _categories = categories;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result<Guid>> HandleAsync(
        CreatePartCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Sku> sku = Sku.Create(request.Sku);
        if (sku.IsFailure)
        {
            return Result.Failure<Guid>(sku.Error);
        }

        Result<PartNumber> partNumber = PartNumber.Create(request.ManufacturerPartNumber);
        if (partNumber.IsFailure)
        {
            return Result.Failure<Guid>(partNumber.Error);
        }

        var brandId = new BrandId(request.BrandId);
        var categoryId = new CategoryId(request.CategoryId);

        Domain.Brands.Brand? brand = await _brands.GetByIdAsync(brandId, cancellationToken).ConfigureAwait(false);
        if (brand is null)
        {
            return Result.Failure<Guid>(CatalogErrors.Brand.NotFound(request.BrandId.ToString()));
        }

        if (!brand.IsActive)
        {
            return Result.Failure<Guid>(CatalogErrors.Brand.Inactive);
        }

        if (!await _categories.ExistsAsync(categoryId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<Guid>(CatalogErrors.Category.NotFound(request.CategoryId.ToString()));
        }

        if (await _parts.SkuExistsAsync(sku.Value, null, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<Guid>(CatalogErrors.Part.SkuAlreadyExists(sku.Value.Value));
        }

        bool numberTaken = await _parts
            .ManufacturerPartNumberExistsAsync(brandId, partNumber.Value.Normalized, null, cancellationToken)
            .ConfigureAwait(false);

        if (numberTaken)
        {
            return Result.Failure<Guid>(
                CatalogErrors.Part.PartNumberAlreadyExists(partNumber.Value.Display));
        }

        UnitOfMeasure stockUnit = UnitOfMeasure.FromCode(request.StockUnitCode);

        Result<Part> part = Part.Create(
            sku.Value,
            partNumber.Value,
            brandId,
            categoryId,
            request.Name,
            stockUnit,
            request.Description);

        if (part.IsFailure)
        {
            return Result.Failure<Guid>(part.Error);
        }

        _parts.Add(part.Value);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return part.Value.Id.Value;
    }
}
