using AutoPartsErp.Modules.Inventory.Domain;
using AutoPartsErp.Modules.Inventory.Domain.Stock;
using AutoPartsErp.SharedKernel.Abstractions;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Inventory.Application.Stock.Commands;

/// <summary>Holds stock back for a document without moving it.</summary>
/// <param name="PartId">The part to hold.</param>
/// <param name="WarehouseId">Where to hold it.</param>
/// <param name="Quantity">How much. Must be positive and currently available.</param>
/// <param name="ReferenceType">What is claiming it, e.g. Quote or SalesOrder.</param>
/// <param name="ReferenceNumber">The document number.</param>
/// <param name="ExpiresInMinutes">
/// How long the claim lasts. A quote that nobody converts must give its stock back on its own,
/// or the shelf fills with quantity reserved for orders that never happen.
/// </param>
public sealed record ReserveStockCommand(
    Guid PartId,
    Guid WarehouseId,
    decimal Quantity,
    string ReferenceType,
    string ReferenceNumber,
    int? ExpiresInMinutes = null) : ICommand<Guid>;

/// <summary>Checks the shape of a <see cref="ReserveStockCommand"/>.</summary>
public sealed class ReserveStockCommandValidator : IValidator<ReserveStockCommand>
{
    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(
        ReserveStockCommand instance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var failures = new List<ValidationFailure>();
        StockValidation.CheckMovement(
            failures, instance.Quantity, instance.ReferenceType, instance.ReferenceNumber);

        if (instance.ExpiresInMinutes is <= 0)
        {
            failures.Add(new ValidationFailure(
                nameof(instance.ExpiresInMinutes),
                "not_positive",
                "An expiry must be at least one minute away, or omitted for a claim that does not lapse."));
        }

        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}

/// <summary>Places the reservation and returns its identifier.</summary>
public sealed class ReserveStockCommandHandler : ICommandHandler<ReserveStockCommand, Guid>
{
    private readonly IStockItemRepository _stockItems;
    private readonly IWarehouseRepository _warehouses;
    private readonly IInventoryUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    /// <summary>Initializes the handler.</summary>
    public ReserveStockCommandHandler(
        IStockItemRepository stockItems,
        IWarehouseRepository warehouses,
        IInventoryUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _stockItems = stockItems;
        _warehouses = warehouses;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result<Guid>> HandleAsync(
        ReserveStockCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<StockContext> context = await StockContext
            .LoadAsync(_stockItems, _warehouses, request.PartId, request.WarehouseId, cancellationToken)
            .ConfigureAwait(false);

        if (context.IsFailure)
        {
            return Result.Failure<Guid>(context.Error);
        }

        Result<MovementReference> reference = StockValidation.BuildReference(
            request.ReferenceType, request.ReferenceNumber, null);

        if (reference.IsFailure)
        {
            return Result.Failure<Guid>(reference.Error);
        }

        DateTimeOffset now = _clock.UtcNow;
        DateTimeOffset? expiry = request.ExpiresInMinutes is { } minutes
            ? now.AddMinutes(minutes)
            : null;

        Result<StockReservation> reservation = context.Value.StockItem
            .Reserve(request.Quantity, reference.Value, now, expiry);

        if (reservation.IsFailure)
        {
            return Result.Failure<Guid>(reservation.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return reservation.Value.Id.Value;
    }
}

/// <summary>Gives a claim back, returning its quantity to available stock.</summary>
/// <param name="PartId">The part.</param>
/// <param name="WarehouseId">The warehouse.</param>
/// <param name="ReservationId">The claim to release.</param>
public sealed record ReleaseReservationCommand(
    Guid PartId,
    Guid WarehouseId,
    Guid ReservationId) : ICommand;

/// <summary>Releases the reservation.</summary>
public sealed class ReleaseReservationCommandHandler : ICommandHandler<ReleaseReservationCommand>
{
    private readonly IStockItemRepository _stockItems;
    private readonly IWarehouseRepository _warehouses;
    private readonly IInventoryUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public ReleaseReservationCommandHandler(
        IStockItemRepository stockItems,
        IWarehouseRepository warehouses,
        IInventoryUnitOfWork unitOfWork)
    {
        _stockItems = stockItems;
        _warehouses = warehouses;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        ReleaseReservationCommand request,
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

        Result released = context.Value.StockItem.Release(new ReservationId(request.ReservationId));

        if (released.IsFailure)
        {
            return released;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>
/// Issues stock against an existing claim: the picker took what was promised.
/// One step rather than release-then-issue, so the two can never drift apart.
/// </summary>
/// <param name="PartId">The part.</param>
/// <param name="WarehouseId">The warehouse.</param>
/// <param name="ReservationId">The claim being picked.</param>
public sealed record FulfilReservationCommand(
    Guid PartId,
    Guid WarehouseId,
    Guid ReservationId) : ICommand;

/// <summary>Fulfils the reservation and writes the ledger entry.</summary>
public sealed class FulfilReservationCommandHandler : ICommandHandler<FulfilReservationCommand>
{
    private readonly IStockItemRepository _stockItems;
    private readonly IWarehouseRepository _warehouses;
    private readonly IStockMovementRepository _movements;
    private readonly IInventoryUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    /// <summary>Initializes the handler.</summary>
    public FulfilReservationCommandHandler(
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
        FulfilReservationCommand request,
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

        Result<StockMovement> movement = context.Value.StockItem
            .Fulfil(new ReservationId(request.ReservationId), _clock.UtcNow);

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
/// Lapses every reservation past its expiry, across all stock.
/// Meant to be run on a schedule rather than from a screen.
/// </summary>
/// <param name="MaxItems">How many balances to sweep in one pass, so a backlog cannot stall the job.</param>
public sealed record ExpireLapsedReservationsCommand(int MaxItems = 500) : ICommand<int>;

/// <summary>Sweeps expired reservations and returns how many lapsed.</summary>
public sealed class ExpireLapsedReservationsCommandHandler
    : ICommandHandler<ExpireLapsedReservationsCommand, int>
{
    private readonly IStockItemRepository _stockItems;
    private readonly IInventoryUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    /// <summary>Initializes the handler.</summary>
    public ExpireLapsedReservationsCommandHandler(
        IStockItemRepository stockItems,
        IInventoryUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _stockItems = stockItems;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result<int>> HandleAsync(
        ExpireLapsedReservationsCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        DateTimeOffset now = _clock.UtcNow;

        IReadOnlyList<StockItem> items = await _stockItems
            .GetWithExpiredReservationsAsync(now, request.MaxItems, cancellationToken)
            .ConfigureAwait(false);

        int expired = 0;

        foreach (StockItem item in items)
        {
            expired += item.ExpireLapsedReservations(now);
        }

        if (expired > 0)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return expired;
    }
}
