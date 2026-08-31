using AutoPartsErp.Modules.Partners.Domain.Partners.Events;
using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Partners.Domain.Partners;

/// <summary>
/// A company we trade with, in whichever directions we trade with them.
/// <para>
/// One aggregate with roles rather than separate Customer and Supplier types. In parts
/// distribution the same company is routinely both — you buy from a factor and sell them
/// something they are short of the same week — and modelling that as two records means two
/// addresses to keep in step, two credit conversations, and a reconciliation nobody wants when
/// they owe you and you owe them.
/// </para>
/// <para>
/// Roles are additive and reversible: granting the supplier role to an existing customer keeps
/// the trading history intact.
/// </para>
/// </summary>
public sealed class Partner : AggregateRoot<PartnerId>, IAuditable, ISoftDeletable, ITenantScoped
{
    /// <summary>Longest permitted partner code.</summary>
    public const int MaxCodeLength = 20;

    /// <summary>Longest permitted legal name.</summary>
    public const int MaxNameLength = 200;

    private readonly List<Address> _addresses = [];
    private readonly List<ContactDetail> _contacts = [];

    private Partner(PartnerId id, string code, string legalName, TaxNumber taxNumber)
        : base(id)
    {
        Code = code;
        LegalName = legalName;
        TaxNumber = taxNumber;
        Status = PartnerStatus.Active;
        Roles = PartnerRoles.None;
        CreatedBy = string.Empty;
    }

    /// <summary>Required by EF Core materialization.</summary>
#pragma warning disable CS8618
    private Partner()
    {
    }
#pragma warning restore CS8618

    /// <summary>Short code used on documents and typed at the counter.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>The registered company name, as it must appear on an invoice.</summary>
    public string LegalName { get; private set; } = string.Empty;

    /// <summary>What they are actually called, when that differs from the legal name.</summary>
    public string? TradingName { get; private set; }

    /// <summary>Their tax identification number.</summary>
    public TaxNumber TaxNumber { get; private set; } = null!;

    /// <summary>Which directions we trade with them in.</summary>
    public PartnerRoles Roles { get; private set; }

    /// <summary>Whether we are still trading with them.</summary>
    public PartnerStatus Status { get; private set; }

    /// <summary>The commercial arrangement, when they are a customer.</summary>
    public CustomerTerms? CustomerTerms { get; private set; }

    /// <summary>The arrangement, when they are a supplier.</summary>
    public SupplierTerms? SupplierTerms { get; private set; }

    /// <summary>Why they were put on hold, when they were.</summary>
    public string? HoldReason { get; private set; }

    /// <summary>Their addresses.</summary>
    public IReadOnlyCollection<Address> Addresses => _addresses.AsReadOnly();

    /// <summary>People to contact there.</summary>
    public IReadOnlyCollection<ContactDetail> Contacts => _contacts.AsReadOnly();

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

    /// <summary>True when we sell to them.</summary>
    public bool IsCustomer => Roles.HasFlag(PartnerRoles.Customer);

    /// <summary>True when we buy from them.</summary>
    public bool IsSupplier => Roles.HasFlag(PartnerRoles.Supplier);

    /// <summary>
    /// True when a new sales order may be taken. Sales asks this rather than reimplementing
    /// the rule, so "why can't I sell to them?" has one answer.
    /// </summary>
    public bool CanTakeNewOrders => Status == PartnerStatus.Active && IsCustomer;

    /// <summary>True when a purchase order may be raised on them.</summary>
    public bool CanPlacePurchaseOrders => Status == PartnerStatus.Active && IsSupplier;

    /// <summary>The billing address, or null when none has been recorded.</summary>
    public Address? BillingAddress =>
        _addresses.Find(address => address.Kind == AddressKind.Billing);

