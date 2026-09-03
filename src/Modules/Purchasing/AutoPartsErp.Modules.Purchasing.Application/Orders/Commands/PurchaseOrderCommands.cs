using AutoPartsErp.ModuleContracts.Catalog;
using AutoPartsErp.ModuleContracts.Partners;
using AutoPartsErp.Modules.Purchasing.Domain;
using AutoPartsErp.Modules.Purchasing.Domain.Orders;
using AutoPartsErp.SharedKernel.Abstractions;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Purchasing.Application.Orders.Commands;

/// <summary>
/// Starts a draft purchase order.
/// <para>
/// Only the supplier is named. Their code comes from Partners, through the directory contract,
/// and so does the answer to the question this command used to take on trust: is this partner
/// actually a supplier we are allowed to buy from right now?
/// </para>
/// </summary>
/// <param name="SupplierId">Who we are buying from.</param>
/// <param name="WarehouseId">Where the goods are to be delivered.</param>
/// <param name="CurrencyCode">The currency the order is priced in.</param>
/// <param name="Notes">Anything the buyer wants recorded against it.</param>
public sealed record CreatePurchaseOrderCommand(
    Guid SupplierId,
    Guid WarehouseId,
    string CurrencyCode,
    string? Notes = null) : ICommand<Guid>;

