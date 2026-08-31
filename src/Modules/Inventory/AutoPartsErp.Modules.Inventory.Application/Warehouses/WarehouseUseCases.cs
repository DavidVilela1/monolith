using AutoPartsErp.Modules.Inventory.Application.Abstractions;
using AutoPartsErp.Modules.Inventory.Application.Contracts;
using AutoPartsErp.Modules.Inventory.Domain;
using AutoPartsErp.Modules.Inventory.Domain.Warehouses;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Inventory.Application.Warehouses;

/// <summary>Registers a warehouse.</summary>
/// <param name="Code">Short code, uppercased automatically: MAIN, BR01.</param>
/// <param name="Name">Display name.</param>
/// <param name="Kind">Depot, Branch, Van, Receiving, Quarantine or CoreReturn.</param>
/// <param name="RequiresBinTracking">Whether movements must name a bin.</param>
public sealed record CreateWarehouseCommand(
    string Code,
    string Name,
    string Kind = "Depot",
    bool RequiresBinTracking = false) : ICommand<Guid>;

/// <summary>Checks the shape of a <see cref="CreateWarehouseCommand"/>.</summary>
public sealed class CreateWarehouseCommandValidator : IValidator<CreateWarehouseCommand>
{
    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(
        CreateWarehouseCommand instance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var failures = new List<ValidationFailure>();

        if (string.IsNullOrWhiteSpace(instance.Code))
        {
            failures.Add(new ValidationFailure(nameof(instance.Code), "required", "A warehouse code is required."));
        }

        if (string.IsNullOrWhiteSpace(instance.Name))
        {
            failures.Add(new ValidationFailure(nameof(instance.Name), "required", "A warehouse name is required."));
        }

        if (!Enum.TryParse(instance.Kind, ignoreCase: true, out WarehouseKind kind) ||
            kind == WarehouseKind.Unknown)
        {
            failures.Add(new ValidationFailure(
                nameof(instance.Kind),
                "unknown",
                "Kind must be one of: Depot, Branch, Van, Receiving, Quarantine, CoreReturn."));
        }

        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}

/// <summary>Creates the warehouse.</summary>
public sealed class CreateWarehouseCommandHandler : ICommandHandler<CreateWarehouseCommand, Guid>
{
    private readonly IWarehouseRepository _warehouses;
    private readonly IInventoryUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public CreateWarehouseCommandHandler(IWarehouseRepository warehouses, IInventoryUnitOfWork unitOfWork)
    {
        _warehouses = warehouses;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result<Guid>> HandleAsync(
        CreateWarehouseCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string code = request.Code?.Trim().ToUpperInvariant() ?? string.Empty;

        if (await _warehouses.CodeExistsAsync(code, null, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<Guid>(InventoryErrors.Warehouse.CodeAlreadyExists(code));
        }

        var kind = Enum.Parse<WarehouseKind>(request.Kind, ignoreCase: true);

        Result<Warehouse> warehouse = Warehouse.Create(
            request.Code, request.Name, kind, request.RequiresBinTracking);

        if (warehouse.IsFailure)
        {
            return Result.Failure<Guid>(warehouse.Error);
        }

        _warehouses.Add(warehouse.Value);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return warehouse.Value.Id.Value;
    }
}

/// <summary>Sets the replenishment policy for a part in a warehouse.</summary>
/// <param name="PartId">The part.</param>
/// <param name="WarehouseId">The warehouse.</param>
/// <param name="ReorderPoint">The level that triggers a reorder, or null to clear the policy.</param>
/// <param name="ReorderQuantity">How much to order, or null to clear the policy.</param>
public sealed record SetReplenishmentPolicyCommand(
    Guid PartId,
    Guid WarehouseId,
    decimal? ReorderPoint,
    decimal? ReorderQuantity) : ICommand;

/// <summary>Applies the replenishment policy.</summary>
public sealed class SetReplenishmentPolicyCommandHandler : ICommandHandler<SetReplenishmentPolicyCommand>
{
    private readonly IStockItemRepository _stockItems;
    private readonly IInventoryUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public SetReplenishmentPolicyCommandHandler(
        IStockItemRepository stockItems,
        IInventoryUnitOfWork unitOfWork)
    {
        _stockItems = stockItems;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        SetReplenishmentPolicyCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Domain.Stock.StockItem? stockItem = await _stockItems
            .GetAsync(new PartRef(request.PartId), new WarehouseId(request.WarehouseId), cancellationToken)
            .ConfigureAwait(false);

        if (stockItem is null)
        {
            return InventoryErrors.Stock.NotFound(
                request.PartId.ToString(), request.WarehouseId.ToString());
        }

        Result applied = stockItem.SetReplenishmentPolicy(request.ReorderPoint, request.ReorderQuantity);

        if (applied.IsFailure)
        {
            return applied;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>Lists warehouses.</summary>
/// <param name="ActiveOnly">True to exclude closed sites.</param>
public sealed record ListWarehousesQuery(bool ActiveOnly = true) : IQuery<IReadOnlyList<WarehouseDto>>;

/// <summary>Serves <see cref="ListWarehousesQuery"/> from the read store.</summary>
public sealed class ListWarehousesQueryHandler : IQueryHandler<ListWarehousesQuery, IReadOnlyList<WarehouseDto>>
{
    private readonly IInventoryReadStore _readStore;

    /// <summary>Initializes the handler.</summary>
    public ListWarehousesQueryHandler(IInventoryReadStore readStore)
    {
        _readStore = readStore;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<WarehouseDto>>> HandleAsync(
        ListWarehousesQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        IReadOnlyList<WarehouseDto> warehouses = await _readStore
            .ListWarehousesAsync(request.ActiveOnly, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(warehouses);
    }
}
