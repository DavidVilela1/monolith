using AutoPartsErp.Modules.Sales.Domain;
using AutoPartsErp.Modules.Sales.Domain.Customers;
using AutoPartsErp.Modules.Sales.Domain.Orders;
using AutoPartsErp.SharedKernel.Abstractions;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Sales.Application.Orders.Commands;

/// <summary>
/// Starts a sales order.
/// <para>
/// Only the customer is named. Their code, their name and the currency all come off the account
/// Sales already holds — nothing is retyped, and nothing is looked up across a module boundary.
/// That is the payoff for keeping a customer projection: the counter types a code and the rest
/// is known.
/// </para>
/// </summary>
/// <param name="CustomerId">Who it is for.</param>
/// <param name="WarehouseId">Where the goods come from.</param>
/// <param name="Kind">CounterSale or Order.</param>
/// <param name="CustomerReference">Their own order number.</param>
/// <param name="Notes">Anything worth recording against it.</param>
public sealed record CreateSalesOrderCommand(
    Guid CustomerId,
    Guid WarehouseId,
    string Kind = "Order",
    string? CustomerReference = null,
    string? Notes = null) : ICommand<Guid>;

/// <summary>Checks the shape of a <see cref="CreateSalesOrderCommand"/>.</summary>
public sealed class CreateSalesOrderCommandValidator : IValidator<CreateSalesOrderCommand>
{
    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(
        CreateSalesOrderCommand instance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var failures = new List<ValidationFailure>();

        if (instance.CustomerId == Guid.Empty)
        {
            failures.Add(new ValidationFailure(
                nameof(instance.CustomerId), "required", "A customer is required."));
        }

        if (instance.WarehouseId == Guid.Empty)
        {
            failures.Add(new ValidationFailure(
                nameof(instance.WarehouseId), "required",
                "Say which warehouse the goods are coming out of."));
        }

        if (!Enum.TryParse(instance.Kind, ignoreCase: true, out SalesOrderKind kind)
            || kind == SalesOrderKind.Unknown)
        {
            failures.Add(new ValidationFailure(
                nameof(instance.Kind), "unknown", "Kind must be CounterSale or Order."));
        }

        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}

/// <summary>Creates the order.</summary>
public sealed class CreateSalesOrderCommandHandler : ICommandHandler<CreateSalesOrderCommand, Guid>
{
    private readonly ISalesOrderRepository _orders;
    private readonly ICustomerAccountRepository _customers;
    private readonly ISalesUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    /// <summary>Initializes the handler.</summary>
    public CreateSalesOrderCommandHandler(
        ISalesOrderRepository orders,
        ICustomerAccountRepository customers,
        ISalesUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _orders = orders;
        _customers = customers;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result<Guid>> HandleAsync(
        CreateSalesOrderCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var customerId = new CustomerRef(request.CustomerId);

        CustomerAccount? account = await _customers
            .GetByIdAsync(customerId, cancellationToken)
            .ConfigureAwait(false);

        if (account is null)
        {
            return Result.Failure<Guid>(SalesErrors.Customer.NotFound(request.CustomerId.ToString()));
        }

        // Refused at the door rather than at confirmation. Letting someone build a twenty-line
        // order for an account that is on hold, then telling them at the end, is the kind of
        // thing that makes people keep a paper book instead.
        Result canTrade = account.EnsureCanTrade();
        if (canTrade.IsFailure)
        {
            return Result.Failure<Guid>(canTrade.Error);
        }

        string orderNumber = await _orders
            .NextOrderNumberAsync(_clock.TodayUtc.Year, cancellationToken)
            .ConfigureAwait(false);

        Result<SalesOrder> order = SalesOrder.Draft(
            orderNumber,
            Enum.Parse<SalesOrderKind>(request.Kind, ignoreCase: true),
            customerId,
            account.Code,
            account.LegalName,
            new WarehouseRef(request.WarehouseId),
            account.Currency);

        if (order.IsFailure)
        {
            return Result.Failure<Guid>(order.Error);
        }

        Result referenced = order.Value.SetReferences(request.CustomerReference, request.Notes);
        if (referenced.IsFailure)
        {
            return Result.Failure<Guid>(referenced.Error);
        }

        _orders.Add(order.Value);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return order.Value.Id.Value;
    }
}

/// <summary>Adds a part to a draft order.</summary>
/// <param name="SalesOrderId">The order.</param>
/// <param name="PartId">The part to sell.</param>
/// <param name="Sku">Its SKU, snapshotted onto the document.</param>
/// <param name="Description">Its description, snapshotted onto the document.</param>
/// <param name="Quantity">How much to sell.</param>
/// <param name="UnitCode">The unit to sell it in, e.g. EA, SET, L.</param>
/// <param name="UnitPrice">The list price per unit, in the order's currency.</param>
/// <param name="DiscountPercent">The discount given, 0 to 100.</param>
/// <param name="VatRatePercent">The VAT rate, 0 to 100. Portugal's normal rate is 23.</param>
public sealed record AddSalesOrderLineCommand(
    Guid SalesOrderId,
    Guid PartId,
    string Sku,
    string Description,
    decimal Quantity,
    string UnitCode,
    decimal UnitPrice,
    decimal DiscountPercent = 0m,
    decimal VatRatePercent = 23m) : ICommand<Guid>;

/// <summary>Checks the shape of an <see cref="AddSalesOrderLineCommand"/>.</summary>
public sealed class AddSalesOrderLineCommandValidator : IValidator<AddSalesOrderLineCommand>
{
    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(
        AddSalesOrderLineCommand instance,
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
                nameof(instance.Quantity), "not_positive", "A quantity must be above zero."));
        }

