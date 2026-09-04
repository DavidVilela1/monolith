using AutoPartsErp.Modules.Pricing.Application.Contracts;
using AutoPartsErp.Modules.Pricing.Domain;
using AutoPartsErp.Modules.Pricing.Domain.Customers;
using AutoPartsErp.Modules.Pricing.Domain.PriceLists;
using AutoPartsErp.Modules.Pricing.Domain.Quotes;
using AutoPartsErp.SharedKernel.Abstractions;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Pricing.Application.Quotes;

/// <summary>
/// What this customer pays for this part at this quantity today.
/// <para>
/// A query rather than a command, and the one thing the rest of the system will actually call.
/// Everything else in this module exists so that this can be answered without anybody typing a
/// number into a sales line.
/// </para>
/// </summary>
/// <param name="PartId">The part.</param>
/// <param name="Quantity">How many are being bought.</param>
/// <param name="CustomerId">The customer, or null for a walk-in with no account.</param>
/// <param name="CurrencyCode">
/// The currency the document is in. The quote is refused rather than converted when the list
/// disagrees — see the handler.
/// </param>
/// <param name="On">The day being priced for. Defaults to today.</param>
public sealed record QuotePriceQuery(
    Guid PartId,
    decimal Quantity,
    Guid? CustomerId = null,
    string? CurrencyCode = null,
    DateOnly? On = null) : IQuery<PriceQuoteDto>;

/// <summary>Answers the question.</summary>
public sealed class QuotePriceQueryHandler : IQueryHandler<QuotePriceQuery, PriceQuoteDto>
{
    private readonly IPriceCandidateSource _candidates;
    private readonly ICustomerPricingRepository _agreements;
    private readonly IPriceListRepository _lists;
    private readonly IDateTimeProvider _clock;

    /// <summary>Initializes the handler.</summary>
    public QuotePriceQueryHandler(
        IPriceCandidateSource candidates,
        ICustomerPricingRepository agreements,
        IPriceListRepository lists,
        IDateTimeProvider clock)
    {
        _candidates = candidates;
        _agreements = agreements;
        _lists = lists;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result<PriceQuoteDto>> HandleAsync(
        QuotePriceQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Quantity <= 0m)
        {
            return Result.Failure<PriceQuoteDto>(PricingErrors.Break.MinimumNotPositive);
        }

        DateOnly on = request.On ?? _clock.TodayUtc;

        CustomerPricing? agreement = request.CustomerId is { } customerId
            ? await _agreements
                .GetForCustomerAsync(new CustomerRef(customerId), cancellationToken)
                .ConfigureAwait(false)
            : null;

        IReadOnlyList<PriceCandidate> candidates = await _candidates
            .GetCandidatesAsync(new PartRef(request.PartId), on, cancellationToken)
            .ConfigureAwait(false);

        Result<PriceQuote> quote = PriceResolution.Resolve(candidates, agreement, request.Quantity, on);

        // The resolver only sees the lists it was handed. When it finds nothing and the customer
        // has no agreement, the useful answer is usually "there is no default list" rather than
        // "no price" - one is a part somebody forgot to price, the other is a setup nobody has
        // finished, and they get fixed by different people.
        if (quote.IsFailure
            && quote.Error.Code == "pricing.quote.no_price"
            && agreement is null
            && await _lists.GetDefaultAsync(cancellationToken).ConfigureAwait(false) is null)
        {
            return Result.Failure<PriceQuoteDto>(PricingErrors.Quote.NoDefaultList);
        }

        if (quote.IsFailure)
        {
            return Result.Failure<PriceQuoteDto>(quote.Error);
        }

        // Refused, not converted. A sales line that quietly turns dollars into euros at whatever
        // rate somebody configured last year is where exchange-rate losses go to hide.
        if (request.CurrencyCode is { } wanted
            && !string.Equals(wanted, quote.Value.Currency.Code, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<PriceQuoteDto>(
                PricingErrors.Quote.CurrencyMismatch(wanted, quote.Value.Currency.Code));
        }

        PriceQuote resolved = quote.Value;
        Money lineTotal = resolved.NetUnitPrice.Multiply(request.Quantity);

        return new PriceQuoteDto(
            request.PartId,
            request.Quantity,
            resolved.Currency.Code,
            resolved.GrossUnitPrice.Amount,
            resolved.DiscountPercent,
            resolved.NetUnitPrice.Amount,
            lineTotal.Amount,
            resolved.PriceListId.Value,
            resolved.PriceListCode,
            resolved.AppliedBreakQuantity);
    }
}
