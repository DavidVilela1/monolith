namespace AutoPartsErp.Modules.Partners.Application.Contracts;

/// <summary>One row in a partner list.</summary>
/// <param name="Id">The partner.</param>
/// <param name="Code">Their short code.</param>
/// <param name="Name">Trading name where there is one, otherwise the legal name.</param>
/// <param name="TaxNumber">Their tax number, formatted with the country prefix.</param>
/// <param name="Roles">Customer, Supplier, or both.</param>
/// <param name="Status">Active, OnHold or Closed.</param>
/// <param name="City">Billing city, for telling apart two branches of the same chain.</param>
/// <param name="CreditLimit">Their limit, when they buy on account.</param>
public sealed record PartnerSummary(
    Guid Id,
    string Code,
    string Name,
    string TaxNumber,
    string Roles,
    string Status,
    string? City,
    decimal? CreditLimit);

/// <summary>The full picture of one partner.</summary>
public sealed record PartnerDetail
{
    /// <summary>The partner.</summary>
    public required Guid Id { get; init; }

    /// <summary>Their short code.</summary>
    public required string Code { get; init; }

    /// <summary>The registered company name.</summary>
    public required string LegalName { get; init; }

    /// <summary>What they are actually called.</summary>
    public string? TradingName { get; init; }

    /// <summary>Tax country code.</summary>
    public required string TaxCountryCode { get; init; }

    /// <summary>The tax number.</summary>
    public required string TaxNumber { get; init; }

    /// <summary>True when the number passed a real check-digit algorithm.</summary>
    public required bool TaxNumberVerified { get; init; }

    /// <summary>Customer, Supplier, or both.</summary>
    public required string Roles { get; init; }

    /// <summary>Active, OnHold or Closed.</summary>
    public required string Status { get; init; }

    /// <summary>Why they are on hold, when they are.</summary>
    public string? HoldReason { get; init; }

    /// <summary>True when a new sales order may be taken.</summary>
    public required bool CanTakeNewOrders { get; init; }

    /// <summary>True when a purchase order may be raised on them.</summary>
    public required bool CanPlacePurchaseOrders { get; init; }

    /// <summary>Their credit limit, when they buy on account.</summary>
    public decimal? CreditLimit { get; init; }

    /// <summary>Currency of the credit limit.</summary>
    public string? CreditCurrency { get; init; }

    /// <summary>Days to pay. Zero means on delivery.</summary>
    public int? PaymentDueInDays { get; init; }

    /// <summary>Whether payment days run from the end of the invoice month.</summary>
    public bool? PaymentEndOfMonth { get; init; }

    /// <summary>How payment is expected.</summary>
    public string? PaymentMethod { get; init; }

    /// <summary>Which price list applies.</summary>
    public string? PriceListCode { get; init; }

    /// <summary>Typical days from order to delivery, when they are a supplier.</summary>
    public int? SupplierLeadTimeDays { get; init; }

    /// <summary>Our account number with them.</summary>
    public string? OurAccountNumber { get; init; }

    /// <summary>Their addresses.</summary>
    public required IReadOnlyList<AddressDto> Addresses { get; init; }

    /// <summary>People to contact there.</summary>
    public required IReadOnlyList<ContactDto> Contacts { get; init; }
}

/// <summary>An address recorded against a partner.</summary>
/// <param name="Kind">Billing, Delivery or Registered.</param>
/// <param name="Line1">Street and number.</param>
/// <param name="Line2">Floor, unit, estate.</param>
/// <param name="Postcode">Postcode.</param>
/// <param name="City">City or town.</param>
/// <param name="CountryCode">ISO two-letter country code.</param>
/// <param name="Notes">Delivery notes: gate codes, windows, where to ring.</param>
public sealed record AddressDto(
    string Kind,
    string Line1,
    string? Line2,
    string Postcode,
    string City,
    string CountryCode,
    string? Notes);

/// <summary>A contact recorded against a partner.</summary>
/// <param name="Name">The person's name.</param>
/// <param name="Role">What they do.</param>
/// <param name="Email">Email address.</param>
/// <param name="Phone">Phone number.</param>
/// <param name="IsPrimary">True for the person to call by default.</param>
public sealed record ContactDto(
    string Name,
    string? Role,
    string? Email,
    string? Phone,
    bool IsPrimary);
