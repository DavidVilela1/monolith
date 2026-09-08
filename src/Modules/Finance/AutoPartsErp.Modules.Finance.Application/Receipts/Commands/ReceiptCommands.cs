using AutoPartsErp.Modules.Finance.Domain;
using AutoPartsErp.Modules.Finance.Domain.Receipts;
using AutoPartsErp.Modules.Finance.Domain.Receivables;
using AutoPartsErp.SharedKernel.Abstractions;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Finance.Application.Receipts.Commands;

/// <summary>How much of a receipt goes against one document.</summary>
/// <param name="OpenItemId">The item being paid.</param>
/// <param name="Amount">How much goes against it.</param>
public sealed record AllocationLine(Guid OpenItemId, decimal Amount);

/// <summary>
/// Records money received from a customer, and optionally says what it paid.
/// <para>
/// The allocation is optional because the two acts are separate in real life. A transfer lands in
/// the bank with a reference nobody can read: it is real money on a real date and the customer's
/// balance should show it immediately, whether or not anybody has yet worked out which of the
/// eleven open invoices it was for.
/// </para>
/// </summary>
/// <param name="CustomerId">Who paid.</param>
/// <param name="Amount">How much arrived.</param>
/// <param name="CurrencyCode">The currency it arrived in.</param>
/// <param name="ReceivedOn">The date it arrived. Defaults to today.</param>
/// <param name="Method">Cash, BankTransfer, Cheque, Card, DirectDebit or Other.</param>
/// <param name="Reference">The bank reference or cheque number.</param>
/// <param name="Notes">Anything else worth recording.</param>
/// <param name="Allocations">What it pays, when that is already known.</param>
public sealed record RecordReceiptCommand(
    Guid CustomerId,
    decimal Amount,
    string CurrencyCode,
    DateOnly? ReceivedOn = null,
    string Method = "BankTransfer",
    string? Reference = null,
    string? Notes = null,
    IReadOnlyList<AllocationLine>? Allocations = null) : ICommand<Guid>;

