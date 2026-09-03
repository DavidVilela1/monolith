using AutoPartsErp.ModuleContracts.Catalog;
using AutoPartsErp.Modules.Purchasing.Application.Abstractions;
using AutoPartsErp.Modules.Purchasing.Application.Contracts;
using AutoPartsErp.Modules.Purchasing.Domain;
using AutoPartsErp.Modules.Purchasing.Domain.Orders;
using AutoPartsErp.Modules.Purchasing.Domain.Replenishment;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Paging;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Purchasing.Application.Replenishment;

/// <summary>Lists parts that have run low and probably need buying.</summary>
/// <param name="WarehouseId">Restrict to one warehouse.</param>
/// <param name="PartId">Restrict to one part.</param>
/// <param name="Status">Open, Ordered or Dismissed. Defaults to Open.</param>
/// <param name="Page">One-based page number.</param>
/// <param name="PageSize">Rows per page.</param>
public sealed record ListReplenishmentSuggestionsQuery(
    Guid? WarehouseId = null,
    Guid? PartId = null,
    string? Status = null,
    int Page = 1,
    int PageSize = PageRequest.DefaultPageSize) : IQuery<PagedResult<ReplenishmentSuggestionDto>>;

/// <summary>Serves <see cref="ListReplenishmentSuggestionsQuery"/> from the read store.</summary>
public sealed class ListReplenishmentSuggestionsQueryHandler
    : IQueryHandler<ListReplenishmentSuggestionsQuery, PagedResult<ReplenishmentSuggestionDto>>
{
    private readonly IPurchasingReadStore _readStore;

    /// <summary>Initializes the handler.</summary>
    public ListReplenishmentSuggestionsQueryHandler(IPurchasingReadStore readStore)
    {
        _readStore = readStore;
    }

    /// <inheritdoc />
    public async Task<Result<PagedResult<ReplenishmentSuggestionDto>>> HandleAsync(
        ListReplenishmentSuggestionsQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var criteria = new SuggestionSearchCriteria
        {
            WarehouseId = request.WarehouseId,
            PartId = request.PartId,
            Status = request.Status,
        };

        PagedResult<ReplenishmentSuggestionDto> page = await _readStore
            .ListSuggestionsAsync(criteria, PageRequest.Of(request.Page, request.PageSize), cancellationToken)
            .ConfigureAwait(false);

        return page;
    }
}

/// <summary>Takes a suggestion off the buyer's list without buying anything.</summary>
/// <param name="SuggestionId">The suggestion.</param>
/// <param name="Reason">Why, so the next person does not raise it again.</param>
public sealed record DismissReplenishmentSuggestionCommand(
    Guid SuggestionId,
    string Reason) : ICommand;