    /// <summary>Registers a partner. Roles are granted separately.</summary>
    /// <param name="code">Short code, uppercased automatically.</param>
    /// <param name="legalName">The registered company name.</param>
    /// <param name="taxNumber">Their tax identification number.</param>
    /// <param name="tradingName">Optional trading name.</param>
    public static Result<Partner> Create(
        string? code,
        string? legalName,
        TaxNumber taxNumber,
        string? tradingName = null)
    {
        ArgumentNullException.ThrowIfNull(taxNumber);

        if (string.IsNullOrWhiteSpace(code))
        {
            return PartnerErrors.Partner.CodeRequired;
        }

        string normalizedCode = code.Trim().ToUpperInvariant();
        if (normalizedCode.Length > MaxCodeLength)
        {
            return PartnerErrors.Partner.CodeTooLong;
        }

        Result<string> name = ValidateName(legalName);
        if (name.IsFailure)
        {
            return Result.Failure<Partner>(name.Error);
        }

        var partner = new Partner(PartnerId.New(), normalizedCode, name.Value, taxNumber)
        {
            TradingName = Clean(tradingName),
        };

        partner.Raise(new PartnerCreatedDomainEvent(partner.Id, normalizedCode, name.Value));

        return partner;
    }

    /// <summary>
    /// Starts selling to them on the given terms.
    /// <para>
    /// A billing address is required first. Everything downstream — the invoice, the VAT
    /// treatment, the dunning letter — needs somewhere to send it, and discovering that at
    /// invoicing time means an order that cannot be completed.
    /// </para>
    /// </summary>
    public Result GrantCustomerRole(CustomerTerms terms)
    {
        ArgumentNullException.ThrowIfNull(terms);

        if (Status == PartnerStatus.Closed)
        {
            return PartnerErrors.Partner.ClosedIsReadOnly;
        }

        if (BillingAddress is null)
        {
            return PartnerErrors.Partner.BillingAddressRequired;
        }

        Roles |= PartnerRoles.Customer;
        CustomerTerms = terms;

        Raise(new CustomerRoleGrantedDomainEvent(Id, Code, terms.CreditLimit.Amount));

        return Result.Success();
    }

    /// <summary>Starts buying from them on the given terms.</summary>
    public Result GrantSupplierRole(SupplierTerms terms)
    {
        ArgumentNullException.ThrowIfNull(terms);

        if (Status == PartnerStatus.Closed)
        {
            return PartnerErrors.Partner.ClosedIsReadOnly;
        }

        Roles |= PartnerRoles.Supplier;
        SupplierTerms = terms;

        Raise(new SupplierRoleGrantedDomainEvent(Id, Code));

        return Result.Success();
    }

    /// <summary>Changes the credit limit and payment terms.</summary>
    public Result ChangeCustomerTerms(CustomerTerms terms)
    {
        ArgumentNullException.ThrowIfNull(terms);

        if (!IsCustomer)
        {
            return PartnerErrors.Partner.NotACustomer;
        }

        if (Status == PartnerStatus.Closed)
        {
            return PartnerErrors.Partner.ClosedIsReadOnly;
        }

        Money? previous = CustomerTerms?.CreditLimit;
        CustomerTerms = terms;

        if (previous is not null && previous != terms.CreditLimit)
        {
            Raise(new CreditLimitChangedDomainEvent(
                Id, Code, previous.Amount, terms.CreditLimit.Amount));
        }

        return Result.Success();
    }

    /// <summary>
    /// Stops new orders without ending the relationship — the usual response to an overdue
    /// account. Existing orders are unaffected; somebody still has to decide about those.
    /// </summary>
    /// <param name="reason">Why. It will be read by whoever has to explain it to the customer.</param>
    public Result PlaceOnHold(string? reason)
    {
        if (Status == PartnerStatus.Closed)
        {
            return PartnerErrors.Partner.ClosedIsReadOnly;
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return PartnerErrors.Partner.HoldReasonRequired;
        }

        Status = PartnerStatus.OnHold;
        HoldReason = reason.Trim();

        Raise(new PartnerPlacedOnHoldDomainEvent(Id, Code, HoldReason));

        return Result.Success();
    }

    /// <summary>Lifts a hold.</summary>
    public Result ReleaseHold()
    {
        if (Status != PartnerStatus.OnHold)
        {
            return PartnerErrors.Partner.NotOnHold;
        }

        Status = PartnerStatus.Active;
        HoldReason = null;

        Raise(new PartnerHoldReleasedDomainEvent(Id, Code));

        return Result.Success();
    }

