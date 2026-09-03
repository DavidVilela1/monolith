using AutoPartsErp.ModuleContracts.Catalog;
using AutoPartsErp.ModuleContracts.Inventory;
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
/// <param name="Quantity">How much to sell, in the part's stocking unit.</param>
/// <param name="UnitPrice">The list price per unit, in the order's currency.</param>
/// <param name="DiscountPercent">The discount given, 0 to 100.</param>
/// <param name="VatRatePercent">The VAT rate, 0 to 100. Portugal's normal rate is 23.</param>
/// <remarks>
/// This command used to carry the SKU, the description and the unit as well. It no longer does:
/// the caller says which part it means and the catalogue is asked what that part is called and
/// how it is counted. Three fewer chances to disagree with the catalogue on every line, on a
/// document that is kept for years.
/// </remarks>
public sealed record AddSalesOrderLineCommand(
    Guid SalesOrderId,
    Guid PartId,
    decimal Quantity,
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

        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}

/// <summary>Adds the line.</summary>
public sealed class AddSalesOrderLineCommandHandler : ICommandHandler<AddSalesOrderLineCommand, Guid>
{
    private readonly ISalesOrderRepository _orders;
    private readonly ICatalogDirectory _catalogue;
    private readonly ISalesUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public AddSalesOrderLineCommandHandler(
        ISalesOrderRepository orders,
        ICatalogDirectory catalogue,
        ISalesUnitOfWork unitOfWork)
    {
        _orders = orders;
        _catalogue = catalogue;
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

        // The order's own state guard first. Asking the catalogue about a part nobody is allowed
        // to add is a wasted round trip, and reporting "that part is obsolete" when the real
        // answer is "that order was confirmed on Tuesday" sends somebody looking in the wrong
        // place.
        if (!order.IsEditable)
        {
            return Result.Failure<Guid>(SalesErrors.Order.NotEditable);
        }

        PartDescriptor? part = await _catalogue
            .GetAsync(request.PartId, cancellationToken)
            .ConfigureAwait(false);

        if (part is null)
        {
            return Result.Failure<Guid>(
                SalesErrors.Line.PartNotInCatalogue(request.PartId.ToString()));
        }

        if (!part.IsSellable)
        {
            return Result.Failure<Guid>(
                SalesErrors.Line.PartNotSellable(part.Sku, part.SupersededByPartId));
        }

        // The stocking unit, not a unit the caller chose. Every quantity ever recorded against
        // this part is in this unit, and a line raised in another one is a reservation that
        // cannot be filled and an invoice that cannot be reconciled.
        Result<Quantity> quantity = Quantity.Create(
            request.Quantity, UnitOfMeasure.FromCode(part.StockUnitCode));

        if (quantity.IsFailure)
        {
            return Result.Failure<Guid>(quantity.Error);
        }

        Result<SalesOrderLineId> line = order.AddLine(
            new PartRef(part.PartId),
            part.Sku,
            part.Name,
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
/// <param name="AllowBackorder">
/// True to confirm even where there is not enough on the shelf.
/// <para>
/// Off by default, and a deliberate act when it is on. A distributor does take back-orders —
/// but the person promising one should know they are doing it, rather than finding out when
/// the customer rings about goods that were never going to be there.
/// </para>
/// </param>
public sealed record ConfirmSalesOrderCommand(
    Guid SalesOrderId,
    DateOnly? RequiredBy = null,
    bool AllowBackorder = false) : ICommand;

/// <summary>
/// Confirms the order, after the customer's account has agreed to carry it and Inventory has
/// confirmed the goods exist.
/// <para>
/// Two aggregates change here — the order and the account — which is a rule usually worth
/// keeping. It is broken deliberately: the credit committed and the order that committed it have
/// to move together or the exposure figure is a lie, and they live in the same module and the
/// same transaction. Splitting them across an event would buy purity and pay for it with a
/// number nobody can trust.
/// </para>
/// <para>
/// The stock check is the one synchronous call Sales makes into another module, and it is here
/// rather than anywhere else because this is the moment a promise is made. It asks about every
/// line in one round trip.
/// </para>
/// </summary>
public sealed class ConfirmSalesOrderCommandHandler : ICommandHandler<ConfirmSalesOrderCommand>
{
    private readonly ISalesOrderRepository _orders;
    private readonly ICustomerAccountRepository _customers;
    private readonly IInventoryAvailability _availability;
    private readonly ISalesUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    /// <summary>Initializes the handler.</summary>
    public ConfirmSalesOrderCommandHandler(
        ISalesOrderRepository orders,
        ICustomerAccountRepository customers,
        IInventoryAvailability availability,
        ISalesUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _orders = orders;
        _customers = customers;
        _availability = availability;
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

        // The order's own state guard runs first. Without this, re-confirming an order would
        // spend a cross-module round trip and then report "not enough stock" - because the
        // order's own reservations are what consumed it - instead of "already confirmed".
        if (order.Status != SalesOrderStatus.Draft)
        {
            return order.Confirm(_clock.TodayUtc, request.RequiredBy, request.AllowBackorder);
        }

        // A back-order is a deliberate promise of goods that are not there, and it only makes
        // sense for something being delivered later. A counter sale is goods leaving now, so it
        // is checked whatever the caller asked for.
        if (!request.AllowBackorder || !order.ConsumesCredit)
        {
            Result stocked = await EnsureStockAvailableAsync(order, cancellationToken)
                .ConfigureAwait(false);

            if (stocked.IsFailure)
            {
                return stocked;
            }
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

        Result confirmed = order.Confirm(
            _clock.TodayUtc, request.RequiredBy, request.AllowBackorder);

        if (confirmed.IsFailure)
        {
            return confirmed;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }

    /// <summary>
    /// Asks Inventory whether every line can actually be promised.
    /// <para>
    /// One call for the whole order rather than one per line: a ten-line order asking ten times
    /// is the kind of thing that looks fine on a laptop and makes a counter unusable.
    /// </para>
    /// <para>
    /// Reports the first line that fails rather than all of them. The person confirming has to
    /// deal with one problem at a time anyway, and the first shortfall is usually the whole
    /// story.
    /// </para>
    /// <para>
    /// This reads; it does not hold. Two confirmations for the last of something can both pass
    /// here and one will lose when Inventory actually reserves — that race is inherent to a
    /// read-only contract, and closing it would mean Sales taking a lock inside another module's
    /// data. The window is milliseconds and the loser gets a dead-lettered reservation rather
    /// than a wrong balance, which is the right way round.
    /// </para>
    /// </summary>
    private async Task<Result> EnsureStockAvailableAsync(
        SalesOrder order,
        CancellationToken cancellationToken)
    {
        Guid[] partIds = [.. order.Lines.Select(line => line.PartId.Value)];

        if (partIds.Length == 0)
        {
            return Result.Success();
        }

        IReadOnlyDictionary<Guid, StockAvailability> availability = await _availability
            .GetManyAsync(partIds, order.FromWarehouseId.Value, cancellationToken)
            .ConfigureAwait(false);

        foreach (SalesOrderLine line in order.Lines)
        {
            if (!availability.TryGetValue(line.PartId.Value, out StockAvailability? stock))
            {
                return SalesErrors.Line.NoStockRecord(line.Sku);
            }

            // Compared before the magnitudes, because "5" of something stocked in litres and
            // sold in boxes is not a shortfall, it is a different question. Inventory refuses
            // this outright when the reservation arrives; catching it here is the whole point
            // of asking first.
            if (!string.Equals(stock.UnitCode, line.Quantity.Unit.Code, StringComparison.OrdinalIgnoreCase))
            {
                return SalesErrors.Line.UnitDiffersFromStock(
                    line.Sku, line.Quantity.Unit.Code, stock.UnitCode);
            }

            if (stock.Available < line.Quantity.Value)
            {
                return SalesErrors.Line.InsufficientStock(
                    line.Sku, line.Quantity.Value, stock.Available, stock.UnitCode);
            }
        }

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
