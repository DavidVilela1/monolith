using System.Globalization;
using AutoPartsErp.IntegrationEvents.Inventory;
using AutoPartsErp.Modules.Finance.Application.Ledger;
using AutoPartsErp.Modules.Finance.Domain.Ledger;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Finance.Application.EventHandlers;

/// <summary>
/// Posts the cost of a sale when stock leaves the shelf against a customer's document.
/// <para>
/// <b>Not every issue is a cost of sale.</b> Stock leaves for a sale, and it also leaves for a
/// transfer to the other branch — and a transfer is two shelves the company still owns, not an
/// expense. Posting one would book a cost of sale every time a van went across town and leave the
/// year's margin looking like a business that sells to itself. The reference type is what tells
/// them apart, which is why the event carries it.
/// </para>
/// <para>
/// The value is what the moving average said the goods had cost, worked out by Inventory when
/// they left. This module does not recompute it: the figure that came off the shelf and the
/// figure that reaches the ledger have to be the same one, to the cent.
/// </para>
/// </summary>
public sealed class PostCostOfSaleOnStockIssued
    : IIntegrationEventHandler<StockIssuedIntegrationEvent>
{
    private readonly ILedgerPoster _poster;

    /// <summary>Initializes the handler.</summary>
    public PostCostOfSaleOnStockIssued(ILedgerPoster poster)
    {
        _poster = poster;
    }

    /// <inheritdoc />
    public async Task HandleAsync(
        StockIssuedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        if (!StockFacts.IsSaleToACustomer(integrationEvent.ReferenceType))
        {
            return;
        }

        // A shelf carrying no value has nothing to post. It happens on a part received before
        // anybody knew what it cost, and a zero entry would say the company gave it away.
        if (integrationEvent.CostValue is not decimal cost || cost == 0m)
        {
            return;
        }

        await _poster.PostFactAsync(
            PostingFacts.CostOfSale,
            StockFacts.Reference(integrationEvent.Reference, integrationEvent.MovementId),
            StockFacts.DayOf(integrationEvent.OccurredAtUtc),
            $"Cost of sale on {integrationEvent.Reference}",
            StockFacts.Value(cost, integrationEvent.CurrencyCode),
            cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Takes the cost of a sale back off when a customer returns the goods.
/// <para>
/// The mirror of the issue, and it posts the same fact with a negative value rather than a fact of
/// its own — which is exactly what a rule's side-flipping exists for. A company that posted the
/// cost of every sale and never reversed one would show a margin that falls a little with every
/// return and never recovers.
/// </para>
/// <para>
/// The value is what the goods cost when they <i>left</i>, not today's average. Inventory already
/// insists on that, and it is what makes the two postings cancel to zero.
/// </para>
/// </summary>
public sealed class ReverseCostOfSaleOnCustomerReturn
    : IIntegrationEventHandler<StockReceivedIntegrationEvent>
{
    private readonly ILedgerPoster _poster;

    /// <summary>Initializes the handler.</summary>
    public ReverseCostOfSaleOnCustomerReturn(ILedgerPoster poster)
    {
        _poster = poster;
    }

    /// <inheritdoc />
    public async Task HandleAsync(
        StockReceivedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        // Goods arriving from a supplier are a purchase, and the supplier's invoice posts that.
        // Only a customer's return undoes a cost of sale.
        if (!string.Equals(
                integrationEvent.ReferenceType, "CustomerReturn", StringComparison.Ordinal))
        {
            return;
        }

        if (integrationEvent.Value is not decimal value || value == 0m)
        {
            return;
        }

        await _poster.PostFactAsync(
            PostingFacts.CostOfSale,
            StockFacts.Reference(integrationEvent.Reference, integrationEvent.MovementId),
            StockFacts.DayOf(integrationEvent.OccurredAtUtc),
            $"Cost of sale reversed on {integrationEvent.Reference}",
            StockFacts.Value(-value, integrationEvent.CurrencyCode),
            cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Posts what a count found, in money.
/// <para>
/// A count that found less is stock the company thought it had and does not; one that found more
/// is the opposite. The value is signed the way the quantity is, so one rule covers both — the
/// side flips on a negative and a shortfall lands where a windfall does not.
/// </para>
/// </summary>
public sealed class PostAdjustmentOnStockAdjusted
    : IIntegrationEventHandler<StockAdjustedIntegrationEvent>
{
    private readonly ILedgerPoster _poster;

    /// <summary>Initializes the handler.</summary>
    public PostAdjustmentOnStockAdjusted(ILedgerPoster poster)
    {
        _poster = poster;
    }

    /// <inheritdoc />
    public async Task HandleAsync(
        StockAdjustedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        if (integrationEvent.Value is not decimal value || value == 0m)
        {
            return;
        }

        await _poster.PostFactAsync(
            PostingFacts.StockAdjustment,
            StockFacts.Reference(integrationEvent.Reference, integrationEvent.MovementId),
            StockFacts.DayOf(integrationEvent.OccurredAtUtc),
            $"Stock adjustment on {integrationEvent.Reference}",
            StockFacts.Value(value, integrationEvent.CurrencyCode),
            cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Posts a price variance: the shelf is worth more or less than it was, with nothing having moved.
/// <para>
/// The fact that had nowhere to go for longest. A supplier invoiced a delivery at a price the
/// receipt was not booked at; Inventory corrects what is still on the shelf, and the difference
/// on the part already sold belongs in the accounts and had no account to land on.
/// </para>
/// </summary>
public sealed class PostVarianceOnStockRevalued
    : IIntegrationEventHandler<StockRevaluedIntegrationEvent>
{
    private readonly ILedgerPoster _poster;

    /// <summary>Initializes the handler.</summary>
    public PostVarianceOnStockRevalued(ILedgerPoster poster)
    {
        _poster = poster;
    }

    /// <inheritdoc />
    public async Task HandleAsync(
        StockRevaluedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        if (integrationEvent.Difference == 0m)
        {
            return;
        }

        await _poster.PostFactAsync(
            PostingFacts.PriceVariance,
            StockFacts.Reference(integrationEvent.Reference, integrationEvent.MovementId),
            StockFacts.DayOf(integrationEvent.OccurredAtUtc),
            $"Supplier price variance on {integrationEvent.Reference}",
            StockFacts.Value(integrationEvent.Difference, integrationEvent.CurrencyCode),
            cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Posts the shrinkage when a transfer arrives short.
/// <para>
/// Goods that left one warehouse and never reached the other are gone: the company owned them at
/// breakfast and does not own them at lunch. Until now that carried its value in an event nobody
/// consumed.
/// </para>
/// </summary>
public sealed class PostShrinkageOnTransferClosedShort
    : IIntegrationEventHandler<StockTransferClosedShortIntegrationEvent>
{
    private readonly ILedgerPoster _poster;

    /// <summary>Initializes the handler.</summary>
    public PostShrinkageOnTransferClosedShort(ILedgerPoster poster)
    {
        _poster = poster;
    }

    /// <inheritdoc />
    public async Task HandleAsync(
        StockTransferClosedShortIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        if (integrationEvent.LostValue == 0m)
        {
            return;
        }

        // One transfer, one shrinkage, so the number alone is the reference: unlike the per-part
        // facts above, this is already about the whole document.
        await _poster.PostFactAsync(
            PostingFacts.StockShrinkage,
            integrationEvent.Number,
            StockFacts.DayOf(integrationEvent.OccurredAtUtc),
            $"Transfer {integrationEvent.Number} closed short: {integrationEvent.Reason}",
            StockFacts.Value(integrationEvent.LostValue, integrationEvent.CurrencyCode),
            cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// The small decisions every stock posting shares, written once.
/// </summary>
internal static class StockFacts
{
    /// <summary>
    /// True when stock leaving the shelf against this kind of document is a cost of sale.
    /// <para>
    /// A sales order and a counter sale are; a stock transfer is a move between two shelves the
    /// company still owns, and a supplier return is a purchase going back rather than a sale.
    /// </para>
    /// </summary>
    /// <param name="referenceType">The kind of document, as Inventory names it.</param>
    internal static bool IsSaleToACustomer(string? referenceType) =>
        string.Equals(referenceType, "SalesOrder", StringComparison.Ordinal)
        || string.Equals(referenceType, "CounterSale", StringComparison.Ordinal);

    /// <summary>
    /// The key for one movement on one document.
    /// <para>
    /// The document number alone will not do. A delivery note covers six parts and each is its own
    /// fact; worse, one order dispatched in two vans moves the same part twice. Either would have
    /// the second fact look like the first arriving again, and the record of facts would swallow
    /// it. The movement is the identity Inventory itself gives the event.
    /// </para>
    /// </summary>
    /// <param name="reference">The document number.</param>
    /// <param name="movementId">The movement in Inventory's own ledger.</param>
    internal static string Reference(string reference, Guid movementId) =>
        string.Create(CultureInfo.InvariantCulture, $"{reference}/{movementId:N}");

    /// <summary>
    /// The day a fact belongs to.
    /// <para>
    /// Stock movements carry an instant rather than a date, because a warehouse works to the
    /// minute. A ledger works to the day, and the day is the one the movement happened on.
    /// </para>
    /// </summary>
    /// <param name="occurredAtUtc">When it happened.</param>
    internal static DateOnly DayOf(DateTimeOffset occurredAtUtc) =>
        DateOnly.FromDateTime(occurredAtUtc.UtcDateTime);

    /// <summary>One amount under the key the stock facts carry it by.</summary>
    /// <param name="amount">How much.</param>
    /// <param name="currencyCode">The currency.</param>
    internal static IReadOnlyDictionary<string, Money> Value(
        decimal amount,
        string currencyCode) =>
        new Dictionary<string, Money>(StringComparer.Ordinal)
        {
            [PostingFacts.Value] = Money.Of(amount, Currency.FromCode(currencyCode)),
        };
}
