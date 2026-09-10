using AutoPartsErp.Modules.Purchasing.Domain;
using AutoPartsErp.Modules.Purchasing.Domain.Agreements;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Purchasing.Application.Agreements.Commands;

/// <summary>Opens an agreement with a supplier. No rebate until one is set.</summary>
/// <param name="SupplierId">The supplier.</param>
/// <param name="SupplierCode">Their short code.</param>
/// <param name="CurrencyCode">The currency it is written in.</param>
/// <param name="EffectiveFrom">The first day it applies.</param>
public sealed record OpenSupplierAgreementCommand(
    Guid SupplierId,
    string SupplierCode,
    string CurrencyCode,
    DateOnly EffectiveFrom) : ICommand<Guid>;

/// <summary>Checks the shape of an <see cref="OpenSupplierAgreementCommand"/>.</summary>
public sealed class OpenSupplierAgreementCommandValidator
    : IValidator<OpenSupplierAgreementCommand>
{
    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(
        OpenSupplierAgreementCommand instance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var failures = new List<ValidationFailure>();

        if (instance.SupplierId == Guid.Empty)
        {
            failures.Add(new ValidationFailure(
                nameof(instance.SupplierId), "required", "A supplier is required."));
        }

        if (string.IsNullOrWhiteSpace(instance.SupplierCode))
        {
            failures.Add(new ValidationFailure(
                nameof(instance.SupplierCode), "required", "A supplier code is required."));
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

/// <summary>Opens the agreement.</summary>
public sealed class OpenSupplierAgreementCommandHandler
    : ICommandHandler<OpenSupplierAgreementCommand, Guid>
{
    private readonly ISupplierAgreementRepository _agreements;
    private readonly IPurchasingUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public OpenSupplierAgreementCommandHandler(
        ISupplierAgreementRepository agreements,
        IPurchasingUnitOfWork unitOfWork)
    {
        _agreements = agreements;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result<Guid>> HandleAsync(
        OpenSupplierAgreementCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var supplier = new SupplierRef(request.SupplierId);

        // Checked here so a second agreement comes back as a 409 with something to read rather
        // than a 500 with a constraint name in it. The filtered unique index is still there and
        // still authoritative under a race.
        if (await _agreements
            .HasLiveAgreementAsync(supplier, null, cancellationToken)
            .ConfigureAwait(false))
        {
            return Result.Failure<Guid>(PurchasingErrors.Agreement.AlreadyAgreed);
        }

        Result<SupplierAgreement> agreement = SupplierAgreement.Open(
            supplier,
            request.SupplierCode,
            Currency.FromCode(request.CurrencyCode),
            request.EffectiveFrom);

        if (agreement.IsFailure)
        {
            return Result.Failure<Guid>(agreement.Error);
        }

        _agreements.Add(agreement.Value);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return agreement.Value.Id.Value;
    }
}

/// <summary>
/// Sets the supplier's rebate, in either of the two shapes it comes in.
/// </summary>
/// <param name="SupplierAgreementId">The agreement.</param>
/// <param name="Basis">
/// <c>OnInvoice</c> when the supplier takes it off their own document, <c>PeriodCreditNote</c>
/// when it arrives afterwards, <c>None</c> to drop it.
/// </param>
/// <param name="Period">Monthly, Quarterly or Annual. Required for a credit-note rebate.</param>
/// <param name="Steps">
/// The steps. One step starting at zero is a flat agreed percentage, which is the ordinary case.
/// </param>
public sealed record SetSupplierRebateCommand(
    Guid SupplierAgreementId,
    string Basis,
    string? Period,
    IReadOnlyList<RebateStepInput> Steps) : ICommand;

/// <summary>One step of a rebate scale, as a request carries it.</summary>
/// <param name="FromValue">The cumulative purchase value that reaches this step.</param>
/// <param name="Percent">The rate that then applies to the whole of the value.</param>
public sealed record RebateStepInput(decimal FromValue, decimal Percent);

/// <summary>Checks the shape of a <see cref="SetSupplierRebateCommand"/>.</summary>
public sealed class SetSupplierRebateCommandValidator : IValidator<SetSupplierRebateCommand>
{
    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(
        SetSupplierRebateCommand instance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var failures = new List<ValidationFailure>();

        if (!Enum.TryParse(instance.Basis, ignoreCase: true, out RappelBasis basis))
        {
            failures.Add(new ValidationFailure(
                nameof(instance.Basis), "unknown_basis",
                "A rebate is settled OnInvoice, by PeriodCreditNote, or None."));
        }
        else if (basis != RappelBasis.None && (instance.Steps is null || instance.Steps.Count == 0))
        {
            failures.Add(new ValidationFailure(
                nameof(instance.Steps), "required",
                "A rebate needs at least one step. A flat percentage is one step starting at zero."));
        }

        if (instance.Period is not null
            && !Enum.TryParse(instance.Period, ignoreCase: true, out RappelPeriod _))
        {
            failures.Add(new ValidationFailure(
                nameof(instance.Period), "unknown_period",
                "A rebate period is Monthly, Quarterly or Annual."));
        }

        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}

/// <summary>Sets the rebate.</summary>
public sealed class SetSupplierRebateCommandHandler : ICommandHandler<SetSupplierRebateCommand>
{
    private readonly ISupplierAgreementRepository _agreements;
    private readonly IPurchasingUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public SetSupplierRebateCommandHandler(
        ISupplierAgreementRepository agreements,
        IPurchasingUnitOfWork unitOfWork)
    {
        _agreements = agreements;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        SetSupplierRebateCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        SupplierAgreement? agreement = await _agreements
            .GetByIdAsync(new SupplierAgreementId(request.SupplierAgreementId), cancellationToken)
            .ConfigureAwait(false);

        if (agreement is null)
        {
            return PurchasingErrors.Agreement.NotFound(request.SupplierAgreementId.ToString());
        }

        var basis = Enum.Parse<RappelBasis>(request.Basis, ignoreCase: true);

        if (basis == RappelBasis.None)
        {
            agreement.DropRebate();
            await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success();
        }

        // The thresholds take the agreement's currency rather than one the caller states. A
        // request that could name its own would be a way of writing a scale the agreement then
        // refuses, and the caller has no better answer than the agreement itself.
        Result<RappelScale> scale = RappelScale.Of(
            request.Steps.Select(step =>
                new RappelStep(Money.Of(step.FromValue, agreement.Currency), step.Percent)));

        if (scale.IsFailure)
        {
            return scale.Error;
        }

        Result set = basis == RappelBasis.OnInvoice
            ? agreement.RebateOnInvoice(scale.Value)
            : agreement.RebateByCreditNote(
                scale.Value,
                request.Period is null
                    ? RappelPeriod.None
                    : Enum.Parse<RappelPeriod>(request.Period, ignoreCase: true));

        if (set.IsFailure)
        {
            return set;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>Ends an agreement. Deliveries after the last day price themselves at the order's figure.</summary>
/// <param name="SupplierAgreementId">The agreement.</param>
/// <param name="LastDay">The last day it applies, inclusive.</param>
/// <param name="Note">Why it ended.</param>
public sealed record EndSupplierAgreementCommand(
    Guid SupplierAgreementId,
    DateOnly LastDay,
    string? Note = null) : ICommand;

/// <summary>Ends the agreement.</summary>
public sealed class EndSupplierAgreementCommandHandler
    : ICommandHandler<EndSupplierAgreementCommand>
{
    private readonly ISupplierAgreementRepository _agreements;
    private readonly IPurchasingUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public EndSupplierAgreementCommandHandler(
        ISupplierAgreementRepository agreements,
        IPurchasingUnitOfWork unitOfWork)
    {
        _agreements = agreements;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        EndSupplierAgreementCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        SupplierAgreement? agreement = await _agreements
            .GetByIdAsync(new SupplierAgreementId(request.SupplierAgreementId), cancellationToken)
            .ConfigureAwait(false);

        if (agreement is null)
        {
            return PurchasingErrors.Agreement.NotFound(request.SupplierAgreementId.ToString());
        }

        Result noted = agreement.SetNote(request.Note);

        if (noted.IsFailure)
        {
            return noted;
        }

        Result ended = agreement.End(request.LastDay);

        if (ended.IsFailure)
        {
            return ended;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>
/// Records what a supplier charges for a part from a given day.
/// <para>
/// A rise is this command with a new day, not an edit. Both rows survive, and the delivery that
/// arrived in August is still explained by the price that was in force in August.
/// </para>
/// </summary>
/// <param name="SupplierId">The supplier.</param>
/// <param name="PartId">The part.</param>
/// <param name="UnitPrice">What one unit costs, before any rebate.</param>
/// <param name="EffectiveFrom">The first day it applies.</param>
/// <param name="SupplierPartNumber">Their reference for the part, as it appears on their paperwork.</param>
public sealed record AgreeSupplierPriceCommand(
    Guid SupplierId,
    Guid PartId,
    decimal UnitPrice,
    DateOnly EffectiveFrom,
    string? SupplierPartNumber = null) : ICommand<Guid>;

/// <summary>Records the price.</summary>
public sealed class AgreeSupplierPriceCommandHandler
    : ICommandHandler<AgreeSupplierPriceCommand, Guid>
{
    private readonly ISupplierPriceRepository _prices;
    private readonly ISupplierAgreementRepository _agreements;
    private readonly IPurchasingUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public AgreeSupplierPriceCommandHandler(
        ISupplierPriceRepository prices,
        ISupplierAgreementRepository agreements,
        IPurchasingUnitOfWork unitOfWork)
    {
        _prices = prices;
        _agreements = agreements;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result<Guid>> HandleAsync(
        AgreeSupplierPriceCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var supplier = new SupplierRef(request.SupplierId);
        var part = new PartRef(request.PartId);

        if (await _prices
            .ExistsForAsync(supplier, part, request.EffectiveFrom, cancellationToken)
            .ConfigureAwait(false))
        {
            return Result.Failure<Guid>(PurchasingErrors.Agreement.PriceAlreadyAgreed);
        }

        // The currency comes from the agreement, not from the request. A price in a currency the
        // agreement is not written in would be a figure nothing downstream could use: the draft
        // invoice refuses it, silently falls back to the order's price, and nobody finds out why
        // the agreed price is being ignored.
        SupplierAgreement? agreement = await _agreements
            .GetForSupplierAsync(supplier, request.EffectiveFrom, cancellationToken)
            .ConfigureAwait(false);

        if (agreement is null)
        {
            return Result.Failure<Guid>(
                PurchasingErrors.Agreement.NotFound(request.SupplierId.ToString()));
        }

        Result<SupplierPrice> price = SupplierPrice.Agree(
            supplier,
            part,
            Money.Of(request.UnitPrice, agreement.Currency),
            request.EffectiveFrom,
            request.SupplierPartNumber);

        if (price.IsFailure)
        {
            return Result.Failure<Guid>(price.Error);
        }

        _prices.Add(price.Value);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return price.Value.Id.Value;
    }
}

/// <summary>
/// Corrects a price that was typed wrong. For a mistake, not for a change — a change is a new day.
/// </summary>
/// <param name="SupplierPriceId">The price.</param>
/// <param name="UnitPrice">The figure it should have said.</param>
/// <param name="Note">Why it changed.</param>
public sealed record CorrectSupplierPriceCommand(
    Guid SupplierPriceId,
    decimal UnitPrice,
    string? Note = null) : ICommand;

/// <summary>Corrects the price.</summary>
public sealed class CorrectSupplierPriceCommandHandler
    : ICommandHandler<CorrectSupplierPriceCommand>
{
    private readonly ISupplierPriceRepository _prices;
    private readonly IPurchasingUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public CorrectSupplierPriceCommandHandler(
        ISupplierPriceRepository prices,
        IPurchasingUnitOfWork unitOfWork)
    {
        _prices = prices;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        CorrectSupplierPriceCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        SupplierPrice? price = await _prices
            .GetByIdAsync(new SupplierPriceId(request.SupplierPriceId), cancellationToken)
            .ConfigureAwait(false);

        if (price is null)
        {
            return PurchasingErrors.Agreement.NotFound(request.SupplierPriceId.ToString());
        }

        Result corrected = price.Correct(
            Money.Of(request.UnitPrice, price.UnitPrice.Currency), request.Note);

        if (corrected.IsFailure)
        {
            return corrected;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
