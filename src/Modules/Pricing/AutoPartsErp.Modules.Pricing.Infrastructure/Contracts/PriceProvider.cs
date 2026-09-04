using AutoPartsErp.ModuleContracts.Pricing;
using AutoPartsErp.Modules.Pricing.Domain;
using AutoPartsErp.Modules.Pricing.Domain.Customers;
using AutoPartsErp.Modules.Pricing.Domain.Quotes;
using AutoPartsErp.SharedKernel.Abstractions;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Pricing.Infrastructure.Contracts;

/// <summary>
/// Pricing's answer to "what does this cost?".
/// <para>
/// A thin shell over <see cref="PriceResolution"/>, and deliberately thin: the rules are in the
/// domain where they can be tested without a database, and this fetches the three things they
/// need and hands the answer across the boundary as a flat record.
/// </para>
/// <para>
/// It lives in Infrastructure rather than Application because it is an adapter, like Inventory's
/// availability and Partners' directory — a port another module holds, implemented by the module
/// that owns the data.
/// </para>
/// </summary>
public sealed class PriceProvider : IPriceProvider
{
    private readonly IPriceCandidateSource _candidates;
    private readonly ICustomerPricingRepository _agreements;
    private readonly IDateTimeProvider _clock;

    /// <summary>Initializes the adapter.</summary>
    public PriceProvider(
        IPriceCandidateSource candidates,
        ICustomerPricingRepository agreements,
        IDateTimeProvider clock)
    {
        _candidates = candidates;
        _agreements = agreements;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<PartPrice?> GetAsync(
        Guid partId,
        decimal quantity,
        Guid? customerId = null,
        DateOnly? on = null,
        CancellationToken cancellationToken = default)
    {
        if (quantity <= 0m)
        {
            return null;
        }

        DateOnly day = on ?? _clock.TodayUtc;

        CustomerPricing? agreement = customerId is { } customer
            ? await _agreements
                .GetForCustomerAsync(new CustomerRef(customer), cancellationToken)
                .ConfigureAwait(false)
            : null;

        IReadOnlyList<PriceCandidate> candidates = await _candidates
            .GetCandidatesAsync(new PartRef(partId), day, cancellationToken)
            .ConfigureAwait(false);

        Result<PriceQuote> quote = PriceResolution.Resolve(candidates, agreement, quantity, day);

        // Null rather than an error, because this contract answers a question and "nothing prices
        // it" is an answer. The caller turns that into whatever refusal makes sense on its own
        // document — Sales knows the SKU and the order number; this does not.
        if (quote.IsFailure)
        {
            return null;
        }

        PriceQuote resolved = quote.Value;

        return new PartPrice(
            partId,
            quantity,
            resolved.Currency.Code,
            resolved.GrossUnitPrice.Amount,
            resolved.DiscountPercent,
            resolved.NetUnitPrice.Amount,
            resolved.PriceListId.Value,
            resolved.PriceListCode,
            resolved.AppliedBreakQuantity);
    }
}