/// <summary>Checks the shape of a <see cref="RecordReceiptCommand"/>.</summary>
public sealed class RecordReceiptCommandValidator : IValidator<RecordReceiptCommand>
{
    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(
        RecordReceiptCommand instance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var failures = new List<ValidationFailure>();

        if (instance.CustomerId == Guid.Empty)
        {
            failures.Add(new ValidationFailure(
                nameof(instance.CustomerId), "required", "Say who the money came from."));
        }

        if (instance.Amount <= 0m)
        {
            failures.Add(new ValidationFailure(
                nameof(instance.Amount),
                "not_positive",
                "A receipt is for a positive amount. Money going the other way is a refund."));
        }

        if (!Currency.TryFromCode(instance.CurrencyCode, out _))
        {
            failures.Add(new ValidationFailure(
                nameof(instance.CurrencyCode),
                "unknown",
                $"'{instance.CurrencyCode}' is not a currency this system knows."));
        }

        if (!Enum.TryParse<ReceiptMethod>(instance.Method, ignoreCase: true, out ReceiptMethod method)
            || method == ReceiptMethod.Unknown)
        {
            failures.Add(new ValidationFailure(
                nameof(instance.Method),
                "unknown",
                "Say how the money arrived: Cash, BankTransfer, Cheque, Card, DirectDebit or "
                + "Other."));
        }

        if (instance.Allocations is { Count: > 0 } lines
            && lines.Select(line => line.OpenItemId).Distinct().Count() != lines.Count)
        {
            failures.Add(new ValidationFailure(
                nameof(instance.Allocations),
                "duplicate_item",
                "The same document appears more than once. Give each one a single amount."));
        }

        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}

/// <summary>Records the receipt, and matches it when the caller said what it paid.</summary>
public sealed class RecordReceiptCommandHandler : ICommandHandler<RecordReceiptCommand, Guid>
{
    private readonly IReceiptRepository _receipts;
    private readonly IOpenItemRepository _items;
    private readonly IFinanceUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    /// <summary>Initializes the handler.</summary>
    public RecordReceiptCommandHandler(
        IReceiptRepository receipts,
        IOpenItemRepository items,
        IFinanceUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _receipts = receipts;
        _items = items;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result<Guid>> HandleAsync(
        RecordReceiptCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Currency currency = Currency.FromCode(request.CurrencyCode);
        DateOnly receivedOn = request.ReceivedOn ?? _clock.TodayUtc;

        string number = await _receipts
            .NextReceiptNumberAsync(receivedOn.Year, cancellationToken)
            .ConfigureAwait(false);

        Result<Receipt> recorded = Receipt.Record(
            number,
            new CustomerRef(request.CustomerId),
            Money.Of(request.Amount, currency),
            receivedOn,
            Enum.Parse<ReceiptMethod>(request.Method, ignoreCase: true),
            request.Reference,
            request.Notes);

        if (recorded.IsFailure)
        {
            return Result.Failure<Guid>(recorded.Error);
        }

        Receipt receipt = recorded.Value;

        if (request.Allocations is { Count: > 0 } lines)
        {
            Result matched = await SettleAsync(receipt, lines, cancellationToken)
                .ConfigureAwait(false);

            if (matched.IsFailure)
            {
                // Nothing is saved, so the money is not recorded either. That is deliberate: a
                // caller who said what the receipt pays and got it wrong should fix the
                // allocation and send it again, rather than discover that the money went in and
                // the matching did not.
                return Result.Failure<Guid>(matched.Error);
            }
        }

        receipt.Announce();
        _receipts.Add(receipt);

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return receipt.Id.Value;
    }

    private async Task<Result> SettleAsync(
        Receipt receipt,
        IReadOnlyList<AllocationLine> lines,
        CancellationToken cancellationToken)
    {
        List<OpenItemId> ids = [.. lines.Select(line => new OpenItemId(line.OpenItemId))];

        IReadOnlyList<OpenItem> items = await _items
            .GetManyAsync(ids, cancellationToken)
            .ConfigureAwait(false);

        Dictionary<OpenItemId, OpenItem> byId = items.ToDictionary(item => item.Id);

        var settlementLines = new List<SettlementLine>(lines.Count);

        foreach (AllocationLine line in lines)
        {
            var id = new OpenItemId(line.OpenItemId);

            if (!byId.TryGetValue(id, out OpenItem? item))
            {
                return FinanceErrors.OpenItem.NotFound(line.OpenItemId.ToString());
            }

            settlementLines.Add(new SettlementLine(item, Money.Of(line.Amount, receipt.Currency)));
        }

        return Settlement.Apply(receipt, settlementLines, _clock.TodayUtc);
    }
}

/// <summary>
/// Matches money already received against documents the customer owes.
/// <para>
/// The other half of <see cref="RecordReceiptCommand"/>, for the common case where the money
/// arrived first and somebody works out what it paid afterwards.
/// </para>
/// </summary>
/// <param name="ReceiptId">The receipt.</param>
/// <param name="Allocations">What it pays, and how much of each.</param>
public sealed record AllocateReceiptCommand(
    Guid ReceiptId,
    IReadOnlyList<AllocationLine> Allocations) : ICommand;

/// <summary>Checks the shape of an <see cref="AllocateReceiptCommand"/>.</summary>
public sealed class AllocateReceiptCommandValidator : IValidator<AllocateReceiptCommand>
{
    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(
        AllocateReceiptCommand instance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var failures = new List<ValidationFailure>();

        if (instance.ReceiptId == Guid.Empty)
        {
            failures.Add(new ValidationFailure(
                nameof(instance.ReceiptId), "required", "Say which receipt is being allocated."));
        }

        if (instance.Allocations is null || instance.Allocations.Count == 0)
        {
            failures.Add(new ValidationFailure(
                nameof(instance.Allocations),
                "required",
                "Say which documents the money pays and how much of each."));
        }
        else if (instance.Allocations.Select(line => line.OpenItemId).Distinct().Count()
                 != instance.Allocations.Count)
        {
            failures.Add(new ValidationFailure(
                nameof(instance.Allocations),
                "duplicate_item",
                "The same document appears more than once. Give each one a single amount."));
        }

        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}

/// <summary>Matches the money.</summary>
public sealed class AllocateReceiptCommandHandler : ICommandHandler<AllocateReceiptCommand>
{
    private readonly IReceiptRepository _receipts;
    private readonly IOpenItemRepository _items;
    private readonly IFinanceUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    /// <summary>Initializes the handler.</summary>
    public AllocateReceiptCommandHandler(
        IReceiptRepository receipts,
        IOpenItemRepository items,
        IFinanceUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _receipts = receipts;
        _items = items;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        AllocateReceiptCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Receipt? receipt = await _receipts
            .GetByIdAsync(new ReceiptId(request.ReceiptId), cancellationToken)
            .ConfigureAwait(false);

        if (receipt is null)
        {
            return FinanceErrors.Receipt.NotFound(request.ReceiptId.ToString());
        }

        List<OpenItemId> ids =
            [.. request.Allocations.Select(line => new OpenItemId(line.OpenItemId))];

        IReadOnlyList<OpenItem> items = await _items
            .GetManyAsync(ids, cancellationToken)
            .ConfigureAwait(false);

        Dictionary<OpenItemId, OpenItem> byId = items.ToDictionary(item => item.Id);

        var lines = new List<SettlementLine>(request.Allocations.Count);

        foreach (AllocationLine line in request.Allocations)
        {
            if (!byId.TryGetValue(new OpenItemId(line.OpenItemId), out OpenItem? item))
            {
                return FinanceErrors.OpenItem.NotFound(line.OpenItemId.ToString());
            }

            lines.Add(new SettlementLine(item, Money.Of(line.Amount, receipt.Currency)));
        }

        Result applied = Settlement.Apply(receipt, lines, _clock.TodayUtc);

        if (applied.IsFailure)
        {
            return applied;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
