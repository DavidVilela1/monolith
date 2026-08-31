using AutoPartsErp.Modules.Abstractions.Http;
using AutoPartsErp.Modules.Abstractions.Modules;
using AutoPartsErp.Modules.Inventory.Application.Contracts;
using AutoPartsErp.Modules.Inventory.Application.Warehouses;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AutoPartsErp.Modules.Inventory.Presentation.Endpoints;

/// <summary>HTTP routes for warehouses.</summary>
public sealed class WarehouseEndpoints : IEndpointGroup
{
    /// <inheritdoc />
    public void Map(IEndpointRouteBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        RouteGroupBuilder warehouses = group.MapGroup("/warehouses");

        warehouses.MapGet("/", ListAsync)
            .WithName("ListWarehouses")
            .WithSummary("List warehouses, with how many parts hold stock in each.")
            .Produces<IReadOnlyList<WarehouseDto>>();

        warehouses.MapPost("/", CreateAsync)
            .WithName("CreateWarehouse")
            .WithSummary("Register a warehouse.")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> ListAsync(
        IDispatcher dispatcher,
        bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        Result<IReadOnlyList<WarehouseDto>> result =
            await dispatcher.SendAsync(new ListWarehousesQuery(activeOnly), cancellationToken);

        return result.ToOk();
    }

    private static async Task<IResult> CreateAsync(
        IDispatcher dispatcher,
        CreateWarehouseCommand command,
        CancellationToken cancellationToken)
    {
        Result<Guid> result = await dispatcher.SendAsync(command, cancellationToken);
        return result.ToCreated(id => $"/api/inventory/warehouses/{id}");
    }
}
