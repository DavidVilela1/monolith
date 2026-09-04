using AutoPartsErp.Modules.Pricing.Domain.PriceLists.Events;
using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Pricing.Domain.PriceLists;

/// <summary>
/// A named set of prices, in one currency, that applies over a period.
/// <para>
/// The list itself holds only what identifies it and when it applies. The prices live in
/// <see cref="PriceListEntry"/>, which is its own aggregate root rather than a child collection
/// here — a deliberate departure from the rule the rest of this system follows.
/// </para>
/// <para>
/// The reason is size. A parts distributor's standard list is tens of thousands of parts, and
/// making the entries children would mean loading all of them to correct one price. The cost of
/// the departure is that nothing stops an entry from outliving its list, so
/// <see cref="Archive"/> is the only way to withdraw a list and the resolver checks the list's
/// state before it will quote from an entry. That check is the boundary, in place of the object
/// graph.
/// </para>
/// </summary>
public sealed class PriceList : AggregateRoot<PriceListId>, IAuditable, ISoftDeletable, ITenantScoped
{
    /// <summary>Longest permitted list code.</summary>
    public const int MaxCodeLength = 30;

    /// <summary>Longest permitted list name.</summary>
    public const int MaxNameLength = 120;

    private PriceList(
        PriceListId id,
        string code,
        string name,
        Currency currency,
        PriceListKind kind,
        DateOnly? effectiveFrom,
        DateOnly? effectiveTo)
        : base(id)
    {
        Code = code;
        Name = name;
        CurrencyCode = currency.Code;
        Kind = kind;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        Status = PriceListStatus.Draft;
        CreatedBy = string.Empty;
    }

    /// <summary>Required by EF Core materialization.</summary>
#pragma warning disable CS8618
    private PriceList()
    {
    }
#pragma warning restore CS8618

    /// <summary>The code buyers and counter staff refer to the list by. Unique within a tenant.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>What the list is called.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// The currency every price in the list is expressed in.
    /// <para>
    /// Fixed for the life of the list. Changing it would silently reinterpret every price already
    /// in it, which is the kind of edit that turns €20.00 into $20.00 and nobody notices for a
    /// month. A list in another currency is another list.
    /// </para>
    /// </summary>
    public string CurrencyCode { get; private set; } = Currency.Default.Code;

    /// <summary>What the list is for, and therefore how it ranks against the others.</summary>
    public PriceListKind Kind { get; private set; }

    /// <summary>Where the list is in its life.</summary>
    public PriceListStatus Status { get; private set; }

    /// <summary>The first day the list applies. Null means it has always applied.</summary>
    public DateOnly? EffectiveFrom { get; private set; }

    /// <summary>The last day the list applies, inclusive. Null means it does not expire.</summary>
    public DateOnly? EffectiveTo { get; private set; }

    /// <summary>
    /// True for the one list a customer with no agreement falls back to.
    /// <para>
    /// At most one per tenant. Enforced in the database with a filtered unique index rather than
    /// here, because "only one" is a statement about every row in the table and an aggregate can
    /// only ever see itself.
    /// </para>
    /// </summary>
    public bool IsDefault { get; private set; }

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

    /// <summary>The currency the list is priced in.</summary>
    public Currency Currency => Currency.FromCode(CurrencyCode);

    /// <summary>True while prices can still be added to and removed from the list.</summary>
    public bool IsEditable => Status is PriceListStatus.Draft or PriceListStatus.Active;

    /// <summary>
    /// How strongly this list wins when more than one could answer the same question.
    /// <para>
    /// Data rather than a rule scattered across the resolver, and the same reason Catalog exposes
    /// its status lists: the resolver has to sort by it, and a sort cannot call a method that
    /// switches on an enum in three different places.
    /// </para>
    /// </summary>
    public int Precedence => Kind switch
    {
        PriceListKind.Promotion => 30,
        PriceListKind.Customer => 20,
        PriceListKind.Standard => 10,
        _ => 0,
    };

