using AutoPartsErp.ModuleContracts.Partners;
using AutoPartsErp.Modules.Finance.Domain;
using AutoPartsErp.Modules.Finance.Domain.Payables;
using AutoPartsErp.Modules.Finance.Domain.Payments;
using AutoPartsErp.SharedKernel.Abstractions;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Finance.Application.Payments.Commands;

/// <summary>
/// Records money that left, and optionally what it paid.
/// <para>
/// The allocations are optional, deliberately. A transfer goes out on Friday against a statement
/// and which of eleven documents it covered is answered on Monday with the remittance in front of
/// somebody — forcing it now would mean guessing, or not recording the payment and leaving the
/// bank balance wrong in the meantime.
/// </para>
/// </summary>
/// <param name="SupplierId">Who was paid.</param>
/// <param name="Amount">How much left.</param>
/// <param name="PaidOn">The day it left.</param>
/// <param name="Method">Cash, BankTransfer, DirectDebit, Cheque or Card.</param>
/// <param name="Reference">The bank reference or cheque number.</param>
/// <param name="Notes">Anything else worth recording.</param>
/// <param name="Allocations">Which documents it paid, when that is already known.</param>
public sealed record RecordSupplierPaymentCommand(
    Guid SupplierId,
    decimal Amount,
    DateOnly PaidOn,
    string Method,
    string? Reference = null,
    string? Notes = null,
    IReadOnlyList<PaymentAllocationInput>? Allocations = null) : ICommand<Guid>;

/// <summary>One line of a remittance, as a request carries it.</summary>
/// <param name="PayableItemId">The document being paid.</param>
/// <param name="Amount">How much of the payment goes against it.</param>
public sealed record PaymentAllocationInput(Guid PayableItemId, decimal Amount);

