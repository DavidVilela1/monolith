using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Purchasing.Domain.Agreements;

/// <summary>
/// The steps a supplier's rebate climbs through as the year's purchases add up.
/// <para>
/// A scale is a set of thresholds on cumulative purchase value, each with the percentage that
/// applies once it is reached. A single step starting at zero is the flat agreed percentage —
/// "desconto de rappel 4%" — so a flat rebate and a scaled one are the same object here rather
/// than two shapes with two sets of arithmetic.
/// </para>
/// <para>
/// <b>The rate reached applies to everything, not only to the excess.</b> Buy 48.000 against a
/// scale of 25.000 → 2% and 50.000 → 3%, and the rebate is 2% of 48.000. Buy 51.000 and it becomes
/// 3% of 51.000 — the whole of it, including the first 25.000. That is how a distribution rappel
/// is written in this market, and it is also why the last two thousand euros of a year are worth
/// chasing: crossing a step is worth more than the goods that crossed it. A marginal scale, where
/// each slice keeps its own rate, is a different contract and this object does not express it.
/// </para>
/// <para>
/// The value is what was bought, net of any discount already on the invoice. Whether it is a
/// year, a quarter or a month is <see cref="SupplierAgreement"/>'s business — a scale is a table
/// of thresholds and knows nothing about calendars.
/// </para>
/// </summary>
public sealed class RappelScale : ValueObject
{
    /// <summary>The most steps one scale may carry.</summary>
    public const int MaxSteps = 12;

    private readonly List<RappelStep> _steps;

    private RappelScale(List<RappelStep> steps)
    {
        _steps = steps;
    }

    /// <summary>Required by EF Core materialization.</summary>
#pragma warning disable CS8618
    private RappelScale()
    {
    }
#pragma warning restore CS8618

    /// <summary>The steps, in ascending order of the value that reaches them.</summary>
    public IReadOnlyList<RappelStep> Steps => _steps;

    /// <summary>The currency every threshold in the scale is expressed in.</summary>
    public Currency Currency => _steps[0].From.Currency;

    /// <summary>The highest rate the scale can ever pay, for a screen showing what is on offer.</summary>
    public decimal BestRatePercent => _steps[^1].Percent;

    /// <summary>
    /// Builds a scale from its steps.
    /// <para>
    /// The steps are sorted here rather than demanded in order, because whoever is typing a
    /// supplier's contract into a screen is reading it off a fax and should not have to.
    /// </para>
    /// </summary>
    /// <param name="steps">The steps. At least one, at most <see cref="MaxSteps"/>.</param>
    public static Result<RappelScale> Of(IEnumerable<RappelStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);

        List<RappelStep> ordered = [.. steps.OrderBy(step => step.From.Amount)];

        if (ordered.Count == 0)
        {
            return PurchasingErrors.Rappel.NoSteps;
        }

        if (ordered.Count > MaxSteps)
        {
            return PurchasingErrors.Rappel.TooManySteps;
        }

        Currency currency = ordered[0].From.Currency;

        for (int index = 0; index < ordered.Count; index++)
        {
            RappelStep step = ordered[index];

            if (step.From.Currency != currency)
            {
                return PurchasingErrors.Rappel.MixedCurrencies;
            }

            if (step.From.IsNegative)
            {
                return PurchasingErrors.Rappel.ThresholdNegative;
            }

            if (step.Percent is <= 0m or >= 100m)
            {
                return PurchasingErrors.Rappel.RateOutOfRange;
            }

            if (index == 0)
            {
                continue;
            }

            RappelStep previous = ordered[index - 1];

            // Two steps at the same threshold is a contract nobody can read: at 50.000 the rebate
            // is both two per cent and three. Refused rather than resolved by picking one.
            if (step.From.Amount == previous.From.Amount)
            {
                return PurchasingErrors.Rappel.DuplicateThreshold;
            }

            // Buying more has to be worth more. A scale that pays less at a higher threshold is
            // somebody having typed the percentages into the wrong rows, and letting it through
            // means a buyer stops ordering at exactly the wrong moment for exactly the wrong
            // reason.
            if (step.Percent <= previous.Percent)
            {
                return PurchasingErrors.Rappel.StepsNotIncreasing;
            }
        }

