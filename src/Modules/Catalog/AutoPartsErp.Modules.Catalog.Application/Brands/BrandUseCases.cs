using AutoPartsErp.Modules.Catalog.Application.Abstractions;
using AutoPartsErp.Modules.Catalog.Application.Contracts;
using AutoPartsErp.Modules.Catalog.Domain;
using AutoPartsErp.Modules.Catalog.Domain.Brands;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Catalog.Application.Brands;

/// <summary>Registers a new brand.</summary>
/// <param name="Code">Short code, uppercased automatically: BOSCH, VW, FEBI.</param>
/// <param name="Name">Full brand name.</param>
/// <param name="IsOriginalEquipment">True for vehicle manufacturers and OE suppliers.</param>
/// <param name="CountryCode">Optional ISO country code.</param>
public sealed record CreateBrandCommand(
    string Code,
    string Name,
    bool IsOriginalEquipment = false,
    string? CountryCode = null) : ICommand<Guid>;

/// <summary>Checks the shape of a <see cref="CreateBrandCommand"/>.</summary>
public sealed class CreateBrandCommandValidator : IValidator<CreateBrandCommand>
{
    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(
        CreateBrandCommand instance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var failures = new List<ValidationFailure>();

        if (string.IsNullOrWhiteSpace(instance.Code))
        {
            failures.Add(new ValidationFailure(nameof(instance.Code), "required", "A brand code is required."));
        }

        if (string.IsNullOrWhiteSpace(instance.Name))
        {
            failures.Add(new ValidationFailure(nameof(instance.Name), "required", "A brand name is required."));
        }

        if (!string.IsNullOrWhiteSpace(instance.CountryCode) && instance.CountryCode.Trim().Length != 2)
        {
            failures.Add(new ValidationFailure(
                nameof(instance.CountryCode),
                "invalid_country",
                "A country code must be two letters, for example DE or PT."));
        }

        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}

/// <summary>Creates the brand.</summary>
public sealed class CreateBrandCommandHandler : ICommandHandler<CreateBrandCommand, Guid>
{
    private readonly IBrandRepository _brands;
    private readonly ICatalogUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public CreateBrandCommandHandler(IBrandRepository brands, ICatalogUnitOfWork unitOfWork)
    {
        _brands = brands;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result<Guid>> HandleAsync(
        CreateBrandCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string code = request.Code?.Trim().ToUpperInvariant() ?? string.Empty;

        if (await _brands.CodeExistsAsync(code, null, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<Guid>(CatalogErrors.Brand.CodeAlreadyExists(code));
        }

        Result<Brand> brand = Brand.Create(
            request.Code,
            request.Name,
            request.IsOriginalEquipment,
            request.CountryCode);

        if (brand.IsFailure)
        {
            return Result.Failure<Guid>(brand.Error);
        }

        _brands.Add(brand.Value);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return brand.Value.Id.Value;
    }
}

/// <summary>Lists brands for pickers and filters.</summary>
/// <param name="ActiveOnly">True to exclude brands that are no longer used for new parts.</param>
public sealed record ListBrandsQuery(bool ActiveOnly = true) : IQuery<IReadOnlyList<BrandDto>>;

/// <summary>Serves <see cref="ListBrandsQuery"/> from the read store.</summary>
public sealed class ListBrandsQueryHandler : IQueryHandler<ListBrandsQuery, IReadOnlyList<BrandDto>>
{
    private readonly ICatalogReadStore _readStore;

    /// <summary>Initializes the handler.</summary>
    public ListBrandsQueryHandler(ICatalogReadStore readStore)
    {
        _readStore = readStore;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<BrandDto>>> HandleAsync(
        ListBrandsQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        IReadOnlyList<BrandDto> brands = await _readStore
            .ListBrandsAsync(request.ActiveOnly, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(brands);
    }
}
