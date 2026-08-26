using AutoPartsErp.Modules.Catalog.Application.Abstractions;
using AutoPartsErp.Modules.Catalog.Application.Contracts;
using AutoPartsErp.Modules.Catalog.Domain;
using AutoPartsErp.Modules.Catalog.Domain.Categories;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Catalog.Application.Categories;

/// <summary>Creates a category in the product hierarchy.</summary>
/// <param name="Code">Short code, uppercased automatically.</param>
/// <param name="Name">Display name.</param>
/// <param name="ParentId">Optional parent category.</param>
/// <param name="SortOrder">Sort order among siblings.</param>
public sealed record CreateCategoryCommand(
    string Code,
    string Name,
    Guid? ParentId = null,
    int SortOrder = 0) : ICommand<Guid>;

/// <summary>Checks the shape of a <see cref="CreateCategoryCommand"/>.</summary>
public sealed class CreateCategoryCommandValidator : IValidator<CreateCategoryCommand>
{
    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(
        CreateCategoryCommand instance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var failures = new List<ValidationFailure>();

        if (string.IsNullOrWhiteSpace(instance.Code))
        {
            failures.Add(new ValidationFailure(nameof(instance.Code), "required", "A category code is required."));
        }

        if (string.IsNullOrWhiteSpace(instance.Name))
        {
            failures.Add(new ValidationFailure(nameof(instance.Name), "required", "A category name is required."));
        }

        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}

/// <summary>Creates the category.</summary>
public sealed class CreateCategoryCommandHandler : ICommandHandler<CreateCategoryCommand, Guid>
{
    private readonly ICategoryRepository _categories;
    private readonly ICatalogUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public CreateCategoryCommandHandler(ICategoryRepository categories, ICatalogUnitOfWork unitOfWork)
    {
        _categories = categories;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result<Guid>> HandleAsync(
        CreateCategoryCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string code = request.Code?.Trim().ToUpperInvariant() ?? string.Empty;

        if (await _categories.CodeExistsAsync(code, null, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<Guid>(CatalogErrors.Category.CodeAlreadyExists(code));
        }

        CategoryId? parentId = null;

        if (request.ParentId is { } parent)
        {
            var candidate = new CategoryId(parent);

            if (!await _categories.ExistsAsync(candidate, cancellationToken).ConfigureAwait(false))
            {
                return Result.Failure<Guid>(CatalogErrors.Category.NotFound(parent.ToString()));
            }

            parentId = candidate;
        }

        Result<PartCategory> category = PartCategory.Create(
            request.Code,
            request.Name,
            parentId,
            request.SortOrder);

        if (category.IsFailure)
        {
            return Result.Failure<Guid>(category.Error);
        }

        _categories.Add(category.Value);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return category.Value.Id.Value;
    }
}

/// <summary>Lists categories for pickers and navigation.</summary>
/// <param name="ActiveOnly">True to exclude categories no longer used for new parts.</param>
public sealed record ListCategoriesQuery(bool ActiveOnly = true) : IQuery<IReadOnlyList<CategoryDto>>;

/// <summary>Serves <see cref="ListCategoriesQuery"/> from the read store.</summary>
public sealed class ListCategoriesQueryHandler : IQueryHandler<ListCategoriesQuery, IReadOnlyList<CategoryDto>>
{
    private readonly ICatalogReadStore _readStore;

    /// <summary>Initializes the handler.</summary>
    public ListCategoriesQueryHandler(ICatalogReadStore readStore)
    {
        _readStore = readStore;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<CategoryDto>>> HandleAsync(
        ListCategoriesQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        IReadOnlyList<CategoryDto> categories = await _readStore
            .ListCategoriesAsync(request.ActiveOnly, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(categories);
    }
}
