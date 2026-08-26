using AutoPartsErp.Modules.Abstractions.Http;
using AutoPartsErp.Modules.Abstractions.Modules;
using AutoPartsErp.Modules.Catalog.Application.Brands;
using AutoPartsErp.Modules.Catalog.Application.Categories;
using AutoPartsErp.Modules.Catalog.Application.Contracts;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AutoPartsErp.Modules.Catalog.Presentation.Endpoints;

/// <summary>HTTP routes for brands.</summary>
public sealed class BrandEndpoints : IEndpointGroup
{
    /// <inheritdoc />
    public void Map(IEndpointRouteBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        RouteGroupBuilder brands = group.MapGroup("/brands");

        brands.MapGet("/", ListAsync)
            .WithName("ListBrands")
            .WithSummary("List brands, with the number of parts carrying each one.")
            .Produces<IReadOnlyList<BrandDto>>();

        brands.MapPost("/", CreateAsync)
            .WithName("CreateBrand")
            .WithSummary("Register a new brand.")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> ListAsync(
        IDispatcher dispatcher,
        bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        Result<IReadOnlyList<BrandDto>> result =
            await dispatcher.SendAsync(new ListBrandsQuery(activeOnly), cancellationToken);

        return result.ToOk();
    }

    private static async Task<IResult> CreateAsync(
        IDispatcher dispatcher,
        CreateBrandCommand command,
        CancellationToken cancellationToken)
    {
        Result<Guid> result = await dispatcher.SendAsync(command, cancellationToken);
        return result.ToCreated(id => $"/api/catalog/brands/{id}");
    }
}

/// <summary>HTTP routes for the product hierarchy.</summary>
public sealed class CategoryEndpoints : IEndpointGroup
{
    /// <inheritdoc />
    public void Map(IEndpointRouteBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        RouteGroupBuilder categories = group.MapGroup("/categories");

        categories.MapGet("/", ListAsync)
            .WithName("ListCategories")
            .WithSummary("List categories, with the number of parts filed under each.")
            .Produces<IReadOnlyList<CategoryDto>>();

        categories.MapPost("/", CreateAsync)
            .WithName("CreateCategory")
            .WithSummary("Create a category in the product hierarchy.")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> ListAsync(
        IDispatcher dispatcher,
        bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        Result<IReadOnlyList<CategoryDto>> result =
            await dispatcher.SendAsync(new ListCategoriesQuery(activeOnly), cancellationToken);

        return result.ToOk();
    }

    private static async Task<IResult> CreateAsync(
        IDispatcher dispatcher,
        CreateCategoryCommand command,
        CancellationToken cancellationToken)
    {
        Result<Guid> result = await dispatcher.SendAsync(command, cancellationToken);
        return result.ToCreated(id => $"/api/catalog/categories/{id}");
    }
}
