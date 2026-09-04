using AutoPartsErp.Modules.Pricing.Domain;
using AutoPartsErp.Modules.Pricing.Domain.Customers;
using AutoPartsErp.Modules.Pricing.Domain.PriceLists;
using AutoPartsErp.SharedKernel.Abstractions;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Pricing.Application.Customers.Commands;

/// <summary>Records what was agreed with a customer.</summary>
/// <param name="CustomerId">The customer.</param>
/// <param name="PriceListId">The list they buy from.</param>
/// <param name="DiscountPercent">What comes off it, 0 to 100.</param>
/// <param name="EffectiveFrom">The first day it applies, or null for always.</param>
/// <param name="EffectiveTo">The last day it applies, or null for never expiring.</param>
/// <param name="Note">Why it exists, for whoever inherits the account.</param>
public sealed record AgreeCustomerPricingCommand(
    Guid CustomerId,
    Guid PriceListId,
    decimal DiscountPercent = 0m,
    DateOnly? EffectiveFrom = null,
    DateOnly? EffectiveTo = null,
    string? Note = null) : ICommand<Guid>;

/// <summary>Checks the shape of an <see cref="AgreeCustomerPricingCommand"/>.</summary>
public sealed class AgreeCustomerPricingCommandValidator : IValidator<AgreeCustomerPricingCommand>
{
    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(
        AgreeCustomerPricingCommand instance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var failures = new List<ValidationFailure>();

        if (instance.CustomerId == Guid.Empty)
        {
            failures.Add(new ValidationFailure(
                nameof(instance.CustomerId), "required", "A customer is required."));
        }

        if (instance.PriceListId == Guid.Empty)
        {
            failures.Add(new ValidationFailure(
                nameof(instance.PriceListId), "required", "A price list is required."));
        }

        if (instance.DiscountPercent is < 0m or > 100m)
        {
            failures.Add(new ValidationFailure(
                nameof(instance.DiscountPercent), "range",
                "A discount must be between 0 and 100 percent."));
        }

        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}

/// <summary>Records the agreement.</summary>
public sealed class AgreeCustomerPricingCommandHandler
    : ICommandHandler<AgreeCustomerPricingCommand, Guid>
{
    private readonly ICustomerPricingRepository _agreements;
    private readonly IPriceListRepository _lists;
    private readonly IPricingUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public AgreeCustomerPricingCommandHandler(
        ICustomerPricingRepository agreements,
        IPriceListRepository lists,
        IPricingUnitOfWork unitOfWork)
    {
        _agreements = agreements;
        _lists = lists;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result<Guid>> HandleAsync(
        AgreeCustomerPricingCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var customerId = new CustomerRef(request.CustomerId);

        CustomerPricing? existing = await _agreements
            .GetForCustomerAsync(customerId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return Result.Failure<Guid>(PricingErrors.Agreement.AlreadyAgreed);
        }

        // Pointing a customer at a list that was withdrawn last year is how somebody ends up with
        // no price at all and nobody can see why. Checked here because the agreement aggregate
        // holds the list by identity and cannot see its state.
        PriceList? list = await _lists
            .GetByIdAsync(new PriceListId(request.PriceListId), cancellationToken)
            .ConfigureAwait(false);

        if (list is null)
        {
            return Result.Failure<Guid>(PricingErrors.List.NotFound(request.PriceListId.ToString()));
        }

        if (list.Status == PriceListStatus.Archived)
        {
            return Result.Failure<Guid>(PricingErrors.List.Archived);
        }

        Result<CustomerPricing> agreement = CustomerPricing.Agree(
            customerId,
            list.Id,
            request.DiscountPercent,
            request.EffectiveFrom,
            request.EffectiveTo,
            request.Note);

        if (agreement.IsFailure)
        {
            return Result.Failure<Guid>(agreement.Error);
        }

        _agreements.Add(agreement.Value);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return agreement.Value.Id.Value;
    }
}

/// <summary>Changes a customer's terms: a different list, a different discount, or both.</summary>
/// <param name="CustomerId">The customer.</param>
/// <param name="PriceListId">The list they buy from now.</param>
/// <param name="DiscountPercent">What comes off it now, 0 to 100.</param>
/// <param name="EffectiveFrom">The new first day, or null for always.</param>
/// <param name="EffectiveTo">The new last day, or null for never expiring.</param>
/// <param name="Note">Why it changed.</param>
public sealed record RenegotiateCustomerPricingCommand(
    Guid CustomerId,
    Guid PriceListId,
    decimal DiscountPercent = 0m,
    DateOnly? EffectiveFrom = null,
    DateOnly? EffectiveTo = null,
    string? Note = null) : ICommand;

/// <summary>Changes the terms.</summary>
public sealed class RenegotiateCustomerPricingCommandHandler
    : ICommandHandler<RenegotiateCustomerPricingCommand>
{
    private readonly ICustomerPricingRepository _agreements;
    private readonly IPriceListRepository _lists;
    private readonly IPricingUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public RenegotiateCustomerPricingCommandHandler(
        ICustomerPricingRepository agreements,
        IPriceListRepository lists,
        IPricingUnitOfWork unitOfWork)
    {
        _agreements = agreements;
        _lists = lists;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        RenegotiateCustomerPricingCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        CustomerPricing? agreement = await _agreements
            .GetForCustomerAsync(new CustomerRef(request.CustomerId), cancellationToken)
            .ConfigureAwait(false);

        if (agreement is null)
        {
            return PricingErrors.Agreement.NotFound(request.CustomerId.ToString());
        }

        PriceList? list = await _lists
            .GetByIdAsync(new PriceListId(request.PriceListId), cancellationToken)
            .ConfigureAwait(false);

        if (list is null)
        {
            return PricingErrors.List.NotFound(request.PriceListId.ToString());
        }

        if (list.Status == PriceListStatus.Archived)
        {
            return PricingErrors.List.Archived;
        }

        Result renegotiated = agreement.Renegotiate(
            list.Id,
            request.DiscountPercent,
            request.EffectiveFrom,
            request.EffectiveTo,
            request.Note);

        if (renegotiated.IsFailure)
        {
            return renegotiated;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>Ends a customer's terms, sending them back to the default list.</summary>
/// <param name="CustomerId">The customer.</param>
/// <param name="On">The last day the terms apply. Defaults to today.</param>
public sealed record EndCustomerPricingCommand(Guid CustomerId, DateOnly? On = null) : ICommand;

/// <summary>Ends the terms.</summary>
public sealed class EndCustomerPricingCommandHandler : ICommandHandler<EndCustomerPricingCommand>
{
    private readonly ICustomerPricingRepository _agreements;
    private readonly IPricingUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    /// <summary>Initializes the handler.</summary>
    public EndCustomerPricingCommandHandler(
        ICustomerPricingRepository agreements,
        IPricingUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _agreements = agreements;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        EndCustomerPricingCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        CustomerPricing? agreement = await _agreements
            .GetForCustomerAsync(new CustomerRef(request.CustomerId), cancellationToken)
            .ConfigureAwait(false);

        if (agreement is null)
        {
            return PricingErrors.Agreement.NotFound(request.CustomerId.ToString());
        }

        Result ended = agreement.End(request.On ?? _clock.TodayUtc);

        if (ended.IsFailure)
        {
            return ended;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
