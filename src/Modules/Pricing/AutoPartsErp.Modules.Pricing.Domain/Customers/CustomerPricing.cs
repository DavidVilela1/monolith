using AutoPartsErp.Modules.Pricing.Domain.Customers.Events;
using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Pricing.Domain.Customers;

/// <summary>
/// What was agreed with one customer: which list they buy from, and what comes off it.
/// <para>
/// Two separate things, deliberately. A workshop on the trade list with 5% off is not the same
/// arrangement as a workshop on a list where every price is already 5% lower, even when today's
/// figures agree — the first follows the trade list when it moves and the second does not. Sales
/// people negotiate both, and collapsing them into one number loses which was actually agreed.
/// </para>
/// <para>
/// One agreement per customer. Layering several would need a rule for which wins, and that rule
/// is the price list's <see cref="PriceLists.PriceList.Precedence"/> — putting a second one here
/// would mean two different answers to the same question.
/// </para>
/// </summary>
public sealed class CustomerPricing
    : AggregateRoot<CustomerPricingId>, IAuditable, ISoftDeletable, ITenantScoped
{
    /// <summary>Longest permitted note.</summary>
    public const int MaxNoteLength = 300;

    private CustomerPricing(
        CustomerPricingId id,
        CustomerRef customerId,
        PriceListId priceListId,
        decimal discountPercent,
        DateOnly? effectiveFrom,
        DateOnly? effectiveTo,
        string? note)
        : base(id)
    {
        CustomerId = customerId;
        PriceListId = priceListId;
        DiscountPercent = discountPercent;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        Note = note;
        CreatedBy = string.Empty;
    }

    /// <summary>Required by EF Core materialization.</summary>
#pragma warning disable CS8618
    private CustomerPricing()
    {
    }
#pragma warning restore CS8618

    /// <summary>The customer the agreement is with.</summary>
    public CustomerRef CustomerId { get; private set; }

    /// <summary>The list they buy from.</summary>
    public PriceListId PriceListId { get; private set; }

    /// <summary>
    /// What comes off that list's prices, as a percentage.
    /// <para>
    /// Applied after the quantity break, not before. A customer on 5% buying fifty gets 5% off
    /// the fifty-up price, which is what everybody assumes and is worth stating because doing it
    /// the other way round is both defensible and wrong.
    /// </para>
    /// </summary>
    public decimal DiscountPercent { get; private set; }

    /// <summary>The first day the agreement applies. Null means it has always applied.</summary>
    public DateOnly? EffectiveFrom { get; private set; }

    /// <summary>The last day it applies, inclusive. Null means it does not expire.</summary>
    public DateOnly? EffectiveTo { get; private set; }

    /// <summary>Why the agreement exists, for whoever inherits the account.</summary>
    public string? Note { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <inheritdoc />
    public string CreatedBy { get; set; } = string.Empty;

    /// <inheritdoc />
    public DateTimeOffset? ModifiedAtUtc { get; set; }

    /// <inheritdoc />
    public string? ModifiedBy { get; set; }

    /// <inheritdoc />
    public bool IsDeleted { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? DeletedAtUtc { get; set; }

    /// <inheritdoc />
    public string? DeletedBy { get; set; }

    /// <summary>Records what was agreed with a customer.</summary>
    /// <param name="customerId">The customer.</param>
    /// <param name="priceListId">The list they buy from.</param>
    /// <param name="discountPercent">What comes off that list, 0 to 100.</param>
    /// <param name="effectiveFrom">The first day it applies, or null for always.</param>
    /// <param name="effectiveTo">The last day it applies, or null for never expiring.</param>
    /// <param name="note">Why it exists.</param>
    public static Result<CustomerPricing> Agree(
        CustomerRef customerId,
        PriceListId priceListId,
        decimal discountPercent = 0m,
        DateOnly? effectiveFrom = null,
        DateOnly? effectiveTo = null,
        string? note = null)
    {
        if (customerId.IsEmpty)
        {
            return PricingErrors.Agreement.CustomerRequired;
        }

        if (priceListId.IsEmpty)
        {
            return PricingErrors.Agreement.ListRequired;
        }

        if (discountPercent is < 0m or > 100m)
        {
            return PricingErrors.Agreement.DiscountOutOfRange;
        }

        if (effectiveFrom is { } from && effectiveTo is { } to && to < from)
        {
            return PricingErrors.Agreement.PeriodInverted;
        }

        if (note is not null && note.Trim().Length > MaxNoteLength)
        {
            return PricingErrors.Agreement.NoteTooLong;
        }

        var agreement = new CustomerPricing(
            CustomerPricingId.New(),
            customerId,
            priceListId,
            discountPercent,
            effectiveFrom,
            effectiveTo,
            string.IsNullOrWhiteSpace(note) ? null : note.Trim());

        agreement.Raise(new CustomerPricingAgreedDomainEvent(
            agreement.Id, customerId, priceListId, discountPercent));

        return agreement;
    }

    /// <summary>Renegotiates the agreement: a different list, a different discount, or both.</summary>
    /// <param name="priceListId">The list they buy from now.</param>
    /// <param name="discountPercent">What comes off it now, 0 to 100.</param>
    /// <param name="effectiveFrom">The new first day, or null for always.</param>
    /// <param name="effectiveTo">The new last day, or null for never expiring.</param>
    /// <param name="note">Why it changed.</param>
    public Result Renegotiate(
        PriceListId priceListId,
        decimal discountPercent,
        DateOnly? effectiveFrom = null,
        DateOnly? effectiveTo = null,
        string? note = null)
    {
        if (priceListId.IsEmpty)
        {
            return PricingErrors.Agreement.ListRequired;
        }

        if (discountPercent is < 0m or > 100m)
        {
            return PricingErrors.Agreement.DiscountOutOfRange;
        }

        if (effectiveFrom is { } from && effectiveTo is { } to && to < from)
        {
            return PricingErrors.Agreement.PeriodInverted;
        }

        if (note is not null && note.Trim().Length > MaxNoteLength)
        {
            return PricingErrors.Agreement.NoteTooLong;
        }

        PriceListId previousList = PriceListId;
        decimal previousDiscount = DiscountPercent;

        PriceListId = priceListId;
        DiscountPercent = discountPercent;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

        // Only when something a customer would notice actually moved. An agreement re-saved with
        // the same figures is somebody tidying a note, and a price-change alert for that is how
        // people learn to ignore price-change alerts.
        if (previousList != priceListId || previousDiscount != discountPercent)
        {
            Raise(new CustomerPricingRenegotiatedDomainEvent(
                Id, CustomerId, previousList, priceListId, previousDiscount, discountPercent));
        }

        return Result.Success();
    }

    /// <summary>Ends the agreement today, sending the customer back to the default list.</summary>
    /// <param name="on">The last day it applies.</param>
    public Result End(DateOnly on)
    {
        if (EffectiveTo is { } already && already <= on)
        {
            return PricingErrors.Agreement.AlreadyEnded;
        }

        if (EffectiveFrom is { } from && on < from)
        {
            return PricingErrors.Agreement.EndBeforeStart;
        }

        EffectiveTo = on;
        Raise(new CustomerPricingEndedDomainEvent(Id, CustomerId, on));

        return Result.Success();
    }

    /// <summary>True when the given day falls inside the agreed period.</summary>
    /// <param name="on">The day being priced for.</param>
    public bool IsEffectiveOn(DateOnly on) =>
        (EffectiveFrom is null || on >= EffectiveFrom.Value)
        && (EffectiveTo is null || on <= EffectiveTo.Value);
}
