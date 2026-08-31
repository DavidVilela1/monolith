using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Partners.Domain.Partners;

/// <summary>What an address is for.</summary>
public enum AddressKind
{
    /// <summary>Unspecified. Never persisted.</summary>
    Unknown = 0,

    /// <summary>Where invoices go. Required before a partner can be invoiced.</summary>
    Billing = 1,

    /// <summary>Where goods go. A workshop chain may have many.</summary>
    Delivery = 2,

    /// <summary>The registered office, as it appears on official filings.</summary>
    Registered = 3,
}

/// <summary>
/// A postal address.
/// <para>
/// Kept deliberately loose on structure. Address formats differ enough between countries that a
/// rigid schema either rejects valid addresses or forces staff to lie to the form; a delivery
/// that goes to the wrong place because the system had nowhere to put "gate code 4472" is a real
/// cost. The parts that matter for tax and routing — postcode, city, country — are separate; the
/// rest is free lines.
/// </para>
/// </summary>
public sealed class Address : ValueObject
{
    private Address(
        AddressKind kind,
        string line1,
        string? line2,
        string postcode,
        string city,
        string countryCode,
        string? notes)
    {
        Kind = kind;
        Line1 = line1;
        Line2 = line2;
        Postcode = postcode;
        City = city;
        CountryCode = countryCode;
        Notes = notes;
    }

    /// <summary>Required by EF Core materialization.</summary>
#pragma warning disable CS8618
    private Address()
    {
    }
#pragma warning restore CS8618

    /// <summary>What this address is used for.</summary>
    public AddressKind Kind { get; }

    /// <summary>Street and number.</summary>
    public string Line1 { get; } = string.Empty;

    /// <summary>Floor, unit, industrial estate.</summary>
    public string? Line2 { get; }

    /// <summary>Postcode, as written locally.</summary>
    public string Postcode { get; } = string.Empty;

    /// <summary>City or town.</summary>
    public string City { get; } = string.Empty;

    /// <summary>ISO two-letter country code.</summary>
    public string CountryCode { get; } = string.Empty;

    /// <summary>Anything the driver needs: gate codes, delivery windows, "ring the bell at the back".</summary>
    public string? Notes { get; }

    /// <summary>Creates an address.</summary>
    /// <param name="kind">What it is used for.</param>
    /// <param name="line1">Street and number.</param>
    /// <param name="postcode">Postcode.</param>
    /// <param name="city">City or town.</param>
    /// <param name="countryCode">ISO two-letter country code.</param>
    /// <param name="line2">Optional second line.</param>
    /// <param name="notes">Optional delivery notes.</param>
    public static Result<Address> Create(
        AddressKind kind,
        string? line1,
        string? postcode,
        string? city,
        string? countryCode,
        string? line2 = null,
        string? notes = null)
    {
        if (kind == AddressKind.Unknown)
        {
            return PartnerErrors.Address.KindRequired;
        }

        if (string.IsNullOrWhiteSpace(line1))
        {
            return PartnerErrors.Address.Line1Required;
        }

        if (string.IsNullOrWhiteSpace(postcode))
        {
            return PartnerErrors.Address.PostcodeRequired;
        }

        if (string.IsNullOrWhiteSpace(city))
        {
            return PartnerErrors.Address.CityRequired;
        }

        if (string.IsNullOrWhiteSpace(countryCode) || countryCode.Trim().Length != 2)
        {
            return PartnerErrors.Partner.CountryCodeInvalid;
        }

        return new Address(
            kind,
            line1.Trim(),
            Clean(line2),
            postcode.Trim().ToUpperInvariant(),
            city.Trim(),
            countryCode.Trim().ToUpperInvariant(),
            Clean(notes));
    }

    /// <summary>Rehydrates an address already known to be valid.</summary>
    public static Address FromStorage(
        AddressKind kind,
        string line1,
        string? line2,
        string postcode,
        string city,
        string countryCode,
        string? notes) =>
        new(kind, line1, line2, postcode, city, countryCode, notes);

    /// <inheritdoc />
    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Kind;
        yield return Line1;
        yield return Line2;
        yield return Postcode;
        yield return City;
        yield return CountryCode;
    }

    /// <inheritdoc />
    public override string ToString() => $"{Line1}, {Postcode} {City}, {CountryCode}";

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// A named person at a partner, with how to reach them.
/// The counter needs to know who to call about a wrong-fit part, and accounts needs to know who
/// to chase about an overdue invoice — rarely the same person.
/// </summary>
public sealed class ContactDetail : ValueObject
{
    private ContactDetail(string name, string? role, string? email, string? phone, bool isPrimary)
    {
        Name = name;
        Role = role;
        Email = email;
        Phone = phone;
        IsPrimary = isPrimary;
    }

    /// <summary>Required by EF Core materialization.</summary>
#pragma warning disable CS8618
    private ContactDetail()
    {
    }
#pragma warning restore CS8618

    /// <summary>The person's name.</summary>
    public string Name { get; } = string.Empty;

    /// <summary>What they do: workshop manager, accounts, parts buyer.</summary>
    public string? Role { get; }

    /// <summary>Email address.</summary>
    public string? Email { get; }

    /// <summary>Phone number, stored as given.</summary>
    public string? Phone { get; }

    /// <summary>True for the person to call by default.</summary>
    public bool IsPrimary { get; }

    /// <summary>Creates a contact. At least one of email or phone is required.</summary>
    public static Result<ContactDetail> Create(
        string? name,
        string? role = null,
        string? email = null,
        string? phone = null,
        bool isPrimary = false)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return PartnerErrors.Contact.NameRequired;
        }

        if (string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(phone))
        {
            return PartnerErrors.Contact.NoWayToReachThem;
        }

        string? cleanedEmail = Clean(email)?.ToLowerInvariant();

        if (cleanedEmail is not null && !cleanedEmail.Contains('@', StringComparison.Ordinal))
        {
            return PartnerErrors.Contact.EmailInvalid;
        }

        return new ContactDetail(name.Trim(), Clean(role), cleanedEmail, Clean(phone), isPrimary);
    }

    /// <summary>Rehydrates a contact already known to be valid.</summary>
    public static ContactDetail FromStorage(
        string name,
        string? role,
        string? email,
        string? phone,
        bool isPrimary) =>
        new(name, role, email, phone, isPrimary);

    /// <inheritdoc />
    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Name;
        yield return Email;
        yield return Phone;
    }

    /// <inheritdoc />
    public override string ToString() => Role is null ? Name : $"{Name} ({Role})";

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