    /// <summary>Ends the relationship. Kept so historical documents still resolve.</summary>
    public Result Close()
    {
        if (Status == PartnerStatus.Closed)
        {
            return Result.Success();
        }

        Status = PartnerStatus.Closed;
        HoldReason = null;

        Raise(new PartnerClosedDomainEvent(Id, Code));

        return Result.Success();
    }

    /// <summary>Records an address. Replaces the existing one of the same kind for billing.</summary>
    public Result AddAddress(Address address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (Status == PartnerStatus.Closed)
        {
            return PartnerErrors.Partner.ClosedIsReadOnly;
        }

        // Exactly one billing address: two would make "where does the invoice go?" ambiguous
        // at the worst possible moment. Delivery addresses may be many - a workshop chain has
        // one per site.
        if (address.Kind == AddressKind.Billing)
        {
            _addresses.RemoveAll(existing => existing.Kind == AddressKind.Billing);
        }
        else if (_addresses.Contains(address))
        {
            return PartnerErrors.Address.Duplicate;
        }

        _addresses.Add(address);

        return Result.Success();
    }

    /// <summary>Removes an address.</summary>
    public Result RemoveAddress(Address address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (Status == PartnerStatus.Closed)
        {
            return PartnerErrors.Partner.ClosedIsReadOnly;
        }

        if (address.Kind == AddressKind.Billing && IsCustomer)
        {
            return PartnerErrors.Partner.BillingAddressRequired;
        }

        return _addresses.Remove(address)
            ? Result.Success()
            : PartnerErrors.Address.NotFound;
    }

    /// <summary>Records a contact. Only one may be primary.</summary>
    public Result AddContact(ContactDetail contact)
    {
        ArgumentNullException.ThrowIfNull(contact);

        if (Status == PartnerStatus.Closed)
        {
            return PartnerErrors.Partner.ClosedIsReadOnly;
        }

        if (_contacts.Contains(contact))
        {
            return PartnerErrors.Contact.Duplicate;
        }

        if (contact.IsPrimary)
        {
            _contacts.RemoveAll(existing => existing.IsPrimary);
        }

        _contacts.Add(contact);

        return Result.Success();
    }

    /// <summary>Removes a contact.</summary>
    public Result RemoveContact(ContactDetail contact)
    {
        ArgumentNullException.ThrowIfNull(contact);

        return _contacts.Remove(contact)
            ? Result.Success()
            : PartnerErrors.Contact.NotFound;
    }

    /// <summary>Corrects the legal or trading name.</summary>
    public Result Rename(string? legalName, string? tradingName)
    {
        if (Status == PartnerStatus.Closed)
        {
            return PartnerErrors.Partner.ClosedIsReadOnly;
        }

        Result<string> name = ValidateName(legalName);
        if (name.IsFailure)
        {
            return Result.FromError(name.Error);
        }

        LegalName = name.Value;
        TradingName = Clean(tradingName);

        return Result.Success();
    }

    /// <summary>Corrects the tax number.</summary>
    public Result ChangeTaxNumber(TaxNumber taxNumber)
    {
        ArgumentNullException.ThrowIfNull(taxNumber);

        if (Status == PartnerStatus.Closed)
        {
            return PartnerErrors.Partner.ClosedIsReadOnly;
        }

        TaxNumber = taxNumber;

        return Result.Success();
    }

    private static Result<string> ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return PartnerErrors.Partner.NameRequired;
        }

        string trimmed = name.Trim();

        return trimmed.Length > MaxNameLength
            ? PartnerErrors.Partner.NameTooLong
            : trimmed;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Which directions we trade with a partner in. Flags, because both is common.</summary>
[Flags]
public enum PartnerRoles
{
    /// <summary>Recorded, but not yet trading in either direction.</summary>
    None = 0,

    /// <summary>We sell to them.</summary>
    Customer = 1,

    /// <summary>We buy from them.</summary>
    Supplier = 2,
}

/// <summary>Whether we are still trading with a partner.</summary>
public enum PartnerStatus
{
    /// <summary>Unspecified. Never persisted.</summary>
    Unknown = 0,

    /// <summary>Trading normally.</summary>
    Active = 1,

    /// <summary>No new orders, usually because of an overdue account. Reversible.</summary>
    OnHold = 2,

    /// <summary>The relationship has ended. Kept so history still resolves.</summary>
    Closed = 3,
}