/// <summary>Checks the shape of a <see cref="CreatePurchaseOrderCommand"/>.</summary>
public sealed class CreatePurchaseOrderCommandValidator : IValidator<CreatePurchaseOrderCommand>
{
    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(
        CreatePurchaseOrderCommand instance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var failures = new List<ValidationFailure>();

        if (instance.SupplierId == Guid.Empty)
        {
            failures.Add(new ValidationFailure(
                nameof(instance.SupplierId), "required", "A supplier is required."));
        }

        if (instance.WarehouseId == Guid.Empty)
        {
            failures.Add(new ValidationFailure(
                nameof(instance.WarehouseId), "required",
                "Say which warehouse the goods are being delivered to."));
        }

        if (!Currency.TryFromCode(instance.CurrencyCode, out _))
        {
            failures.Add(new ValidationFailure(
                nameof(instance.CurrencyCode), "unknown_currency",
                $"'{instance.CurrencyCode}' is not a supported currency."));
        }

        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}

/// <summary>Creates the draft order.</summary>
public sealed class CreatePurchaseOrderCommandHandler : ICommandHandler<CreatePurchaseOrderCommand, Guid>
{
    private readonly IPurchaseOrderRepository _orders;
    private readonly IPartnerDirectory _partners;
    private readonly IPurchasingUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    /// <summary>Initializes the handler.</summary>
    public CreatePurchaseOrderCommandHandler(
        IPurchaseOrderRepository orders,
        IPartnerDirectory partners,
        IPurchasingUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _orders = orders;
        _partners = partners;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result<Guid>> HandleAsync(
        CreatePurchaseOrderCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        PartnerTradingStatus? supplier = await _partners
            .GetAsync(request.SupplierId, cancellationToken)
            .ConfigureAwait(false);

        if (supplier is null)
        {
            return Result.Failure<Guid>(
                PurchasingErrors.Order.SupplierNotFound(request.SupplierId.ToString()));
        }

        // The rule itself lives on the Partner aggregate - "a supplier, and not on hold". This
        // asks the question; it does not answer it a second time.
        if (!supplier.CanPlacePurchaseOrders)
        {
            return Result.Failure<Guid>(PurchasingErrors.Order.SupplierNotPurchasable);
        }

        string orderNumber = await _orders
            .NextOrderNumberAsync(_clock.TodayUtc.Year, cancellationToken)
            .ConfigureAwait(false);

        Result<PurchaseOrder> order = PurchaseOrder.Draft(
            orderNumber,
            new SupplierRef(request.SupplierId),
            supplier.Code,
            new WarehouseRef(request.WarehouseId),
            Currency.FromCode(request.CurrencyCode));

        if (order.IsFailure)
        {
            return Result.Failure<Guid>(order.Error);
        }

        if (!string.IsNullOrWhiteSpace(request.Notes))
        {
            Result noted = order.Value.SetNotes(request.Notes);
            if (noted.IsFailure)
            {
                return Result.Failure<Guid>(noted.Error);
            }
        }

        _orders.Add(order.Value);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return order.Value.Id.Value;
    }
}

/// <summary>Adds a part to a draft order.</summary>
/// <param name="PurchaseOrderId">The order.</param>
/// <param name="PartId">The part to buy.</param>
/// <param name="Quantity">How much to order, in the part's stocking unit.</param>
/// <param name="UnitPrice">The agreed price per unit, in the order's currency.</param>
/// <remarks>
/// This command used to carry the SKU, the description and the unit as well. It no longer does:
/// the caller says which part it means and the catalogue is asked what that part is called and
/// how it is counted. An old client still sending those three gets them silently ignored, which
/// is worth knowing if you have anything scripted against that endpoint.
/// </remarks>
public sealed record AddPurchaseOrderLineCommand(
    Guid PurchaseOrderId,
    Guid PartId,
    decimal Quantity,
    decimal UnitPrice) : ICommand<Guid>;

/// <summary>Checks the shape of an <see cref="AddPurchaseOrderLineCommand"/>.</summary>
public sealed class AddPurchaseOrderLineCommandValidator : IValidator<AddPurchaseOrderLineCommand>
{
    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(
        AddPurchaseOrderLineCommand instance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var failures = new List<ValidationFailure>();

        if (instance.PartId == Guid.Empty)
        {
            failures.Add(new ValidationFailure(nameof(instance.PartId), "required", "A part is required."));
        }

        if (instance.Quantity <= 0m)
        {
            failures.Add(new ValidationFailure(
                nameof(instance.Quantity), "not_positive", "An order quantity must be above zero."));
        }

        if (instance.UnitPrice < 0m)
        {
            failures.Add(new ValidationFailure(
                nameof(instance.UnitPrice), "negative", "A unit price cannot be negative."));
        }

        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}

/// <summary>Adds the line.</summary>
public sealed class AddPurchaseOrderLineCommandHandler : ICommandHandler<AddPurchaseOrderLineCommand, Guid>
{
    private readonly IPurchaseOrderRepository _orders;
    private readonly ICatalogDirectory _catalogue;
    private readonly IPurchasingUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public AddPurchaseOrderLineCommandHandler(
        IPurchaseOrderRepository orders,
        ICatalogDirectory catalogue,
        IPurchasingUnitOfWork unitOfWork)
    {
        _orders = orders;
        _catalogue = catalogue;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result<Guid>> HandleAsync(
        AddPurchaseOrderLineCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        PurchaseOrder? order = await _orders
            .GetByIdAsync(new PurchaseOrderId(request.PurchaseOrderId), cancellationToken)
            .ConfigureAwait(false);

        if (order is null)
        {
            return Result.Failure<Guid>(
                PurchasingErrors.Order.NotFound(request.PurchaseOrderId.ToString()));
        }

        // The order's own state guard first, so a line added to a sent order reports that rather
        // than whatever the catalogue happens to think of the part.
        if (!order.IsEditable)
        {
            return Result.Failure<Guid>(PurchasingErrors.Order.NotEditable);
        }

        PartDescriptor? part = await _catalogue
            .GetAsync(request.PartId, cancellationToken)
            .ConfigureAwait(false);

        if (part is null)
        {
            return Result.Failure<Guid>(
                PurchasingErrors.Line.PartNotInCatalogue(request.PartId.ToString()));
        }

        if (!part.IsPurchasable)
        {
            return Result.Failure<Guid>(
                PurchasingErrors.Line.PartNotPurchasable(part.Sku, part.SupersededByPartId));
        }

        // The stocking unit, not a unit the caller chose. Goods received against this line become
        // stock, and stock is counted in one unit per part.
        Result<Quantity> quantity = Quantity.Create(
            request.Quantity, UnitOfMeasure.FromCode(part.StockUnitCode));

        if (quantity.IsFailure)
        {
            return Result.Failure<Guid>(quantity.Error);
        }

        Result<PurchaseOrderLineId> line = order.AddLine(
            new PartRef(part.PartId),
            part.Sku,
            part.Name,
            quantity.Value,
            Money.Of(request.UnitPrice, order.Currency));

        if (line.IsFailure)
        {
            return Result.Failure<Guid>(line.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return line.Value.Value;
    }
}

/// <summary>Changes how much of a part is being ordered.</summary>
/// <param name="PurchaseOrderId">The order.</param>
/// <param name="LineId">The line to change.</param>
/// <param name="Quantity">The new quantity, in the unit the line was raised in.</param>
public sealed record ChangePurchaseOrderLineQuantityCommand(
    Guid PurchaseOrderId,
    Guid LineId,
    decimal Quantity) : ICommand;

/// <summary>Changes the line quantity.</summary>
public sealed class ChangePurchaseOrderLineQuantityCommandHandler
    : ICommandHandler<ChangePurchaseOrderLineQuantityCommand>
{
    private readonly IPurchaseOrderRepository _orders;
    private readonly IPurchasingUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public ChangePurchaseOrderLineQuantityCommandHandler(
        IPurchaseOrderRepository orders,
        IPurchasingUnitOfWork unitOfWork)
    {
        _orders = orders;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        ChangePurchaseOrderLineQuantityCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        PurchaseOrder? order = await _orders
            .GetByIdAsync(new PurchaseOrderId(request.PurchaseOrderId), cancellationToken)
            .ConfigureAwait(false);

        if (order is null)
        {
            return PurchasingErrors.Order.NotFound(request.PurchaseOrderId.ToString());
        }

        var lineId = new PurchaseOrderLineId(request.LineId);

        // The unit comes off the line rather than the request: a quantity is only meaningful
        // alongside the unit it was ordered in, and letting the caller restate it would let
        // "50" litres quietly become "50" drums.
        PurchaseOrderLine? line = order.Lines.FirstOrDefault(candidate => candidate.Id == lineId);
        if (line is null)
        {
            return PurchasingErrors.Line.NotFound(request.LineId.ToString());
        }

        Result<Quantity> quantity = Quantity.Create(request.Quantity, line.Quantity.Unit);
        if (quantity.IsFailure)
        {
            return Result.FromError(quantity.Error);
        }

        Result changed = order.ChangeLineQuantity(lineId, quantity.Value);
        if (changed.IsFailure)
        {
            return changed;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>Takes a part off a draft order.</summary>
/// <param name="PurchaseOrderId">The order.</param>
/// <param name="LineId">The line to remove.</param>
public sealed record RemovePurchaseOrderLineCommand(Guid PurchaseOrderId, Guid LineId) : ICommand;

/// <summary>Removes the line.</summary>
public sealed class RemovePurchaseOrderLineCommandHandler
    : ICommandHandler<RemovePurchaseOrderLineCommand>
{
    private readonly IPurchaseOrderRepository _orders;
    private readonly IPurchasingUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public RemovePurchaseOrderLineCommandHandler(
        IPurchaseOrderRepository orders,
        IPurchasingUnitOfWork unitOfWork)
    {
        _orders = orders;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        RemovePurchaseOrderLineCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        PurchaseOrder? order = await _orders
            .GetByIdAsync(new PurchaseOrderId(request.PurchaseOrderId), cancellationToken)
            .ConfigureAwait(false);

        if (order is null)
        {
            return PurchasingErrors.Order.NotFound(request.PurchaseOrderId.ToString());
        }

        Result removed = order.RemoveLine(new PurchaseOrderLineId(request.LineId));
        if (removed.IsFailure)
        {
            return removed;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>Sends the order to the supplier.</summary>
/// <param name="PurchaseOrderId">The order.</param>
/// <param name="ExpectedOn">When delivery is expected, if a date is already known.</param>
public sealed record SubmitPurchaseOrderCommand(
    Guid PurchaseOrderId,
    DateOnly? ExpectedOn = null) : ICommand;

/// <summary>Submits the order.</summary>
public sealed class SubmitPurchaseOrderCommandHandler : ICommandHandler<SubmitPurchaseOrderCommand>
{
    private readonly IPurchaseOrderRepository _orders;
    private readonly IPurchasingUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    /// <summary>Initializes the handler.</summary>
    public SubmitPurchaseOrderCommandHandler(
        IPurchaseOrderRepository orders,
        IPurchasingUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _orders = orders;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        SubmitPurchaseOrderCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        PurchaseOrder? order = await _orders
            .GetByIdAsync(new PurchaseOrderId(request.PurchaseOrderId), cancellationToken)
            .ConfigureAwait(false);

        if (order is null)
        {
            return PurchasingErrors.Order.NotFound(request.PurchaseOrderId.ToString());
        }

        Result submitted = order.Submit(_clock.TodayUtc, request.ExpectedOn);
        if (submitted.IsFailure)
        {
            return submitted;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>Records the supplier's acknowledgement.</summary>
/// <param name="PurchaseOrderId">The order.</param>
/// <param name="ExpectedOn">The date they committed to.</param>
/// <param name="SupplierReference">Their own order number.</param>
public sealed record ConfirmPurchaseOrderCommand(
    Guid PurchaseOrderId,
    DateOnly ExpectedOn,
    string? SupplierReference = null) : ICommand;

/// <summary>Confirms the order.</summary>
public sealed class ConfirmPurchaseOrderCommandHandler : ICommandHandler<ConfirmPurchaseOrderCommand>
{
    private readonly IPurchaseOrderRepository _orders;
    private readonly IPurchasingUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    /// <summary>Initializes the handler.</summary>
    public ConfirmPurchaseOrderCommandHandler(
        IPurchaseOrderRepository orders,
        IPurchasingUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _orders = orders;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        ConfirmPurchaseOrderCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        PurchaseOrder? order = await _orders
            .GetByIdAsync(new PurchaseOrderId(request.PurchaseOrderId), cancellationToken)
            .ConfigureAwait(false);

        if (order is null)
        {
            return PurchasingErrors.Order.NotFound(request.PurchaseOrderId.ToString());
        }

        Result confirmed = order.Confirm(
            request.ExpectedOn, _clock.TodayUtc, request.SupplierReference);

        if (confirmed.IsFailure)
        {
            return confirmed;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>Books a delivery in against one line.</summary>
/// <param name="PurchaseOrderId">The order.</param>
/// <param name="LineId">The line the goods arrived against.</param>
/// <param name="Quantity">How much arrived, in the unit the line was ordered in.</param>
public sealed record ReceivePurchaseOrderLineCommand(
    Guid PurchaseOrderId,
    Guid LineId,
    decimal Quantity) : ICommand;

/// <summary>
/// Records the receipt.
/// <para>
/// Saving here is what puts stock on the shelf, indirectly: the aggregate raises
/// <c>GoodsReceivedDomainEvent</c>, the unit of work dispatches it after the commit, and the
/// translation handler republishes it as an integration event for Inventory. Nothing in this
/// handler mentions stock.
/// </para>
/// </summary>
public sealed class ReceivePurchaseOrderLineCommandHandler
    : ICommandHandler<ReceivePurchaseOrderLineCommand>
{
    private readonly IPurchaseOrderRepository _orders;
    private readonly IPurchasingUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public ReceivePurchaseOrderLineCommandHandler(
        IPurchaseOrderRepository orders,
        IPurchasingUnitOfWork unitOfWork)
    {
        _orders = orders;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        ReceivePurchaseOrderLineCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        PurchaseOrder? order = await _orders
            .GetByIdAsync(new PurchaseOrderId(request.PurchaseOrderId), cancellationToken)
            .ConfigureAwait(false);

        if (order is null)
        {
            return PurchasingErrors.Order.NotFound(request.PurchaseOrderId.ToString());
        }

        var lineId = new PurchaseOrderLineId(request.LineId);

        PurchaseOrderLine? line = order.Lines.FirstOrDefault(candidate => candidate.Id == lineId);
        if (line is null)
        {
            return PurchasingErrors.Line.NotFound(request.LineId.ToString());
        }

        Result<Quantity> received = Quantity.Create(request.Quantity, line.Quantity.Unit);
        if (received.IsFailure)
        {
            return Result.FromError(received.Error);
        }

        Result booked = order.ReceiveLine(lineId, received.Value);
        if (booked.IsFailure)
        {
            return booked;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>Calls off an order before anything has arrived.</summary>
/// <param name="PurchaseOrderId">The order.</param>
/// <param name="Reason">Why. The supplier will ask.</param>
public sealed record CancelPurchaseOrderCommand(Guid PurchaseOrderId, string Reason) : ICommand;

/// <summary>Cancels the order.</summary>
public sealed class CancelPurchaseOrderCommandHandler : ICommandHandler<CancelPurchaseOrderCommand>
{
    private readonly IPurchaseOrderRepository _orders;
    private readonly IPurchasingUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public CancelPurchaseOrderCommandHandler(
        IPurchaseOrderRepository orders,
        IPurchasingUnitOfWork unitOfWork)
    {
        _orders = orders;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        CancelPurchaseOrderCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        PurchaseOrder? order = await _orders
            .GetByIdAsync(new PurchaseOrderId(request.PurchaseOrderId), cancellationToken)
            .ConfigureAwait(false);

        if (order is null)
        {
            return PurchasingErrors.Order.NotFound(request.PurchaseOrderId.ToString());
        }

        Result cancelled = order.Cancel(request.Reason);
        if (cancelled.IsFailure)
        {
            return cancelled;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>Accepts a short delivery and stops chasing the balance.</summary>
/// <param name="PurchaseOrderId">The order.</param>
/// <param name="Reason">Why the shortfall was accepted.</param>
public sealed record ClosePurchaseOrderShortCommand(Guid PurchaseOrderId, string Reason) : ICommand;

/// <summary>Closes the order short.</summary>
public sealed class ClosePurchaseOrderShortCommandHandler
    : ICommandHandler<ClosePurchaseOrderShortCommand>
{
    private readonly IPurchaseOrderRepository _orders;
    private readonly IPurchasingUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public ClosePurchaseOrderShortCommandHandler(
        IPurchaseOrderRepository orders,
        IPurchasingUnitOfWork unitOfWork)
    {
        _orders = orders;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        ClosePurchaseOrderShortCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        PurchaseOrder? order = await _orders
            .GetByIdAsync(new PurchaseOrderId(request.PurchaseOrderId), cancellationToken)
            .ConfigureAwait(false);

        if (order is null)
        {
            return PurchasingErrors.Order.NotFound(request.PurchaseOrderId.ToString());
        }

        Result closed = order.CloseShort(request.Reason);
        if (closed.IsFailure)
        {
            return closed;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
