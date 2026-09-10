using AutoPartsErp.Modules.Purchasing.Domain;
using AutoPartsErp.Modules.Purchasing.Domain.Invoices;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Purchasing.Application.Invoices.Commands;

/// <summary>
/// Records what the supplier's paper says, and finds out whether it agrees.
/// <para>
/// The only three things a person enters about a delivery: their number, their date and their
/// total. Everything else was already known before the van arrived.
/// </para>
/// </summary>
/// <param name="SupplierInvoiceId">The draft their document belongs to.</param>
/// <param name="DocumentNumber">Their document number, as printed.</param>
/// <param name="DocumentDate">The date on their document.</param>
/// <param name="StatedGrossTotal">What their document says the whole thing comes to.</param>
public sealed record ReconcileSupplierInvoiceCommand(
    Guid SupplierInvoiceId,
    string DocumentNumber,
    DateOnly DocumentDate,
    decimal StatedGrossTotal) : ICommand;

/// <summary>Checks the shape of a <see cref="ReconcileSupplierInvoiceCommand"/>.</summary>
public sealed class ReconcileSupplierInvoiceCommandValidator
    : IValidator<ReconcileSupplierInvoiceCommand>
{
    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(
        ReconcileSupplierInvoiceCommand instance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var failures = new List<ValidationFailure>();

        if (string.IsNullOrWhiteSpace(instance.DocumentNumber))
        {
            failures.Add(new ValidationFailure(
                nameof(instance.DocumentNumber), "required",
                "The supplier's own document number is required."));
        }

        if (instance.StatedGrossTotal < 0m)
        {
            failures.Add(new ValidationFailure(
                nameof(instance.StatedGrossTotal), "negative",
                "A document total cannot be negative. A supplier crediting the company sends a "
                + "credit note, which is a different document."));
        }

        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}

/// <summary>Reconciles the document.</summary>
public sealed class ReconcileSupplierInvoiceCommandHandler
    : ICommandHandler<ReconcileSupplierInvoiceCommand>
{
    private readonly ISupplierInvoiceRepository _invoices;
    private readonly IPurchasingUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public ReconcileSupplierInvoiceCommandHandler(
        ISupplierInvoiceRepository invoices,
        IPurchasingUnitOfWork unitOfWork)
    {
        _invoices = invoices;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        ReconcileSupplierInvoiceCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        SupplierInvoice? invoice = await _invoices
            .GetByIdAsync(new SupplierInvoiceId(request.SupplierInvoiceId), cancellationToken)
            .ConfigureAwait(false);

        if (invoice is null)
        {
            return PurchasingErrors.Invoice.NotFound(request.SupplierInvoiceId.ToString());
        }

        Result reconciled = invoice.Reconcile(
            request.DocumentNumber,
            request.DocumentDate,
            Money.Of(request.StatedGrossTotal, invoice.Currency),
            Tolerance(invoice.Currency));

        if (reconciled.IsFailure)
        {
            return reconciled;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }

    /// <summary>
    /// How far apart the two totals may be and still count as agreeing: two of the currency's
    /// smallest units.
    /// <para>
    /// Not configurable, on purpose. A tolerance somebody can raise is a tolerance that gets
    /// raised the first afternoon a supplier's document is fifty euros out and the person
    /// reconciling it is in a hurry — and after that the check is decoration. Two cents is what
    /// rounding costs across two VAT bands; anything larger is a decision, and a decision belongs
    /// to a person with a reason, which is what accepting a disputed figure asks for.
    /// </para>
    /// </summary>
    private static Money Tolerance(Currency currency) =>
        Money.Of(2m / (decimal)Math.Pow(10, currency.DecimalPlaces), currency);
}

/// <summary>
/// Accepts the supplier's figure on a disputed document, with a reason somebody wrote.
/// </summary>
/// <param name="SupplierInvoiceId">The document.</param>
/// <param name="Reason">Why the difference is being accepted.</param>
public sealed record AcceptSupplierFigureCommand(
    Guid SupplierInvoiceId,
    string Reason) : ICommand;

/// <summary>Accepts the figure.</summary>
public sealed class AcceptSupplierFigureCommandHandler
    : ICommandHandler<AcceptSupplierFigureCommand>
{
    private readonly ISupplierInvoiceRepository _invoices;
    private readonly IPurchasingUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public AcceptSupplierFigureCommandHandler(
        ISupplierInvoiceRepository invoices,
        IPurchasingUnitOfWork unitOfWork)
    {
        _invoices = invoices;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        AcceptSupplierFigureCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        SupplierInvoice? invoice = await _invoices
            .GetByIdAsync(new SupplierInvoiceId(request.SupplierInvoiceId), cancellationToken)
            .ConfigureAwait(false);

        if (invoice is null)
        {
            return PurchasingErrors.Invoice.NotFound(request.SupplierInvoiceId.ToString());
        }

        Result accepted = invoice.AcceptSupplierFigure(request.Reason);

        if (accepted.IsFailure)
        {
            return accepted;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>Sends a disputed document back to draft so a line can be corrected.</summary>
/// <param name="SupplierInvoiceId">The document.</param>
/// <param name="Reason">What is being corrected.</param>
public sealed record ReopenSupplierInvoiceCommand(
    Guid SupplierInvoiceId,
    string Reason) : ICommand;

/// <summary>Reopens the document.</summary>
public sealed class ReopenSupplierInvoiceCommandHandler
    : ICommandHandler<ReopenSupplierInvoiceCommand>
{
    private readonly ISupplierInvoiceRepository _invoices;
    private readonly IPurchasingUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public ReopenSupplierInvoiceCommandHandler(
        ISupplierInvoiceRepository invoices,
        IPurchasingUnitOfWork unitOfWork)
    {
        _invoices = invoices;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        ReopenSupplierInvoiceCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        SupplierInvoice? invoice = await _invoices
            .GetByIdAsync(new SupplierInvoiceId(request.SupplierInvoiceId), cancellationToken)
            .ConfigureAwait(false);

        if (invoice is null)
        {
            return PurchasingErrors.Invoice.NotFound(request.SupplierInvoiceId.ToString());
        }

        Result reopened = invoice.Reopen(request.Reason);

        if (reopened.IsFailure)
        {
            return reopened;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>
/// Corrects one line to the price the supplier's document actually charges.
/// <para>
/// Does not touch the agreed price. A supplier who has genuinely raised their prices is a new
/// agreed price with a date on it, recorded on purpose.
/// </para>
/// </summary>
/// <param name="SupplierInvoiceId">The document.</param>
/// <param name="LineId">The line.</param>
/// <param name="UnitPrice">What their document says one unit costs.</param>
public sealed record AcceptSupplierLinePriceCommand(
    Guid SupplierInvoiceId,
    Guid LineId,
    decimal UnitPrice) : ICommand;

/// <summary>Corrects the line.</summary>
public sealed class AcceptSupplierLinePriceCommandHandler
    : ICommandHandler<AcceptSupplierLinePriceCommand>
{
    private readonly ISupplierInvoiceRepository _invoices;
    private readonly IPurchasingUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public AcceptSupplierLinePriceCommandHandler(
        ISupplierInvoiceRepository invoices,
        IPurchasingUnitOfWork unitOfWork)
    {
        _invoices = invoices;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        AcceptSupplierLinePriceCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        SupplierInvoice? invoice = await _invoices
            .GetByIdAsync(new SupplierInvoiceId(request.SupplierInvoiceId), cancellationToken)
            .ConfigureAwait(false);

        if (invoice is null)
        {
            return PurchasingErrors.Invoice.NotFound(request.SupplierInvoiceId.ToString());
        }

        Result accepted = invoice.AcceptLinePrice(
            new SupplierInvoiceLineId(request.LineId),
            Money.Of(request.UnitPrice, invoice.Currency));

        if (accepted.IsFailure)
        {
            return accepted;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>Withdraws a draft opened by mistake.</summary>
/// <param name="SupplierInvoiceId">The document.</param>
/// <param name="Reason">Why.</param>
public sealed record CancelSupplierInvoiceCommand(
    Guid SupplierInvoiceId,
    string Reason) : ICommand;

/// <summary>Cancels the draft.</summary>
public sealed class CancelSupplierInvoiceCommandHandler
    : ICommandHandler<CancelSupplierInvoiceCommand>
{
    private readonly ISupplierInvoiceRepository _invoices;
    private readonly IPurchasingUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public CancelSupplierInvoiceCommandHandler(
        ISupplierInvoiceRepository invoices,
        IPurchasingUnitOfWork unitOfWork)
    {
        _invoices = invoices;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        CancelSupplierInvoiceCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        SupplierInvoice? invoice = await _invoices
            .GetByIdAsync(new SupplierInvoiceId(request.SupplierInvoiceId), cancellationToken)
            .ConfigureAwait(false);

        if (invoice is null)
        {
            return PurchasingErrors.Invoice.NotFound(request.SupplierInvoiceId.ToString());
        }

        Result cancelled = invoice.Cancel(request.Reason);

        if (cancelled.IsFailure)
        {
            return cancelled;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
