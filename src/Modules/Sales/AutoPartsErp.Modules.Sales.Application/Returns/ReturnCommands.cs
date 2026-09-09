using AutoPartsErp.Modules.Sales.Domain;
using AutoPartsErp.Modules.Sales.Domain.Orders;
using AutoPartsErp.Modules.Sales.Domain.Returns;
using AutoPartsErp.SharedKernel.Abstractions;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Sales.Application.Returns;

/// <summary>
/// Raises a return against an order.
/// <para>
/// Only the order is named. The customer, their code, the currency and the warehouse the goods
/// are coming back to all come off the order — nothing is retyped, and in particular the
/// warehouse is not offered as a choice: stock that left one branch and is booked back into
/// another is a transfer nobody recorded.
/// </para>
/// </summary>
/// <param name="SalesOrderId">The order the goods went out on.</param>
/// <param name="Reason">Why they are coming back. Required.</param>
public sealed record RaiseCustomerReturnCommand(Guid SalesOrderId, string Reason) : ICommand<Guid>;

/// <summary>Checks the shape of a <see cref="RaiseCustomerReturnCommand"/>.</summary>
public sealed class RaiseCustomerReturnCommandValidator : IValidator<RaiseCustomerReturnCommand>
{
    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(
        RaiseCustomerReturnCommand instance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var failures = new List<ValidationFailure>();

        if (instance.SalesOrderId == Guid.Empty)
        {
            failures.Add(new ValidationFailure(
                nameof(instance.SalesOrderId), "required",
                "Say which order the goods went out on."));
        }

        if (string.IsNullOrWhiteSpace(instance.Reason))
        {
            failures.Add(new ValidationFailure(
                nameof(instance.Reason), "required",
                "Say why the goods are coming back."));
        }

        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}

/// <summary>
/// Puts a line of the original order on a draft return.
/// <para>
/// The price is not a parameter. What the customer is given back is what they paid, which is on
/// the order line, and letting a caller supply a figure would make it possible to credit
/// somebody an amount they were never charged — by mistake far more often than on purpose.
/// </para>
/// </summary>
/// <param name="CustomerReturnId">The return.</param>
/// <param name="SalesOrderLineId">The line of the original order.</param>
/// <param name="Quantity">How much is coming back.</param>
/// <param name="Disposition">BackToStock or Scrap.</param>
/// <param name="ConditionNote">What state it arrived in.</param>
public sealed record AddCustomerReturnLineCommand(
    Guid CustomerReturnId,
    Guid SalesOrderLineId,
    decimal Quantity,
    string Disposition,
    string? ConditionNote = null) : ICommand<Guid>;

/// <summary>Checks the shape of an <see cref="AddCustomerReturnLineCommand"/>.</summary>
public sealed class AddCustomerReturnLineCommandValidator
    : IValidator<AddCustomerReturnLineCommand>
{
    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(
        AddCustomerReturnLineCommand instance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var failures = new List<ValidationFailure>();

        if (instance.Quantity <= 0m)
        {
            failures.Add(new ValidationFailure(
                nameof(instance.Quantity), "not_positive", "A returned quantity must be positive."));
        }

        if (!Enum.TryParse(instance.Disposition, ignoreCase: true, out ReturnDisposition disposition)
            || disposition == ReturnDisposition.Unknown)
        {
            failures.Add(new ValidationFailure(
                nameof(instance.Disposition), "unknown",
                "Disposition must be BackToStock or Scrap."));
        }

        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}

/// <summary>Takes a line off a draft return.</summary>
/// <param name="CustomerReturnId">The return.</param>
/// <param name="LineId">The line.</param>
public sealed record RemoveCustomerReturnLineCommand(Guid CustomerReturnId, Guid LineId) : ICommand;

/// <summary>Records that the goods on a return are physically back.</summary>
/// <param name="CustomerReturnId">The return.</param>
public sealed record ReceiveCustomerReturnCommand(Guid CustomerReturnId) : ICommand;

/// <summary>Calls a draft return off.</summary>
/// <param name="CustomerReturnId">The return.</param>
/// <param name="Reason">Why. Required.</param>
public sealed record CancelCustomerReturnCommand(Guid CustomerReturnId, string Reason) : ICommand;

/// <summary>Raises a return, filling everything it can from the order.</summary>
public sealed class RaiseCustomerReturnCommandHandler
    : ICommandHandler<RaiseCustomerReturnCommand, Guid>
{
    private readonly ISalesOrderRepository _orders;
    private readonly ICustomerReturnRepository _returns;
    private readonly ISalesUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    /// <summary>Initializes the handler.</summary>
    public RaiseCustomerReturnCommandHandler(
        ISalesOrderRepository orders,
        ICustomerReturnRepository returns,
        ISalesUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _orders = orders;
        _returns = returns;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result<Guid>> HandleAsync(
        RaiseCustomerReturnCommand request,
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

        string number = await _returns
            .NextReturnNumberAsync(_clock.TodayUtc.Year, cancellationToken)
            .ConfigureAwait(false);

        Result<CustomerReturn> raised = CustomerReturn.Raise(
            number,
            order.Id,
            order.OrderNumber,
            order.CustomerId,
            order.CustomerCode,
            order.CustomerName,
            order.FromWarehouseId,
            order.CurrencyCode,
            request.Reason);

        if (raised.IsFailure)
        {
            return Result.Failure<Guid>(raised.Error);
        }

        _returns.Add(raised.Value);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return raised.Value.Id.Value;
    }
}

/// <summary>
/// The four things that can be done to a return after it exists.
/// <para>
/// One handler because they share the same two loads and the same failure modes, and because
/// splitting them into four classes that each open with the same eight lines is four places to
/// forget the same check.
/// </para>
/// </summary>
public sealed class CustomerReturnCommandHandler
    : ICommandHandler<AddCustomerReturnLineCommand, Guid>,
      ICommandHandler<RemoveCustomerReturnLineCommand>,
      ICommandHandler<ReceiveCustomerReturnCommand>,
      ICommandHandler<CancelCustomerReturnCommand>
{
    private readonly ICustomerReturnRepository _returns;
    private readonly ISalesOrderRepository _orders;
    private readonly ISalesUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    /// <summary>Initializes the handler.</summary>
    public CustomerReturnCommandHandler(
        ICustomerReturnRepository returns,
        ISalesOrderRepository orders,
        ISalesUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _returns = returns;
        _orders = orders;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The quantity is checked against the order here as well as at receipt. It is not the
    /// authoritative check — two drafts can each be built against the same line and both pass —
    /// but finding out at the counter that only two of the four went out is worth far more than
    /// finding out when the goods are already on the desk.
    /// </remarks>
    public async Task<Result<Guid>> HandleAsync(
        AddCustomerReturnLineCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        CustomerReturn? customerReturn = await _returns
            .GetByIdAsync(new CustomerReturnId(request.CustomerReturnId), cancellationToken)
            .ConfigureAwait(false);

        if (customerReturn is null)
        {
            return Result.Failure<Guid>(
                SalesErrors.Return.NotFound(request.CustomerReturnId.ToString()));
        }

        SalesOrder? order = await _orders
            .GetByIdAsync(customerReturn.SalesOrderId, cancellationToken)
            .ConfigureAwait(false);

        if (order is null)
        {
            return Result.Failure<Guid>(
                SalesErrors.Order.NotFound(customerReturn.SalesOrderId.ToString()));
        }

        var lineId = new SalesOrderLineId(request.SalesOrderLineId);

        SalesOrderLine? line = order.Lines.FirstOrDefault(candidate => candidate.Id == lineId);

        if (line is null)
        {
            return Result.Failure<Guid>(SalesErrors.Line.NotFound(lineId.ToString()));
        }

        Result<Quantity> quantity = Quantity.Create(request.Quantity, line.Quantity.Unit);

        if (quantity.IsFailure)
        {
            return Result.Failure<Guid>(quantity.Error);
        }

        if (quantity.Value > line.ReturnableQuantity)
        {
            return Result.Failure<Guid>(SalesErrors.Return.ExceedsDispatched(
                line.ReturnableQuantity.Value, quantity.Value.Value, line.Quantity.Unit.Code));
        }

        var disposition = Enum.Parse<ReturnDisposition>(request.Disposition, ignoreCase: true);

        Result<CustomerReturnLineId> added = customerReturn.AddLine(
            line.Id,
            line.PartId,
            line.Sku,
            line.Description,
            quantity.Value,
            line.UnitPrice,
            line.DiscountPercent,
            line.VatRatePercent,
            disposition,
            request.ConditionNote);

        if (added.IsFailure)
        {
            return Result.Failure<Guid>(added.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return added.Value.Value;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        RemoveCustomerReturnLineCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        CustomerReturn? customerReturn = await _returns
            .GetByIdAsync(new CustomerReturnId(request.CustomerReturnId), cancellationToken)
            .ConfigureAwait(false);

        if (customerReturn is null)
        {
            return SalesErrors.Return.NotFound(request.CustomerReturnId.ToString());
        }

        Result removed = customerReturn.RemoveLine(new CustomerReturnLineId(request.LineId));

        if (removed.IsFailure)
        {
            return removed;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }

    /// <inheritdoc />
    /// <remarks>
    /// The step that moves stock, and the one place the arithmetic is authoritative. Both
    /// aggregates change together: the return says the goods are here, and every line it names
    /// is recorded against the order in the same transaction. Either both happen or neither
    /// does — a return booked in against an order that does not know about it is a line that can
    /// be returned twice.
    /// </remarks>
    public async Task<Result> HandleAsync(
        ReceiveCustomerReturnCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        CustomerReturn? customerReturn = await _returns
            .GetByIdAsync(new CustomerReturnId(request.CustomerReturnId), cancellationToken)
            .ConfigureAwait(false);

        if (customerReturn is null)
        {
            return SalesErrors.Return.NotFound(request.CustomerReturnId.ToString());
        }

        SalesOrder? order = await _orders
            .GetByIdAsync(customerReturn.SalesOrderId, cancellationToken)
            .ConfigureAwait(false);

        if (order is null)
        {
            return SalesErrors.Order.NotFound(customerReturn.SalesOrderId.ToString());
        }

        // Recorded against the order before the return changes status, so a line that cannot be
        // returned stops the whole receipt rather than leaving half of it applied.
        foreach (CustomerReturnLine line in customerReturn.Lines)
        {
            Result recorded = order.RecordReturn(line.SalesOrderLineId, line.Quantity);

            if (recorded.IsFailure)
            {
                return recorded;
            }
        }

        Result received = customerReturn.Receive(_clock.TodayUtc);

        if (received.IsFailure)
        {
            return received;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        CancelCustomerReturnCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        CustomerReturn? customerReturn = await _returns
            .GetByIdAsync(new CustomerReturnId(request.CustomerReturnId), cancellationToken)
            .ConfigureAwait(false);

        if (customerReturn is null)
        {
            return SalesErrors.Return.NotFound(request.CustomerReturnId.ToString());
        }

        Result cancelled = customerReturn.Cancel(request.Reason);

        if (cancelled.IsFailure)
        {
            return cancelled;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
