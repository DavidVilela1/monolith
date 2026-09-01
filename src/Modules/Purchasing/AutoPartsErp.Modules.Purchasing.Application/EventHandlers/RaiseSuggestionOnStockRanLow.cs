using AutoPartsErp.IntegrationEvents.Catalog;
using AutoPartsErp.IntegrationEvents.Inventory;
using AutoPartsErp.Modules.Purchasing.Domain;
using AutoPartsErp.Modules.Purchasing.Domain.Replenishment;
using AutoPartsErp.SharedKernel.Abstractions;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Purchasing.Application.EventHandlers;

/// <summary>
/// Turns Inventory's "this has run low" signal into something a buyer can act on.
/// <para>
/// Until this handler existed, <c>StockFellBelowReorderPointIntegrationEvent</c> was published to
/// nobody: Inventory did the right thing at the boundary and then the fact fell on the floor.
/// This is the other half of that contract, and it is worth noticing how little it needs to
/// know. Purchasing does not reference the Inventory module, does not read the inventory schema,
/// and cannot see a StockItem. It receives six values and writes a row in its own schema.
/// </para>
/// <para>
/// Idempotent by construction, which it has to be. Stock crosses the reorder point every time
/// somebody picks the last few off the shelf, and an at-least-once bus will redeliver on retry.
/// An existing open suggestion is refreshed rather than duplicated, so the buyer's list stays a
/// list of parts rather than a list of events.
/// </para>
/// </summary>
public sealed class RaiseSuggestionOnStockRanLow
    : IIntegrationEventHandler<StockFellBelowReorderPointIntegrationEvent>
{
    private readonly IReplenishmentSuggestionRepository _suggestions;
    private readonly IPurchasingUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    /// <summary>Initializes the handler.</summary>
    public RaiseSuggestionOnStockRanLow(
        IReplenishmentSuggestionRepository suggestions,
        IPurchasingUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _suggestions = suggestions;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// The signal carried a quantity this module cannot turn into a suggestion — a reorder
    /// quantity of zero or less, usually meaning the part is set up with a reorder point but no
    /// reorder quantity. Failing loudly is better than silently dropping the only prompt anybody
    /// will get that a part is about to run out.
    /// </exception>
    public async Task HandleAsync(
        StockFellBelowReorderPointIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        var part = new PartRef(integrationEvent.PartId);
        var warehouse = new WarehouseRef(integrationEvent.WarehouseId);

        ReplenishmentSuggestion? existing = await _suggestions
            .GetOpenForAsync(part, warehouse, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            Result refreshed = existing.Refresh(
                integrationEvent.QuantityAvailable,
                integrationEvent.ReorderPoint,
                integrationEvent.ReorderQuantity,
                _clock.UtcNow);

            if (refreshed.IsFailure)
            {
                throw new InvalidOperationException(
                    $"Could not refresh the replenishment suggestion for part " +
                    $"{integrationEvent.PartId} in warehouse {integrationEvent.WarehouseId}: " +
                    $"{refreshed.Error}");
            }
        }
        else
        {
            Result<ReplenishmentSuggestion> suggestion = ReplenishmentSuggestion.Open(
                part,
                warehouse,
                integrationEvent.QuantityAvailable,
                integrationEvent.ReorderPoint,
                integrationEvent.ReorderQuantity,
                _clock.UtcNow);

            if (suggestion.IsFailure)
            {
                throw new InvalidOperationException(
                    $"Could not raise a replenishment suggestion for part " +
                    $"{integrationEvent.PartId} in warehouse {integrationEvent.WarehouseId}: " +
                    $"{suggestion.Error}");
            }

            _suggestions.Add(suggestion.Value);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Stops suggesting a part that Catalog has withdrawn.
/// <para>
/// A discontinued part is one the brand has stopped making. Whatever is on the shelf gets sold
/// down; nobody should be reordering it, and a suggestion that keeps reappearing after the buyer
/// dismissed it is exactly how people learn to ignore the whole list.
/// </para>
/// </summary>
public sealed class DismissSuggestionsOnPartDiscontinued
    : IIntegrationEventHandler<PartDiscontinuedIntegrationEvent>
{
    private const string DiscontinuedReason =
        "The part was discontinued in the catalogue, so there is nothing left to reorder.";

    private readonly IReplenishmentSuggestionRepository _suggestions;
    private readonly IPurchasingUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public DismissSuggestionsOnPartDiscontinued(
        IReplenishmentSuggestionRepository suggestions,
        IPurchasingUnitOfWork unitOfWork)
    {
        _suggestions = suggestions;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task HandleAsync(
        PartDiscontinuedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        IReadOnlyList<ReplenishmentSuggestion> open = await _suggestions
            .GetOpenForPartAsync(new PartRef(integrationEvent.PartId), cancellationToken)
            .ConfigureAwait(false);

        if (open.Count == 0)
        {
            return;
        }

        foreach (ReplenishmentSuggestion suggestion in open)
        {
            // The result is deliberately discarded: a suggestion that was ordered or dismissed
            // between the query and here refuses, and that refusal is the correct outcome, not
            // a failure worth propagating out of an event handler.
            _ = suggestion.Dismiss(DiscontinuedReason);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