        if (instance.UnitPrice < 0m)
        {
            failures.Add(new ValidationFailure(
                nameof(instance.UnitPrice), "negative", "A unit price cannot be negative."));
        }

        if (instance.DiscountPercent is < 0m or > 100m)
        {
            failures.Add(new ValidationFailure(
                nameof(instance.DiscountPercent), "range", "A discount must be between 0 and 100 percent."));
        }

        if (instance.VatRatePercent is < 0m or > 100m)
        {
            failures.Add(new ValidationFailure(
                nameof(instance.VatRatePercent), "range", "A VAT rate must be between 0 and 100 percent."));
        }

        if (!UnitOfMeasure.TryFromCode(instance.UnitCode, out _))
        {
            failures.Add(new ValidationFailure(
                nameof(instance.UnitCode), "unknown_unit",
                $"'{instance.UnitCode}' is not a known unit of measure."));
        }

        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}

/// <summary>Adds the line.</summary>
public sealed class AddSalesOrderLineCommandHandler : ICommandHandler<AddSalesOrderLineCommand, Guid>
{
    private readonly ISalesOrderRepository _orders;
    private readonly ISalesUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public AddSalesOrderLineCommandHandler(ISalesOrderRepository orders, ISalesUnitOfWork unitOfWork)
    {
        _orders = orders;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result<Guid>> HandleAsync(
        AddSalesOrderLineCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        SalesOrder? order = await _orders
            .GetByIdAsync(new SalesOrderId(request.SalesOrderId), cancellationToken)
            .ConfigureAwait(false);

        if (order is null)
        {
            return Result.Failure<Guid>(SalesErrors.Order.NotFound(request.SalesOrderId.ToString()));
        }

        Result<Quantity> quantity = Quantity.Create(
            request.Quantity, UnitOfMeasure.FromCode(request.UnitCode));

        if (quantity.IsFailure)
        {
            return Result.Failure<Guid>(quantity.Error);
        }

        Result<SalesOrderLineId> line = order.AddLine(
            new PartRef(request.PartId),
            request.Sku,
            request.Description,
            quantity.Value,
            Money.Of(request.UnitPrice, order.Currency),
            request.DiscountPercent,
            request.VatRatePercent);

        if (line.IsFailure)
        {
            return Result.Failure<Guid>(line.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return line.Value.Value;
    }
}

/// <summary>Changes how much of a part is being sold.</summary>
/// <param name="SalesOrderId">The order.</param>
/// <param name="LineId">The line to change.</param>
/// <param name="Quantity">The new quantity, in the unit the line was raised in.</param>
public sealed record ChangeSalesOrderLineQuantityCommand(
    Guid SalesOrderId,
    Guid LineId,
    decimal Quantity) : ICommand;

/// <summary>Changes the line quantity.</summary>
public sealed class ChangeSalesOrderLineQuantityCommandHandler
    : ICommandHandler<ChangeSalesOrderLineQuantityCommand>
{
    private readonly ISalesOrderRepository _orders;
    private readonly ISalesUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public ChangeSalesOrderLineQuantityCommandHandler(
        ISalesOrderRepository orders,
        ISalesUnitOfWork unitOfWork)
    {
        _orders = orders;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        ChangeSalesOrderLineQuantityCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        SalesOrder? order = await _orders
            .GetByIdAsync(new SalesOrderId(request.SalesOrderId), cancellationToken)
            .ConfigureAwait(false);

        if (order is null)
        {
            return SalesErrors.Order.NotFound(request.SalesOrderId.ToString());
        }

        var lineId = new SalesOrderLineId(request.LineId);

        // The unit comes off the line, never the request: a bare number is not a quantity.
        SalesOrderLine? line = order.Lines.FirstOrDefault(candidate => candidate.Id == lineId);
        if (line is null)
        {
            return SalesErrors.Line.NotFound(request.LineId.ToString());
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

/// <summary>Changes the price or discount on a line.</summary>
/// <param name="SalesOrderId">The order.</param>
/// <param name="LineId">The line to change.</param>
/// <param name="UnitPrice">The new list price per unit.</param>
/// <param name="DiscountPercent">The new discount, 0 to 100.</param>
public sealed record ChangeSalesOrderLinePricingCommand(
    Guid SalesOrderId,
    Guid LineId,
    decimal UnitPrice,
    decimal DiscountPercent) : ICommand;

/// <summary>Changes the line pricing.</summary>
public sealed class ChangeSalesOrderLinePricingCommandHandler
    : ICommandHandler<ChangeSalesOrderLinePricingCommand>
{
    private readonly ISalesOrderRepository _orders;
    private readonly ISalesUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public ChangeSalesOrderLinePricingCommandHandler(
        ISalesOrderRepository orders,
        ISalesUnitOfWork unitOfWork)
    {
        _orders = orders;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        ChangeSalesOrderLinePricingCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        SalesOrder? order = await _orders
            .GetByIdAsync(new SalesOrderId(request.SalesOrderId), cancellationToken)
            .ConfigureAwait(false);

        if (order is null)
        {
            return SalesErrors.Order.NotFound(request.SalesOrderId.ToString());
        }

        Result changed = order.ChangeLinePricing(
            new SalesOrderLineId(request.LineId),
            Money.Of(request.UnitPrice, order.Currency),
            request.DiscountPercent);

        if (changed.IsFailure)
        {
            return changed;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>Takes a part off a draft order.</summary>
/// <param name="SalesOrderId">The order.</param>
/// <param name="LineId">The line to remove.</param>
public sealed record RemoveSalesOrderLineCommand(Guid SalesOrderId, Guid LineId) : ICommand;

/// <summary>Removes the line.</summary>
public sealed class RemoveSalesOrderLineCommandHandler : ICommandHandler<RemoveSalesOrderLineCommand>
{
    private readonly ISalesOrderRepository _orders;
    private readonly ISalesUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public RemoveSalesOrderLineCommandHandler(ISalesOrderRepository orders, ISalesUnitOfWork unitOfWork)
    {
        _orders = orders;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        RemoveSalesOrderLineCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        SalesOrder? order = await _orders
            .GetByIdAsync(new SalesOrderId(request.SalesOrderId), cancellationToken)
            .ConfigureAwait(false);

        if (order is null)
        {
            return SalesErrors.Order.NotFound(request.SalesOrderId.ToString());
        }

        Result removed = order.RemoveLine(new SalesOrderLineId(request.LineId));
        if (removed.IsFailure)
        {
            return removed;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>Agrees the order with the customer and claims the stock.</summary>
/// <param name="SalesOrderId">The order.</param>
/// <param name="RequiredBy">When the customer wants it.</param>
public sealed record ConfirmSalesOrderCommand(
    Guid SalesOrderId,
    DateOnly? RequiredBy = null) : ICommand;

/// <summary>
/// Confirms the order, after the customer's account has agreed to carry it.
/// <para>
/// Two aggregates change here — the order and the account — which is a rule usually worth
/// keeping. It is broken deliberately: the credit committed and the order that committed it have
/// to move together or the exposure figure is a lie, and they live in the same module and the
/// same transaction. Splitting them across an event would buy purity and pay for it with a
/// number nobody can trust.
/// </para>
/// </summary>
public sealed class ConfirmSalesOrderCommandHandler : ICommandHandler<ConfirmSalesOrderCommand>
{
    private readonly ISalesOrderRepository _orders;
    private readonly ICustomerAccountRepository _customers;
    private readonly ISalesUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    /// <summary>Initializes the handler.</summary>
    public ConfirmSalesOrderCommandHandler(
        ISalesOrderRepository orders,
        ICustomerAccountRepository customers,
        ISalesUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _orders = orders;
        _customers = customers;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        ConfirmSalesOrderCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        SalesOrder? order = await _orders
            .GetByIdAsync(new SalesOrderId(request.SalesOrderId), cancellationToken)
            .ConfigureAwait(false);

        if (order is null)
        {
            return SalesErrors.Order.NotFound(request.SalesOrderId.ToString());
        }

        CustomerAccount? account = await _customers
            .GetByIdAsync(order.CustomerId, cancellationToken)
            .ConfigureAwait(false);

        if (account is null)
        {
            return SalesErrors.Customer.NotFound(order.CustomerId.ToString());
        }

        if (order.ConsumesCredit)
        {
            // Commit checks the hold and the limit together and reports whichever failed.
            Result committed = account.Commit(order.GrossTotal);
            if (committed.IsFailure)
            {
                return committed;
            }
        }
        else
        {
            // A counter sale is paid before the goods leave, so no credit is at risk - but a
            // held account still may not buy.
            Result canTrade = account.EnsureCanTrade();
            if (canTrade.IsFailure)
            {
                return canTrade;
            }
        }

        Result confirmed = order.Confirm(_clock.TodayUtc, request.RequiredBy);
        if (confirmed.IsFailure)
        {
            return confirmed;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>Records goods leaving against one line.</summary>
/// <param name="SalesOrderId">The order.</param>
/// <param name="LineId">The line the goods went out against.</param>
/// <param name="Quantity">How much went, in the unit the line was sold in.</param>
public sealed record DispatchSalesOrderLineCommand(
    Guid SalesOrderId,
    Guid LineId,
    decimal Quantity) : ICommand;

/// <summary>
/// Records the dispatch, and gives the credit back once the order is complete.
/// <para>
/// The commitment is released on the last line rather than line by line, because a half-shipped
/// order is still an order the customer owes money for. Releasing early would let the next order
/// through on credit that has not actually been freed.
/// </para>
/// </summary>
public sealed class DispatchSalesOrderLineCommandHandler
    : ICommandHandler<DispatchSalesOrderLineCommand>
{
    private readonly ISalesOrderRepository _orders;
    private readonly ICustomerAccountRepository _customers;
    private readonly ISalesUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public DispatchSalesOrderLineCommandHandler(
        ISalesOrderRepository orders,
        ICustomerAccountRepository customers,
        ISalesUnitOfWork unitOfWork)
    {
        _orders = orders;
        _customers = customers;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        DispatchSalesOrderLineCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        SalesOrder? order = await _orders
            .GetByIdAsync(new SalesOrderId(request.SalesOrderId), cancellationToken)
            .ConfigureAwait(false);

        if (order is null)
        {
            return SalesErrors.Order.NotFound(request.SalesOrderId.ToString());
        }

        var lineId = new SalesOrderLineId(request.LineId);

        SalesOrderLine? line = order.Lines.FirstOrDefault(candidate => candidate.Id == lineId);
        if (line is null)
        {
            return SalesErrors.Line.NotFound(request.LineId.ToString());
        }

        Result<Quantity> dispatched = Quantity.Create(request.Quantity, line.Quantity.Unit);
        if (dispatched.IsFailure)
        {
            return Result.FromError(dispatched.Error);
        }

        Money gross = order.GrossTotal;

        Result result = order.DispatchLine(lineId, dispatched.Value);
        if (result.IsFailure)
        {
            return result;
        }

        if (order.Status == SalesOrderStatus.Dispatched && order.ConsumesCredit)
        {
            CustomerAccount? account = await _customers
                .GetByIdAsync(order.CustomerId, cancellationToken)
                .ConfigureAwait(false);

            // A missing account here would mean the projection lost a record between confirming
            // and shipping. Not worth failing the dispatch over - the goods have gone - but the
            // exposure figure will be high until somebody notices.
            if (account is not null)
            {
                Result released = account.ReleaseCommitment(gross);
                if (released.IsFailure)
                {
                    return released;
                }
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>Calls off an order before anything has gone out.</summary>
/// <param name="SalesOrderId">The order.</param>
/// <param name="Reason">Why.</param>
public sealed record CancelSalesOrderCommand(Guid SalesOrderId, string Reason) : ICommand;

/// <summary>Cancels the order and gives back any credit it was holding.</summary>
public sealed class CancelSalesOrderCommandHandler : ICommandHandler<CancelSalesOrderCommand>
{
    private readonly ISalesOrderRepository _orders;
    private readonly ICustomerAccountRepository _customers;
    private readonly ISalesUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public CancelSalesOrderCommandHandler(
        ISalesOrderRepository orders,
        ICustomerAccountRepository customers,
        ISalesUnitOfWork unitOfWork)
    {
        _orders = orders;
        _customers = customers;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        CancelSalesOrderCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        SalesOrder? order = await _orders
            .GetByIdAsync(new SalesOrderId(request.SalesOrderId), cancellationToken)
            .ConfigureAwait(false);

        if (order is null)
        {
            return SalesErrors.Order.NotFound(request.SalesOrderId.ToString());
        }

        bool wasHoldingCredit = order.Status == SalesOrderStatus.Confirmed && order.ConsumesCredit;
        Money gross = order.GrossTotal;

        Result cancelled = order.Cancel(request.Reason);
        if (cancelled.IsFailure)
        {
            return cancelled;
        }

        if (wasHoldingCredit)
        {
            CustomerAccount? account = await _customers
                .GetByIdAsync(order.CustomerId, cancellationToken)
                .ConfigureAwait(false);

            if (account is not null)
            {
                Result released = account.ReleaseCommitment(gross);
                if (released.IsFailure)
                {
                    return released;
                }
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
