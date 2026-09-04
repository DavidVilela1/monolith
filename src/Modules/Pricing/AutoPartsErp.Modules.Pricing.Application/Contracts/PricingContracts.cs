namespace AutoPartsErp.Modules.Pricing.Application.Contracts;

/// <summary>A price list, as a list of them is rendered.</summary>
/// <param name="Id">The list.</param>
/// <param name="Code">Its code.</param>
/// <param name="Name">What it is called.</param>
/// <param name="CurrencyCode">The currency its prices are in.</param>
/// <param name="Kind">Standard, customer or promotion.</param>
/// <param name="Status">Draft, active or archived.</param>
/// <param name="EffectiveFrom">The first day it applies, or null for always.</param>
/// <param name="EffectiveTo">The last day it applies, or null for never expiring.</param>
/// <param name="IsDefault">True for the list customers with no agreement fall back to.</param>
/// <param name="PricedParts">How many parts it prices.</param>
public sealed record PriceListSummary(
    Guid Id,
    string Code,
    string Name,
    string CurrencyCode,
    string Kind,
    string Status,
    DateOnly? EffectiveFrom,
    DateOnly? EffectiveTo,
    bool IsDefault,
    int PricedParts);

/// <summary>What one part costs in one list.</summary>
/// <param name="EntryId">The entry.</param>
/// <param name="PartId">The part.</param>
/// <param name="Breaks">Its quantity breaks, smallest first.</param>
public sealed record PriceListEntryDto(
    Guid EntryId,
    Guid PartId,
    IReadOnlyList<PriceBreakDto> Breaks);

/// <summary>One quantity break.</summary>
/// <param name="MinimumQuantity">The quantity the price applies from.</param>
/// <param name="UnitPrice">What one unit costs from there upwards.</param>
public sealed record PriceBreakDto(decimal MinimumQuantity, decimal UnitPrice);

/// <summary>What was agreed with one customer.</summary>
/// <param name="Id">The agreement.</param>
/// <param name="CustomerId">The customer.</param>
/// <param name="PriceListId">The list they buy from.</param>
/// <param name="PriceListCode">Its code, so a screen need not look it up.</param>
/// <param name="DiscountPercent">What comes off it.</param>
/// <param name="EffectiveFrom">The first day it applies, or null for always.</param>
/// <param name="EffectiveTo">The last day it applies, or null for never expiring.</param>
/// <param name="Note">Why it exists.</param>
public sealed record CustomerPricingDto(
    Guid Id,
    Guid CustomerId,
    Guid PriceListId,
    string PriceListCode,
    decimal DiscountPercent,
    DateOnly? EffectiveFrom,
    DateOnly? EffectiveTo,
    string? Note);

/// <summary>
/// The answer to "what does this cost?", flattened for the wire.
/// <para>
/// Carries the reasoning as well as the number. The counter needs the number; the argument three
/// weeks later needs to know which list it came from and which break applied, and reconstructing
/// that afterwards means re-running rules that may have moved since.
/// </para>
/// </summary>
/// <param name="PartId">The part.</param>
/// <param name="Quantity">The quantity it was priced at.</param>
/// <param name="CurrencyCode">The currency.</param>
/// <param name="GrossUnitPrice">The list price before the customer's discount.</param>
/// <param name="DiscountPercent">What their agreement takes off.</param>
/// <param name="NetUnitPrice">What they actually pay per unit.</param>
/// <param name="LineTotal">Net unit price times quantity, before VAT.</param>
/// <param name="PriceListId">The list the price came from.</param>
/// <param name="PriceListCode">Its code.</param>
/// <param name="AppliedBreakQuantity">The quantity the applied break starts at.</param>
public sealed record PriceQuoteDto(
    Guid PartId,
    decimal Quantity,
    string CurrencyCode,
    decimal GrossUnitPrice,
    decimal DiscountPercent,
    decimal NetUnitPrice,
    decimal LineTotal,
    Guid PriceListId,
    string PriceListCode,
    decimal AppliedBreakQuantity);

/// <summary>What to look for when listing price lists.</summary>
public sealed record PriceListSearchCriteria
{
    /// <summary>Free text, matched against code and name.</summary>
    public string? Term { get; init; }

    /// <summary>Restrict to one kind: Standard, Customer or Promotion.</summary>
    public string? Kind { get; init; }

    /// <summary>Restrict to one status: Draft, Active or Archived.</summary>
    public string? Status { get; init; }

    /// <summary>True to return only lists that apply today — the working view.</summary>
    public bool EffectiveOnly { get; init; }
}
