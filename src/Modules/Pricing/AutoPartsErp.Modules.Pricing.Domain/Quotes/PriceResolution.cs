using AutoPartsErp.Modules.Pricing.Domain.Customers;
using AutoPartsErp.Modules.Pricing.Domain.PriceLists;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Pricing.Domain.Quotes;

/// <summary>
/// One part, one customer, one quantity, one day, and a candidate set of prices: which one wins.
/// <para>
/// A pure function over data somebody else fetched. It is written this way on purpose — the rules
/// here are the ones that will be argued about, and rules worth arguing about should be testable
/// without a database, a customer record or a clock.
/// </para>
/// <para>
/// The order is: filter to what actually applies today, rank by the list's precedence, take the
/// best price at the quantity, then apply the customer's discount. Every step is somewhere a real
/// distributor has a rule, and the interesting one is the third — see <see cref="Resolve"/>.
/// </para>
/// </summary>
public static class PriceResolution
{
    /// <summary>
    /// Works out what the customer pays.
    /// </summary>
    /// <param name="candidates">
    /// Every list that could apply, each with the entry that prices this part in it. Lists with no
    /// entry for the part are simply absent — a customer list that does not mention a part is not
    /// a refusal, it is a fall-through to the standard list, which is how a distributor prices
    /// twelve negotiated lines and forty thousand ordinary ones.
    /// </param>
    /// <param name="agreement">The customer's terms, or null when they have none.</param>
    /// <param name="quantity">How many are being bought.</param>
    /// <param name="on">The day being priced for.</param>
    public static Result<PriceQuote> Resolve(
        IReadOnlyCollection<PriceCandidate> candidates,
        CustomerPricing? agreement,
        decimal quantity,
        DateOnly on)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        if (quantity <= 0m)
        {
            return PricingErrors.Break.MinimumNotPositive;
        }

        bool agreementApplies = agreement is not null && agreement.IsEffectiveOn(on);

        // A customer list only reaches this customer through their agreement. Without that check a
        // workshop on the trade list would be quoted from whatever other customer list happened to
        // price the part, which is both wrong and the sort of wrong that shows up on somebody
        // else's invoice.
        List<PriceCandidate> applicable =
        [
            .. candidates.Where(candidate =>
                candidate.List.IsEffectiveOn(on)
                && (candidate.List.Kind != PriceListKind.Customer
                    || (agreementApplies && agreement!.PriceListId == candidate.List.Id))),
        ];

        if (applicable.Count == 0)
        {
            return PricingErrors.Quote.NoPrice(candidates.Count == 0 ? "unknown" : "no applicable list");
        }

        decimal discountPercent = agreementApplies ? agreement!.DiscountPercent : 0m;

        // Ranked, not first-match: a promotion beats a customer's own list beats the standard one,
        // and where two lists rank the same the cheaper price wins. Ranking before looking at the
        // price is what makes a promotion a promotion — it applies because it is a promotion, not
        // because it happened to come out lower.
        PriceCandidate? best = null;
        PriceBreak? bestBreak = null;
        decimal smallestMinimum = decimal.MaxValue;

        foreach (PriceCandidate candidate in applicable)
        {
            PriceBreak? applied = candidate.Entry.BreakFor(quantity);

            if (applied is null)
            {
                // Priced, but not at this quantity. Remembered so the refusal can say what the
                // smallest sellable quantity is instead of "no price", which sends somebody
                // looking for a missing price list that is right there.
                smallestMinimum = Math.Min(smallestMinimum, candidate.Entry.MinimumSaleQuantity);
                continue;
            }

            if (best is null || Beats(candidate, applied, best, bestBreak!))
            {
                best = candidate;
                bestBreak = applied;
            }
        }

        if (best is null || bestBreak is null)
        {
            return smallestMinimum == decimal.MaxValue
                ? PricingErrors.Quote.NoPrice("no applicable list")
                : PricingErrors.Quote.BelowMinimumQuantity(quantity, smallestMinimum);
        }

        return PriceQuote.Of(
            best.List.Id,
            best.List.Code,
            bestBreak.UnitPrice,
            discountPercent,
            bestBreak.MinimumQuantity);
    }

    private static bool Beats(
        PriceCandidate candidate,
        PriceBreak candidateBreak,
        PriceCandidate incumbent,
        PriceBreak incumbentBreak)
    {
        if (candidate.List.Precedence != incumbent.List.Precedence)
        {
            return candidate.List.Precedence > incumbent.List.Precedence;
        }

        // Same rank, so the customer gets the better of the two. Comparing across currencies is
        // meaningless, and Money refuses it outright rather than guessing - so where two equally
        // ranked lists disagree on currency, the incumbent stands and the caller's currency check
        // catches it downstream.
        return candidateBreak.UnitPrice.Currency.Equals(incumbentBreak.UnitPrice.Currency)
            && candidateBreak.UnitPrice < incumbentBreak.UnitPrice;
    }
}

/// <summary>
/// One list, and the price it carries for the part being asked about.
/// <para>
/// The pair rather than two parallel collections, because a list without its entry cannot answer
/// anything and an entry without its list has no precedence and no validity period.
/// </para>
/// </summary>
/// <param name="List">The list.</param>
/// <param name="Entry">What it says this part costs.</param>
public sealed record PriceCandidate(PriceList List, PriceListEntry Entry);
