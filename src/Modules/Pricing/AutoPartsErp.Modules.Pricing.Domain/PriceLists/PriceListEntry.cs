using AutoPartsErp.Modules.Pricing.Domain.PriceLists.Events;
using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Pricing.Domain.PriceLists;

/// <summary>
/// What one part costs in one list, at every quantity that matters.
/// <para>
/// Its own aggregate root rather than a child of <see cref="PriceList"/>, because a standard list
/// runs to tens of thousands of parts and correcting one price should not mean loading all of
/// them. See the note on <see cref="PriceList"/> for what that costs.
/// </para>
/// <para>
/// The breaks are the aggregate's own collection and are kept sorted, with no gaps possible: a
/// break is a floor, and the list of floors sorted ascending is a complete description of the
/// price at every quantity above the first one. Below the first break there is no price, and that
/// is a real answer — "we do not sell fewer than five of these" is a normal thing for a
/// distributor to say.
/// </para>
/// </summary>
public sealed class PriceListEntry
    : AggregateRoot<PriceListEntryId>, IAuditable, ISoftDeletable, ITenantScoped
{
    /// <summary>Most breaks one entry may carry. Beyond this somebody is modelling something else.</summary>
    public const int MaxBreaks = 12;

    private readonly List<PriceBreak> _breaks = [];

    private PriceListEntry(PriceListEntryId id, PriceListId priceListId, PartRef partId, PriceBreak first)
        : base(id)
    {
        PriceListId = priceListId;
        PartId = partId;
        _breaks.Add(first);
        CreatedBy = string.Empty;
    }

    /// <summary>Required by EF Core materialization.</summary>
#pragma warning disable CS8618
    private PriceListEntry()
    {
    }
#pragma warning restore CS8618

    /// <summary>The list this price belongs to.</summary>
    public PriceListId PriceListId { get; private set; }

    /// <summary>The part being priced.</summary>
    public PartRef PartId { get; private set; }

    /// <summary>
    /// The quantity breaks, smallest first.
    /// <para>
    /// Sorted on the way out rather than trusted to be sorted already. The aggregate keeps its
    /// own list ordered when a break is added, but a collection materialized from the database
    /// comes back in whatever order the rows arrived in — and every reader of this property is
    /// about to render "1+, 10+, 50+" to somebody.
    /// </para>
    /// <para>
    /// Read-only from outside. Everything that changes them goes through <see cref="SetBreak"/>
    /// and <see cref="RemoveBreak"/>, which is what keeps a duplicate minimum from existing.
    /// </para>
    /// </summary>
    public IReadOnlyList<PriceBreak> Breaks => [.. _breaks.OrderBy(item => item.MinimumQuantity)];

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

    /// <summary>
    /// The smallest quantity this entry will price at all.
    /// <para>
    /// The minimum of the breaks rather than the first of them, for the same reason
    /// <see cref="Breaks"/> sorts: the database does not promise an order, and "the first row that
    /// came back" is a quiet way to start refusing quantities that are perfectly sellable.
    /// </para>
    /// </summary>
    public decimal MinimumSaleQuantity => _breaks.Min(item => item.MinimumQuantity);

    /// <summary>
    /// The currency every break is expressed in. Any of them will do — <see cref="SetBreak"/>
    /// refuses one in another currency, so they cannot disagree.
    /// </summary>
    public Currency Currency => _breaks[0].UnitPrice.Currency;

    /// <summary>Prices a part in a list, starting from one break.</summary>
    /// <param name="priceListId">The list the price belongs to.</param>
    /// <param name="partId">The part being priced.</param>
    /// <param name="minimumQuantity">The quantity the first price applies from, usually 1.</param>
    /// <param name="unitPrice">What one unit costs from there upwards.</param>
    public static Result<PriceListEntry> Price(
        PriceListId priceListId,
        PartRef partId,
        decimal minimumQuantity,
        Money unitPrice)
    {
        ArgumentNullException.ThrowIfNull(unitPrice);

        if (priceListId.IsEmpty)
        {
            return PricingErrors.Entry.ListRequired;
        }

        if (partId.IsEmpty)
        {
            return PricingErrors.Entry.PartRequired;
        }

        Result<PriceBreak> first = PriceBreak.Create(minimumQuantity, unitPrice);

        return first.IsFailure
            ? Result.Failure<PriceListEntry>(first.Error)
            : new PriceListEntry(PriceListEntryId.New(), priceListId, partId, first.Value);
    }

    /// <summary>
    /// Sets the price from a quantity upwards, adding the break or replacing the one already
    /// standing at that quantity.
    /// <para>
    /// One method rather than an add and an update, because "10 or more is €22" is a single
    /// intention and the caller should not have to know whether that break exists yet. Trying to
    /// add one twice is not a conflict — it is somebody correcting a figure.
    /// </para>
    /// </summary>
    /// <param name="minimumQuantity">The quantity the price applies from.</param>
    /// <param name="unitPrice">What one unit costs from there upwards.</param>
    public Result SetBreak(decimal minimumQuantity, Money unitPrice)
    {
        ArgumentNullException.ThrowIfNull(unitPrice);

        if (!unitPrice.Currency.Equals(Currency))
        {
            return PricingErrors.Entry.CurrencyMismatch;
        }

        Result<PriceBreak> candidate = PriceBreak.Create(minimumQuantity, unitPrice);

        if (candidate.IsFailure)
        {
            return candidate.Error;
        }

        int existing = _breaks.FindIndex(item => item.MinimumQuantity == minimumQuantity);

        if (existing >= 0)
        {
            _breaks[existing] = candidate.Value;
        }
        else
        {
            if (_breaks.Count >= MaxBreaks)
            {
                return PricingErrors.Entry.TooManyBreaks;
            }

            _breaks.Add(candidate.Value);
            _breaks.Sort((left, right) => left.MinimumQuantity.CompareTo(right.MinimumQuantity));
        }

        Raise(new PriceChangedDomainEvent(Id, PriceListId, PartId, minimumQuantity, unitPrice.Amount));

        return Result.Success();
    }

    /// <summary>Removes the break standing at a quantity.</summary>
    /// <param name="minimumQuantity">The quantity whose break is going.</param>
    public Result RemoveBreak(decimal minimumQuantity)
    {
        int index = _breaks.FindIndex(item => item.MinimumQuantity == minimumQuantity);

        if (index < 0)
        {
            return PricingErrors.Entry.BreakNotFound(minimumQuantity);
        }

        // An entry with no breaks is an entry that cannot answer the question it exists to answer.
        // Withdrawing a part from a list is deleting the entry, not emptying it.
        if (_breaks.Count == 1)
        {
            return PricingErrors.Entry.LastBreak;
        }

        _breaks.RemoveAt(index);

        return Result.Success();
    }

    /// <summary>
    /// The price at a quantity, or null when the quantity is below the smallest break.
    /// <para>
    /// The highest break that still applies, not the first one that matches. Ordering ascending
    /// and taking the last match is the whole of quantity pricing, and getting it backwards is
    /// how a customer buying fifty gets charged the price of buying one.
    /// </para>
    /// </summary>
    /// <param name="quantity">How many are being bought.</param>
    public PriceBreak? BreakFor(decimal quantity)
    {
        PriceBreak? best = null;

        foreach (PriceBreak candidate in _breaks)
        {
            if (candidate.AppliesTo(quantity))
            {
                best = candidate;
            }
        }

        return best;
    }
}
