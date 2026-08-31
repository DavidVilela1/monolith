using AutoPartsErp.Modules.Inventory.Domain;
using AutoPartsErp.Modules.Inventory.Domain.Stock;
using AutoPartsErp.Modules.Inventory.Domain.Warehouses;
using AutoPartsErp.SharedKernel.Abstractions;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Inventory.Application.Stock.Commands;

/// <summary>Brings stock into a warehouse against a document.</summary>
/// <param name="PartId">The part received.</param>
/// <param name="WarehouseId">Where it landed.</param>
/// <param name="Quantity">How much. Must be positive.</param>
/// <param name="ReferenceType">The kind of document, e.g. GoodsReceipt.</param>
/// <param name="ReferenceNumber">The document number.</param>
/// <param name="Note">Optional explanation.</param>
public sealed record ReceiveStockCommand(
    Guid PartId,
    Guid WarehouseId,
    decimal Quantity,
    string ReferenceType,
    string ReferenceNumber,
    string? Note = null) : ICommand;

/// <summary>Checks the shape of a <see cref="ReceiveStockCommand"/>.</summary>
public sealed class ReceiveStockCommandValidator : IValidator<ReceiveStockCommand>
{
    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(
        ReceiveStockCommand instance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var failures = new List<ValidationFailure>();
        StockValidation.CheckMovement(
            failures, instance.Quantity, instance.ReferenceType, instance.ReferenceNumber);

        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}

/// <summary>Receives the stock and writes the ledger entry.</summary>
public sealed class ReceiveStockCommandHandler : ICommandHandler<ReceiveStockCommand>
{
    private readonly IStockItemRepository _stockItems;
    private readonly IWarehouseRepository _warehouses;
    private readonly IStockMovementRepository _movements;
    private readonly IInventoryUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    /// <summary>Initializes the handler.</summary>
    public ReceiveStockCommandHandler(
        IStockItemRepository stockItems,
        IWarehouseRepository warehouses,
        IStockMovementRepository movements,
        IInventoryUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _stockItems = stockItems;
        _warehouses = warehouses;
        _movements = movements;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        ReceiveStockCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<StockContext> context = await StockContext
            .LoadAsync(_stockItems, _warehouses, request.PartId, request.WarehouseId, cancellationToken)
            .ConfigureAwait(false);

        if (context.IsFailure)
        {
            return Result.FromError(context.Error);
        }

        Result<MovementReference> reference = StockValidation.BuildReference(
            request.ReferenceType, request.ReferenceNumber, request.Note);

        if (reference.IsFailure)
        {
            return Result.FromError(reference.Error);
        }

        Result<StockMovement> movement = context.Value.StockItem
            .Receive(request.Quantity, reference.Value, _clock.UtcNow);

        if (movement.IsFailure)
        {
            return Result.FromError(movement.Error);
        }

        _movements.Add(movement.Value);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>Takes stock out of a warehouse against a document.</summary>
/// <param name="PartId">The part issued.</param>
/// <param name="WarehouseId">Where it left from.</param>
/// <param name="Quantity">How much. Must be positive.</param>
/// <param name="ReferenceType">The kind of document, e.g. SalesOrder.</param>
/// <param name="ReferenceNumber">The document number.</param>
/// <param name="Note">Optional explanation.</param>
public sealed record IssueStockCommand(
    Guid PartId,
    Guid WarehouseId,
    decimal Quantity,
    string ReferenceType,
    string ReferenceNumber,
    string? Note = null) : ICommand;

/// <summary>Checks the shape of an <see cref="IssueStockCommand"/>.</summary>
public sealed class IssueStockCommandValidator : IValidator<IssueStockCommand>
{
    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(
        IssueStockCommand instance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var failures = new List<ValidationFailure>();
        StockValidation.CheckMovement(
            failures, instance.Quantity, instance.ReferenceType, instance.ReferenceNumber);

        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}

/// <summary>
/// Issues the stock. Whether the balance may go below zero is the warehouse's decision,
/// read here and passed into the aggregate rather than assumed.
/// </summary>
public sealed class IssueStockCommandHandler : ICommandHandler<IssueStockCommand>
{
    private readonly IStockItemRepository _stockItems;
    private readonly IWarehouseRepository _warehouses;
    private readonly IStockMovementRepository _movements;
    private readonly IInventoryUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    /// <summary>Initializes the handler.</summary>
    public IssueStockCommandHandler(
        IStockItemRepository stockItems,
        IWarehouseRepository warehouses,
        IStockMovementRepository movements,
        IInventoryUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _stockItems = stockItems;
        _warehouses = warehouses;
        _movements = movements;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        IssueStockCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<StockContext> context = await StockContext
            .LoadAsync(_stockItems, _warehouses, request.PartId, request.WarehouseId, cancellationToken)
            .ConfigureAwait(false);

        if (context.IsFailure)
        {
            return Result.FromError(context.Error);
        }

        Result<MovementReference> reference = StockValidation.BuildReference(
            request.ReferenceType, request.ReferenceNumber, request.Note);

        if (reference.IsFailure)
        {
            return Result.FromError(reference.Error);
        }

        Result<StockMovement> movement = context.Value.StockItem.Issue(
            request.Quantity,
            reference.Value,
            _clock.UtcNow,
            context.Value.Warehouse.AllowsNegativeStock);

        if (movement.IsFailure)
        {
            return Result.FromError(movement.Error);
        }

        _movements.Add(movement.Value);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>Corrects a balance to a counted figure.</summary>
/// <param name="PartId">The part counted.</param>
/// <param name="WarehouseId">Where it was counted.</param>
/// <param name="CountedQuantity">What the count found.</param>
/// <param name="ReferenceNumber">The count sheet or adjustment number.</param>
/// <param name="Note">Why the figure differs. Required: an unexplained adjustment is not auditable.</param>
public sealed record AdjustStockCommand(
    Guid PartId,
    Guid WarehouseId,
    decimal CountedQuantity,
    string ReferenceNumber,
    string Note) : ICommand;

/// <summary>Checks the shape of an <see cref="AdjustStockCommand"/>.</summary>
public sealed class AdjustStockCommandValidator : IValidator<AdjustStockCommand>
{
    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(
        AdjustStockCommand instance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var failures = new List<ValidationFailure>();

        if (instance.CountedQuantity < 0m)
        {
            failures.Add(new ValidationFailure(
                nameof(instance.CountedQuantity), "negative", "A counted quantity cannot be negative."));
        }

        if (string.IsNullOrWhiteSpace(instance.ReferenceNumber))
        {
            failures.Add(new ValidationFailure(
                nameof(instance.ReferenceNumber), "required", "A count or adjustment number is required."));
        }

        // Deliberately stricter than the other movements. Receipts and issues explain themselves
        // through their source document; an adjustment is somebody overriding the system, and in
        // six months the only thing that will explain it is this sentence.
        if (string.IsNullOrWhiteSpace(instance.Note))
        {
            failures.Add(new ValidationFailure(
                nameof(instance.Note), "required", "Say why the counted figure differs from the system."));
        }

        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}

/// <summary>Applies the correction and records the difference.</summary>
public sealed class AdjustStockCommandHandler : ICommandHandler<AdjustStockCommand>
{
    private readonly IStockItemRepository _stockItems;
    private readonly IWarehouseRepository _warehouses;
    private readonly IStockMovementRepository _movements;
    private readonly IInventoryUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    /// <summary>Initializes the handler.</summary>
    public AdjustStockCommandHandler(
        IStockItemRepository stockItems,
        IWarehouseRepository warehouses,
        IStockMovementRepository movements,
        IInventoryUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _stockItems = stockItems;
        _warehouses = warehouses;
        _movements = movements;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        AdjustStockCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<StockContext> context = await StockContext
            .LoadAsync(_stockItems, _warehouses, request.PartId, request.WarehouseId, cancellationToken)
            .ConfigureAwait(false);

        if (context.IsFailure)
        {
            return Result.FromError(context.Error);
        }

        Result<MovementReference> reference = MovementReference.Create(
            ReferenceType.StockCount, request.ReferenceNumber, request.Note);

        if (reference.IsFailure)
        {
            return Result.FromError(reference.Error);
        }

        Result<StockMovement> movement = context.Value.StockItem
            .AdjustTo(request.CountedQuantity, reference.Value, _clock.UtcNow);

        if (movement.IsFailure)
        {
            return Result.FromError(movement.Error);
        }

        _movements.Add(movement.Value);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>
/// The part, the warehouse and the balance, loaded together.
/// Every stock command needs all three and fails in the same three ways, so the loading and the
/// error messages live in one place instead of being copied into each handler.
/// </summary>
internal sealed record StockContext(Warehouse Warehouse, StockItem StockItem)
{
    public static async Task<Result<StockContext>> LoadAsync(
        IStockItemRepository stockItems,
        IWarehouseRepository warehouses,
        Guid partId,
        Guid warehouseId,
        CancellationToken cancellationToken)
    {
        var warehouseKey = new WarehouseId(warehouseId);

        Warehouse? warehouse = await warehouses
            .GetByIdAsync(warehouseKey, cancellationToken)
            .ConfigureAwait(false);

        if (warehouse is null)
        {
            return Result.Failure<StockContext>(InventoryErrors.Warehouse.NotFound(warehouseId.ToString()));
        }

        if (!warehouse.IsActive)
        {
            return Result.Failure<StockContext>(InventoryErrors.Warehouse.Inactive);
        }

        StockItem? stockItem = await stockItems
            .GetAsync(new PartRef(partId), warehouseKey, cancellationToken)
            .ConfigureAwait(false);

        if (stockItem is null)
        {
            return Result.Failure<StockContext>(
                InventoryErrors.Stock.NotFound(partId.ToString(), warehouse.Code));
        }

        return new StockContext(warehouse, stockItem);
    }
}

/// <summary>Shared request-shape checks for the stock commands.</summary>
internal static class StockValidation
{
    public static void CheckMovement(
        List<ValidationFailure> failures,
        decimal quantity,
        string referenceType,
        string referenceNumber)
    {
        if (quantity <= 0m)
        {
            failures.Add(new ValidationFailure(
                "Quantity", "not_positive", "A movement quantity must be greater than zero."));
        }

        if (string.IsNullOrWhiteSpace(referenceNumber))
        {
            failures.Add(new ValidationFailure(
                "ReferenceNumber", "required", "A document number is required."));
        }

        if (!Enum.TryParse(referenceType, ignoreCase: true, out ReferenceType parsed) ||
            parsed == ReferenceType.Unknown)
        {
            failures.Add(new ValidationFailure(
                "ReferenceType",
                "unknown",
                "Reference type must be one of: GoodsReceipt, PurchaseOrder, SalesOrder, CounterSale, " +
                "Quote, CustomerReturn, SupplierReturn, StockTransfer, StockCount, Adjustment, CoreReturn."));
        }
    }

    public static Result<MovementReference> BuildReference(
        string referenceType,
        string referenceNumber,
        string? note)
    {
        if (!Enum.TryParse(referenceType, ignoreCase: true, out ReferenceType parsed))
        {
            return Result.Failure<MovementReference>(InventoryErrors.Movement.ReferenceTypeRequired);
        }

        return MovementReference.Create(parsed, referenceNumber, note);
    }
}
