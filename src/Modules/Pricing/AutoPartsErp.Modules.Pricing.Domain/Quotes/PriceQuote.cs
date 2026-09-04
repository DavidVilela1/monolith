using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Pricing.Domain.Quotes;

/// <summary>
/// The answer to "what does this cost?", with the reasoning attached.
/// <para>
/// Not just a number. The counter needs the number, but the argument that follows three weeks
/// later needs to know which list it came from, which break applied and what discount was taken
/// off — and reconstructing that after the fact means re-running rules that may have changed
/// since. So the quote carries it.
/// </para>
/// <para>
/// A value object, and never persisted on its own. What gets stored is whatever the document
/// copies out of it.
/// </para>
/// </summary>
public sealed class PriceQuote : ValueObject
{
    private PriceQuote(
        PriceListId priceListId,
        string priceListCode,
        Money grossUnitPrice,
        decimal discountPercent,
        decimal appliedBreakQuantity)
    {
        PriceListId = priceListId;
        PriceListCode = priceListCode;
        GrossUnitPrice = grossUnitPrice;
        DiscountPercent = discountPercent;
        AppliedBreakQuantity = appliedBreakQuantity;
    }

    /// <summary>The list the price came from.</summary>
    public PriceListId PriceListId { get; }

    /// <summary>Its code, so a document can name it without a second lookup.</summary>
    public string PriceListCode { get; }

    /// <summary>The list price before the customer's own discount.</summary>
    public Money GrossUnitPrice { get; }

    /// <summary>What the customer's agreement takes off it.</summary>
    public decimal DiscountPercent { get; }

    /// <summary>The quantity the applied break starts at, so "why is it €22?" has an answer.</summary>
    public decimal AppliedBreakQuantity { get; }

    /// <summary>
    /// What the customer actually pays per unit.
    /// <para>
    /// Rounded to the currency's precision by <see cref="Money"/>, at this step and only this
    /// step. Applying the discount to the line total instead would give a different answer on
    /// half the lines in a parts order, and the one the customer can check with a calculator is
    /// this one.
    /// </para>
    /// </summary>
    public Money NetUnitPrice => GrossUnitPrice - GrossUnitPrice.Percentage(DiscountPercent);

    /// <summary>The currency the price is in.</summary>
    public Currency Currency => GrossUnitPrice.Currency;

    /// <summary>True when the customer's agreement took anything off.</summary>
    public bool IsDiscounted => DiscountPercent > 0m;

    /// <summary>Builds a quote. Called by the resolver, which is the only thing that should.</summary>
    /// <param name="priceListId">The list the price came from.</param>
    /// <param name="priceListCode">Its code.</param>
    /// <param name="grossUnitPrice">The list price before discount.</param>
    /// <param name="discountPercent">What the agreement takes off.</param>
    /// <param name="appliedBreakQuantity">The quantity the applied break starts at.</param>
    public static PriceQuote Of(
        PriceListId priceListId,
        string priceListCode,
        Money grossUnitPrice,
        decimal discountPercent,
        decimal appliedBreakQuantity)
    {
        ArgumentNullException.ThrowIfNull(grossUnitPrice);

        return new PriceQuote(
            priceListId,
            priceListCode,
            grossUnitPrice,
            discountPercent,
            appliedBreakQuantity);
    }

    /// <inheritdoc />
    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return PriceListId;
        yield return GrossUnitPrice;
        yield return DiscountPercent;
        yield return AppliedBreakQuantity;
    }

    /// <inheritdoc />
    public override string ToString() =>
        IsDiscounted
            ? $"{NetUnitPrice} ({GrossUnitPrice} less {DiscountPercent}%, {PriceListCode})"
            : $"{NetUnitPrice} ({PriceListCode})";
}
