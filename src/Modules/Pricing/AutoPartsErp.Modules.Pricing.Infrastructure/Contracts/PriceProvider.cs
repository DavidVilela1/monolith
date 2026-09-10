using AutoPartsErp.ModuleContracts.Pricing;
using AutoPartsErp.Modules.Pricing.Domain;
using AutoPartsErp.Modules.Pricing.Domain.Customers;
using AutoPartsErp.Modules.Pricing.Domain.PriceLists;
using AutoPartsErp.Modules.Pricing.Domain.Quotes;
using AutoPartsErp.SharedKernel.Abstractions;
using AutoPartsErp.SharedKernel.Results;
using Microsoft.Extensions.Options;

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
    private readonly IPriceListRepository _lists;
    private readonly PricingOptions _options;
    private readonly IDateTimeProvider _clock;

    /// <summary>Initializes the adapter.</summary>
    public PriceProvider(
        IPriceCandidateSource candidates,
        ICustomerPricingRepository agreements,
        IPriceListRepository lists,
        IOptions<PricingOptions> options,
        IDateTimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(options);

        _candidates = candidates;
        _agreements = agreements;
        _lists = lists;
        _options = options.Value;
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

    /// <inheritdoc />
    public async Task<decimal?> GetMinimumMarginPercentAsync(
        Guid? customerId = null,
        CancellationToken cancellationToken = default)
    {
        // Their own list first. A fleet on a contract list sold deliberately thin has a floor
        // that says so, and applying the company figure to them would refuse the contract the
        // company signed.
        if (customerId is { } customer)
        {
            CustomerPricing? agreement = await _agreements
                .GetForCustomerAsync(new CustomerRef(customer), cancellationToken)
                .ConfigureAwait(false);

            if (agreement is not null)
            {
                PriceList? agreed = await _lists
                    .GetByIdAsync(agreement.PriceListId, cancellationToken)
                    .ConfigureAwait(false);

                if (agreed?.MinimumMarginPercent is { } agreedFloor)
                {
                    return agreedFloor;
                }

                // An agreement whose list has no opinion falls through to the company figure,
                // not to the default list's. The default list is where customers with no
                // agreement land; borrowing its floor for somebody who has one would apply a
                // number chosen for walk-ins to a negotiated account.
                return _options.DefaultMinimumMarginPercent;
            }
        }

        PriceList? fallback = await _lists
            .GetDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return fallback?.MinimumMarginPercent ?? _options.DefaultMinimumMarginPercent;
    }
}