/// <summary>Checks the shape of a <see cref="RecordSupplierPaymentCommand"/>.</summary>
public sealed class RecordSupplierPaymentCommandValidator
    : IValidator<RecordSupplierPaymentCommand>
{
    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(
        RecordSupplierPaymentCommand instance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var failures = new List<ValidationFailure>();

        if (instance.SupplierId == Guid.Empty)
        {
            failures.Add(new ValidationFailure(
                nameof(instance.SupplierId), "required", "A supplier is required."));
        }

        if (instance.Amount <= 0m)
        {
            failures.Add(new ValidationFailure(
                nameof(instance.Amount), "not_positive", "A payment has to be above zero."));
        }

        if (!Enum.TryParse(instance.Method, ignoreCase: true, out PaymentMethod method)
            || method == PaymentMethod.Unknown)
        {
            failures.Add(new ValidationFailure(
                nameof(instance.Method), "unknown_method",
                "A payment is Cash, BankTransfer, DirectDebit, Cheque or Card."));
        }

        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}

/// <summary>Records the payment.</summary>
public sealed class RecordSupplierPaymentCommandHandler
    : ICommandHandler<RecordSupplierPaymentCommand, Guid>
{
    private readonly ISupplierPaymentRepository _payments;
    private readonly IPayableItemRepository _payables;
    private readonly IPartnerDirectory _partners;
    private readonly IFinanceUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public RecordSupplierPaymentCommandHandler(
        ISupplierPaymentRepository payments,
        IPayableItemRepository payables,
        IPartnerDirectory partners,
        IFinanceUnitOfWork unitOfWork)
    {
        _payments = payments;
        _payables = payables;
        _partners = partners;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result<Guid>> HandleAsync(
        RecordSupplierPaymentCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The code comes from Partners rather than the request, for the same reason every other
        // snapshot in this system does: a caller who can name the supplier's code can name the
        // wrong one, and a remittance carrying a code that is not theirs is one nobody can match.
        PartnerTradingStatus? partner = await _partners
            .GetAsync(request.SupplierId, cancellationToken)
            .ConfigureAwait(false);

        if (partner is null || !partner.IsSupplier)
        {
            return Result.Failure<Guid>(
                FinanceErrors.Payable.NotFound(request.SupplierId.ToString()));
        }

        var supplier = new SupplierRef(request.SupplierId);

        // The currency comes from the documents being paid, or from the default when none are.
        // A payment that named its own could be recorded in one currency against documents in
        // another, and the mismatch would only be found at allocation time.
        IReadOnlyList<PayableItem> outstanding = await _payables
            .GetOutstandingForAsync(supplier, cancellationToken)
            .ConfigureAwait(false);

        Currency currency = outstanding.Count > 0
            ? outstanding[0].OriginalAmount.Currency
            : Currency.Default;

        string number = await _payments
            .NextPaymentNumberAsync(request.PaidOn.Year, cancellationToken)
            .ConfigureAwait(false);

        Result<SupplierPayment> payment = SupplierPayment.Record(
            number,
            supplier,
            partner.Code,
            Money.Of(request.Amount, currency),
            request.PaidOn,
            Enum.Parse<PaymentMethod>(request.Method, ignoreCase: true),
            request.Reference,
            request.Notes);

        if (payment.IsFailure)
        {
            return Result.Failure<Guid>(payment.Error);
        }

        if (request.Allocations is { Count: > 0 })
        {
            Result allocated = Allocate(
                payment.Value, outstanding, request.Allocations, request.PaidOn);

            if (allocated.IsFailure)
            {
                return Result.Failure<Guid>(allocated.Error);
            }
        }

        payment.Value.Announce();

        _payments.Add(payment.Value);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return payment.Value.Id.Value;
    }

    private static Result Allocate(
        SupplierPayment payment,
        IReadOnlyList<PayableItem> outstanding,
        IReadOnlyList<PaymentAllocationInput> requested,
        DateOnly allocatedOn)
    {
        var lines = new List<PayableSettlementLine>(requested.Count);

        foreach (PaymentAllocationInput input in requested)
        {
            var id = new PayableItemId(input.PayableItemId);

            // Only documents the supplier actually has outstanding. Loading one by id instead
            // would let a remittance name a document on somebody else's account and rely on the
            // settlement service to catch it — which it does, but a lookup that cannot return the
            // wrong thing is better than a check that refuses it.
            PayableItem? item = outstanding.FirstOrDefault(candidate => candidate.Id == id);

            if (item is null)
            {
                return FinanceErrors.Payment.NothingOwed(input.PayableItemId.ToString());
            }

            lines.Add(new PayableSettlementLine(
                item, Money.Of(input.Amount, item.OriginalAmount.Currency)));
        }

        return PayableSettlement.Apply(payment, lines, allocatedOn);
    }
}

/// <summary>
/// Matches a payment that was recorded earlier against the documents it turned out to pay.
/// </summary>
/// <param name="SupplierPaymentId">The payment.</param>
/// <param name="Allocations">Which documents it paid, and how much of each.</param>
public sealed record AllocateSupplierPaymentCommand(
    Guid SupplierPaymentId,
    IReadOnlyList<PaymentAllocationInput> Allocations) : ICommand;

/// <summary>Matches the payment.</summary>
public sealed class AllocateSupplierPaymentCommandHandler
    : ICommandHandler<AllocateSupplierPaymentCommand>
{
    private readonly ISupplierPaymentRepository _payments;
    private readonly IPayableItemRepository _payables;
    private readonly IFinanceUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    /// <summary>Initializes the handler.</summary>
    public AllocateSupplierPaymentCommandHandler(
        ISupplierPaymentRepository payments,
        IPayableItemRepository payables,
        IFinanceUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _payments = payments;
        _payables = payables;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        AllocateSupplierPaymentCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        SupplierPayment? payment = await _payments
            .GetByIdAsync(new SupplierPaymentId(request.SupplierPaymentId), cancellationToken)
            .ConfigureAwait(false);

        if (payment is null)
        {
            return FinanceErrors.Payment.NotFound(request.SupplierPaymentId.ToString());
        }

        IReadOnlyList<PayableItem> outstanding = await _payables
            .GetOutstandingForAsync(payment.SupplierId, cancellationToken)
            .ConfigureAwait(false);

        var lines = new List<PayableSettlementLine>(request.Allocations.Count);

        foreach (PaymentAllocationInput input in request.Allocations)
        {
            var id = new PayableItemId(input.PayableItemId);
            PayableItem? item = outstanding.FirstOrDefault(candidate => candidate.Id == id);

            if (item is null)
            {
                return FinanceErrors.Payment.NothingOwed(input.PayableItemId.ToString());
            }

            lines.Add(new PayableSettlementLine(
                item, Money.Of(input.Amount, item.OriginalAmount.Currency)));
        }

        Result applied = PayableSettlement.Apply(payment, lines, _clock.TodayUtc);

        if (applied.IsFailure)
        {
            return applied;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
