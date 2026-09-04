using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Pricing.Domain.PriceLists;

/// <summary>
/// "From this quantity upwards, the unit price is this."
/// <para>
/// The whole of quantity pricing in parts distribution is this one idea repeated: one pad set is
/// €24.50, ten are €22.00 each, fifty are €20.00 each. A break is a floor, not a band — there is
/// no upper bound, because the next break up is the upper bound and expressing it twice is how
/// gaps and overlaps get in.
/// </para>
/// </summary>
public sealed class PriceBreak : ValueObject
{
    private PriceBreak(decimal minimumQuantity, Money unitPrice)
    {
        MinimumQuantity = minimumQuantity;
        UnitPrice = unitPrice;
    }

    /// <summary>
    /// Required by object-relational mappers that materialize this type as an owned value.
    /// Domain code always goes through <see cref="Create"/>.
    /// </summary>
#pragma warning disable CS8618
    private PriceBreak()
    {
    }
#pragma warning restore CS8618

    /// <summary>The quantity at which this price starts to apply.</summary>
    public decimal MinimumQuantity { get; }

    /// <summary>What one unit costs from that quantity upwards.</summary>
    public Money UnitPrice { get; } = null!;

    /// <summary>Creates a break, rejecting the two ways of writing one that cannot mean anything.</summary>
    /// <param name="minimumQuantity">The quantity at which the price starts to apply.</param>
    /// <param name="unitPrice">What one unit costs from there upwards.</param>
    public static Result<PriceBreak> Create(decimal minimumQuantity, Money unitPrice)
    {
        ArgumentNullException.ThrowIfNull(unitPrice);

        if (minimumQuantity <= 0m)
        {
            return PricingErrors.Break.MinimumNotPositive;
        }

        // A price of zero is allowed - free-of-charge lines are real, and a warranty replacement
        // has to be quotable. A NEGATIVE price is not: that is a credit note, which is a document
        // in its own right and not something to smuggle in through a price list.
        return unitPrice.IsNegative
            ? PricingErrors.Break.PriceNegative
            : new PriceBreak(minimumQuantity, unitPrice);
    }

    /// <summary>True when this break applies at the given quantity.</summary>
    public bool AppliesTo(decimal quantity) => quantity >= MinimumQuantity;

    /// <inheritdoc />
    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return MinimumQuantity;
        yield return UnitPrice;
    }

    /// <inheritdoc />
    public override string ToString() => $"{MinimumQuantity}+ @ {UnitPrice}";
}