        return new RappelScale(ordered);
    }

    /// <summary>
    /// Rebuilds a scale from steps that were already checked when they were first agreed.
    /// <para>
    /// Skips the validation deliberately. Rows that reached the database went through
    /// <see cref="Of"/>, and re-running the rules on the way out would mean a scale saved under
    /// one version of them becoming unreadable under the next — which turns a rule change into a
    /// screen that will not open.
    /// </para>
    /// </summary>
    /// <param name="steps">The stored steps.</param>
    internal static RappelScale FromStored(IEnumerable<RappelStep> steps) =>
        new([.. steps.OrderBy(step => step.From.Amount)]);

    /// <summary>A flat rebate, which is a scale with one step starting at nothing.</summary>
    /// <param name="percent">The agreed percentage.</param>
    /// <param name="currency">The currency the agreement is in.</param>
    public static Result<RappelScale> Flat(decimal percent, Currency currency) =>
        Of([new RappelStep(Money.Zero(currency), percent)]);

    /// <summary>
    /// What the rebate is worth on a given cumulative value, as a percentage.
    /// <para>
    /// Zero below the first step. A supplier whose scale starts at 25.000 pays nothing to somebody
    /// who bought 20.000, and reporting the first step's rate to them anyway would put money on a
    /// screen that is never going to arrive.
    /// </para>
    /// </summary>
    /// <param name="cumulativeValue">What has been bought so far in the period.</param>
    public decimal RateFor(Money cumulativeValue)
    {
        ArgumentNullException.ThrowIfNull(cumulativeValue);

        decimal rate = 0m;

        foreach (RappelStep step in _steps)
        {
            if (cumulativeValue.Amount < step.From.Amount)
            {
                break;
            }

            rate = step.Percent;
        }

        return rate;
    }

    /// <summary>
    /// What the rebate is worth in money on a given cumulative value.
    /// <para>
    /// Rounded once, at the end, in the currency's own places. A rebate computed per invoice and
    /// added up is not the same figure as one computed on the total, and the total is the one the
    /// supplier's credit note will be for.
    /// </para>
    /// </summary>
    /// <param name="cumulativeValue">What has been bought so far in the period.</param>
    public Money EarnedOn(Money cumulativeValue)
    {
        ArgumentNullException.ThrowIfNull(cumulativeValue);

        return cumulativeValue.Percentage(RateFor(cumulativeValue));
    }

    /// <summary>
    /// How much more has to be bought to reach the next rate, or null when the top step is
    /// already reached.
    /// <para>
    /// The number a buyer actually wants in December, and the reason a full scale is worth
    /// modelling rather than storing one percentage: "another 1.850 and the whole year goes to
    /// three per cent" is a decision, and "your rebate is two per cent" is not.
    /// </para>
    /// </summary>
    /// <param name="cumulativeValue">What has been bought so far in the period.</param>
    public Money? ToNextStep(Money cumulativeValue)
    {
        ArgumentNullException.ThrowIfNull(cumulativeValue);

        // Walked rather than found. A step is a struct, so List.Find hands back default(RappelStep)
        // when nothing matches instead of null — and default carries a null Money, which turns
        // "already at the top step" into an exception three lines further down. The loop cannot
        // have that shape of accident.
        foreach (RappelStep step in _steps)
        {
            if (step.From.Amount > cumulativeValue.Amount)
            {
                return step.From - cumulativeValue;
            }
        }

        return null;
    }

    /// <inheritdoc />
    protected override IEnumerable<object?> GetEqualityComponents()
    {
        foreach (RappelStep step in _steps)
        {
            yield return step.From;
            yield return step.Percent;
        }
    }
}

/// <summary>
/// One step of a <see cref="RappelScale"/>.
/// <para>
/// A class rather than a record struct, and the reason is storage: EF Core cannot own a collection
/// of value types, so a struct here would model beautifully and fail the moment anybody tried to
/// save one. It is worth knowing that this shape was chosen by the database and not by the domain.
/// </para>
/// </summary>
public sealed class RappelStep : ValueObject
{
    /// <summary>Creates a step.</summary>
    /// <param name="from">The cumulative purchase value that reaches it.</param>
    /// <param name="percent">The rate that then applies to the whole of the value.</param>
    public RappelStep(Money from, decimal percent)
    {
        From = from;
        Percent = percent;
    }

    /// <summary>Required by EF Core materialization.</summary>
#pragma warning disable CS8618
    private RappelStep()
    {
    }
#pragma warning restore CS8618

    /// <summary>
    /// The cumulative purchase value that reaches this step. Inclusive: a step at 25.000 is
    /// reached by buying exactly 25.000, because that is what "a partir de" means on a contract.
    /// </summary>
    public Money From { get; private set; } = null!;

    /// <summary>The rate that then applies to the whole of the value.</summary>
    public decimal Percent { get; private set; }

    /// <inheritdoc />
    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return From;
        yield return Percent;
    }
}