    /// <summary>Opens a new list, in draft.</summary>
    /// <param name="code">The code it will be referred to by.</param>
    /// <param name="name">What it is called.</param>
    /// <param name="currency">The currency every price in it is expressed in.</param>
    /// <param name="kind">What it is for.</param>
    /// <param name="effectiveFrom">The first day it applies, or null for always.</param>
    /// <param name="effectiveTo">The last day it applies, or null for never expiring.</param>
    public static Result<PriceList> Open(
        string? code,
        string? name,
        Currency currency,
        PriceListKind kind,
        DateOnly? effectiveFrom = null,
        DateOnly? effectiveTo = null)
    {
        ArgumentNullException.ThrowIfNull(currency);

        if (string.IsNullOrWhiteSpace(code))
        {
            return PricingErrors.List.CodeRequired;
        }

        if (code.Trim().Length > MaxCodeLength)
        {
            return PricingErrors.List.CodeTooLong;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return PricingErrors.List.NameRequired;
        }

        if (name.Trim().Length > MaxNameLength)
        {
            return PricingErrors.List.NameTooLong;
        }

        if (kind == PriceListKind.Unknown)
        {
            return PricingErrors.List.KindRequired;
        }

        if (effectiveFrom is { } from && effectiveTo is { } to && to < from)
        {
            return PricingErrors.List.PeriodInverted;
        }

        // A promotion that never ends is not a promotion, it is a price change dressed up as one.
        // Making this a rule rather than a convention is what stops "February sale" still being
        // live in November because nobody remembered it existed.
        if (kind == PriceListKind.Promotion && effectiveTo is null)
        {
            return PricingErrors.List.PromotionNeedsEndDate;
        }

        var list = new PriceList(
            PriceListId.New(),
            code.Trim().ToUpperInvariant(),
            name.Trim(),
            currency,
            kind,
            effectiveFrom,
            effectiveTo);

        list.Raise(new PriceListOpenedDomainEvent(list.Id, list.Code, list.Kind, list.CurrencyCode));

        return list;
    }

    /// <summary>Renames the list, or moves the period it applies over.</summary>
    /// <param name="name">The new name.</param>
    /// <param name="effectiveFrom">The new first day, or null for always.</param>
    /// <param name="effectiveTo">The new last day, or null for never expiring.</param>
    public Result Amend(string? name, DateOnly? effectiveFrom, DateOnly? effectiveTo)
    {
        if (Status == PriceListStatus.Archived)
        {
            return PricingErrors.List.Archived;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return PricingErrors.List.NameRequired;
        }

        if (name.Trim().Length > MaxNameLength)
        {
            return PricingErrors.List.NameTooLong;
        }

        if (effectiveFrom is { } from && effectiveTo is { } to && to < from)
        {
            return PricingErrors.List.PeriodInverted;
        }

        if (Kind == PriceListKind.Promotion && effectiveTo is null)
        {
            return PricingErrors.List.PromotionNeedsEndDate;
        }

        Name = name.Trim();
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;

        return Result.Success();
    }

    /// <summary>Puts the list into service, so quotes start coming from it.</summary>
    /// <param name="hasAnyPrice">
    /// Whether the list has at least one entry. Passed in rather than read, because the entries
    /// are not part of this aggregate — see the note on the class.
    /// </param>
    public Result Activate(bool hasAnyPrice)
    {
        if (Status == PriceListStatus.Active)
        {
            return PricingErrors.List.AlreadyActive;
        }

        if (Status == PriceListStatus.Archived)
        {
            return PricingErrors.List.Archived;
        }

        if (!hasAnyPrice)
        {
            return PricingErrors.List.NoPrices;
        }

        Status = PriceListStatus.Active;
        Raise(new PriceListActivatedDomainEvent(Id, Code));

        return Result.Success();
    }

    /// <summary>Withdraws the list. Quotes stop coming from it; documents that used it still explain themselves.</summary>
    public Result Archive()
    {
        if (Status == PriceListStatus.Archived)
        {
            return PricingErrors.List.Archived;
        }

        if (IsDefault)
        {
            return PricingErrors.List.CannotArchiveDefault;
        }

        Status = PriceListStatus.Archived;
        Raise(new PriceListArchivedDomainEvent(Id, Code));

        return Result.Success();
    }

    /// <summary>
    /// Makes this the list a customer with no agreement falls back to.
    /// <para>
    /// The caller is responsible for clearing the flag on the previous default in the same
    /// transaction. That is not an aggregate's job to police — "exactly one" spans rows.
    /// </para>
    /// </summary>
    public Result MakeDefault()
    {
        if (Status != PriceListStatus.Active)
        {
            return PricingErrors.List.DefaultMustBeActive;
        }

        if (Kind != PriceListKind.Standard)
        {
            return PricingErrors.List.DefaultMustBeStandard;
        }

        if (EffectiveTo is not null)
        {
            return PricingErrors.List.DefaultCannotExpire;
        }

        if (IsDefault)
        {
            return Result.Success();
        }

        IsDefault = true;
        Raise(new PriceListMadeDefaultDomainEvent(Id, Code));

        return Result.Success();
    }

    /// <summary>Clears the default flag, so another list can take it.</summary>
    public void ClearDefault() => IsDefault = false;

    /// <summary>True when the list is live and the given day falls inside its period.</summary>
    /// <param name="on">The day being priced for.</param>
    public bool IsEffectiveOn(DateOnly on) =>
        Status == PriceListStatus.Active
        && (EffectiveFrom is null || on >= EffectiveFrom.Value)
        && (EffectiveTo is null || on <= EffectiveTo.Value);
}