/// <summary>Dismisses the suggestion.</summary>
public sealed class DismissReplenishmentSuggestionCommandHandler
    : ICommandHandler<DismissReplenishmentSuggestionCommand>
{
    private readonly IReplenishmentSuggestionRepository _suggestions;
    private readonly IPurchasingUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public DismissReplenishmentSuggestionCommandHandler(
        IReplenishmentSuggestionRepository suggestions,
        IPurchasingUnitOfWork unitOfWork)
    {
        _suggestions = suggestions;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        DismissReplenishmentSuggestionCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        ReplenishmentSuggestion? suggestion = await _suggestions
            .GetByIdAsync(new SuggestionId(request.SuggestionId), cancellationToken)
            .ConfigureAwait(false);

        if (suggestion is null)
        {
            return PurchasingErrors.Suggestion.NotFound(request.SuggestionId.ToString());
        }

        Result dismissed = suggestion.Dismiss(request.Reason);
        if (dismissed.IsFailure)
        {
            return dismissed;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>
/// Turns a suggestion into a line on an existing draft order.
/// <para>
/// One suggestion at a time, onto an order the buyer has already opened, rather than a command
/// that creates orders by itself. That is what lets six suggestions for the same supplier become
/// one order with six lines — which is the whole point of a suggestion list, and the reason a
/// system that raised a purchase order per reorder signal would be worse than useless.
/// </para>
/// <para>
/// A suggestion carries a part id, a warehouse and a number, and nothing else. The SKU, the
/// description and the unit come from the catalogue, exactly as they do on the hand-raised
/// path — two ways onto the same document that describe a part differently is how the same
/// part ends up on one order as "Brake pad set" and on the next as "brk pads frt".
/// </para>
/// <para>
/// The price still comes from the caller. Pricing is its own module, and until it exists
/// somebody has to type what the supplier charges.
/// </para>
/// </summary>
/// <param name="SuggestionId">The suggestion to act on.</param>
/// <param name="PurchaseOrderId">The draft order to add it to.</param>
/// <param name="UnitPrice">The agreed price per unit, in the order's currency.</param>
/// <param name="Quantity">How much to order, when the buyer wants something other than the suggestion.</param>
public sealed record AddSuggestionToPurchaseOrderCommand(
    Guid SuggestionId,
    Guid PurchaseOrderId,
    decimal UnitPrice,
    decimal? Quantity = null) : ICommand<Guid>;

/// <summary>Adds the suggested part to the order and marks the suggestion as dealt with.</summary>
public sealed class AddSuggestionToPurchaseOrderCommandHandler
    : ICommandHandler<AddSuggestionToPurchaseOrderCommand, Guid>
{
    private readonly IReplenishmentSuggestionRepository _suggestions;
    private readonly IPurchaseOrderRepository _orders;
    private readonly ICatalogDirectory _catalogue;
    private readonly IPurchasingUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public AddSuggestionToPurchaseOrderCommandHandler(
        IReplenishmentSuggestionRepository suggestions,
        IPurchaseOrderRepository orders,
        ICatalogDirectory catalogue,
        IPurchasingUnitOfWork unitOfWork)
    {
        _suggestions = suggestions;
        _orders = orders;
        _catalogue = catalogue;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result<Guid>> HandleAsync(
        AddSuggestionToPurchaseOrderCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        ReplenishmentSuggestion? suggestion = await _suggestions
            .GetByIdAsync(new SuggestionId(request.SuggestionId), cancellationToken)
            .ConfigureAwait(false);

        if (suggestion is null)
        {
            return Result.Failure<Guid>(
                PurchasingErrors.Suggestion.NotFound(request.SuggestionId.ToString()));
        }

        PurchaseOrder? order = await _orders
            .GetByIdAsync(new PurchaseOrderId(request.PurchaseOrderId), cancellationToken)
            .ConfigureAwait(false);

        if (order is null)
        {
            return Result.Failure<Guid>(
                PurchasingErrors.Order.NotFound(request.PurchaseOrderId.ToString()));
        }

        if (!order.IsEditable)
        {
            return Result.Failure<Guid>(PurchasingErrors.Order.NotEditable);
        }

        PartDescriptor? part = await _catalogue
            .GetAsync(suggestion.PartId.Value, cancellationToken)
            .ConfigureAwait(false);

        if (part is null)
        {
            return Result.Failure<Guid>(
                PurchasingErrors.Line.PartNotInCatalogue(suggestion.PartId.Value.ToString()));
        }

        // A suggestion raised weeks ago against a part since withdrawn from purchasing is exactly
        // the case this catches. The reorder point fired on real stock movement; whether the
        // company still wants to carry the part is a separate decision, and the catalogue holds it.
        if (!part.IsPurchasable)
        {
            return Result.Failure<Guid>(
                PurchasingErrors.Line.PartNotPurchasable(part.Sku, part.SupersededByPartId));
        }

        Result<Quantity> quantity = Quantity.Create(
            request.Quantity ?? suggestion.SuggestedQuantity,
            UnitOfMeasure.FromCode(part.StockUnitCode));

        if (quantity.IsFailure)
        {
            return Result.Failure<Guid>(quantity.Error);
        }

        Result<PurchaseOrderLineId> line = order.AddLine(
            suggestion.PartId,
            part.Sku,
            part.Name,
            quantity.Value,
            Money.Of(request.UnitPrice, order.Currency));

        if (line.IsFailure)
        {
            return Result.Failure<Guid>(line.Error);
        }

        Result ordered = suggestion.MarkOrdered(order.Id);
        if (ordered.IsFailure)
        {
            return Result.Failure<Guid>(ordered.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return line.Value.Value;
    }
}
