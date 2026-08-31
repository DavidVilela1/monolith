using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Partners.Domain.Partners;

/// <summary>
/// A tax identification number, with its country.
/// <para>
/// Worth validating rather than storing as free text. A wrong NIF on an invoice is a rejected
/// invoice, a customer who cannot reclaim VAT, and in Portugal a SAF-T submission the tax
/// authority pushes back. Catching a transposed digit at the counter costs nothing; catching it
/// at month end costs an afternoon.
/// </para>
/// <para>
/// Portuguese numbers are checked properly, including the check digit. Other countries are
/// accepted on shape alone for now — a wrong-but-plausible number is still better recorded than
/// refused, and adding a country's algorithm later is a change in one place.
/// </para>
/// </summary>
public sealed class TaxNumber : ValueObject
{
    /// <summary>Longest permitted tax number.</summary>
    public const int MaxLength = 20;

    private TaxNumber(string countryCode, string value, bool isVerified)
    {
        CountryCode = countryCode;
        Value = value;
        IsVerified = isVerified;
    }

    /// <summary>Required by EF Core materialization.</summary>
#pragma warning disable CS8618
    private TaxNumber()
    {
    }
#pragma warning restore CS8618

    /// <summary>ISO two-letter country code, uppercase.</summary>
    public string CountryCode { get; } = string.Empty;

    /// <summary>The number itself, without spaces or the country prefix.</summary>
    public string Value { get; } = string.Empty;

    /// <summary>
    /// True when the number passed a real check-digit algorithm, false when it was only
    /// checked for shape. Lets a report list the partners whose tax data nobody has confirmed.
    /// </summary>
    public bool IsVerified { get; }

    /// <summary>The number as it appears on documents, e.g. <c>PT501234567</c>.</summary>
    public string Formatted => $"{CountryCode}{Value}";

    /// <summary>Creates a tax number.</summary>
    /// <param name="countryCode">ISO two-letter country code.</param>
    /// <param name="value">The number. Spaces, dots and a leading country prefix are stripped.</param>
    public static Result<TaxNumber> Create(string? countryCode, string? value)
    {
        if (string.IsNullOrWhiteSpace(countryCode) || countryCode.Trim().Length != 2)
        {
            return PartnerErrors.Partner.CountryCodeInvalid;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            return PartnerErrors.Partner.TaxNumberRequired;
        }

        string country = countryCode.Trim().ToUpperInvariant();
        string digits = Normalize(value, country);

        if (digits.Length == 0 || digits.Length > MaxLength)
        {
            return PartnerErrors.Partner.TaxNumberInvalid;
        }

        if (country == "PT")
        {
            return IsValidPortugueseNif(digits)
                ? new TaxNumber(country, digits, isVerified: true)
                : PartnerErrors.Partner.TaxNumberFailsChecksum;
        }

        return new TaxNumber(country, digits, isVerified: false);
    }

    /// <summary>Rehydrates a tax number already known to be valid.</summary>
    public static TaxNumber FromStorage(string countryCode, string value, bool isVerified) =>
        new(countryCode, value, isVerified);

    /// <summary>
    /// Validates a Portuguese NIF: nine digits, where the last is a check digit derived from
    /// the first eight weighted 9 down to 2, modulo 11.
    /// </summary>
    public static bool IsValidPortugueseNif(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length != 9)
        {
            return false;
        }

        foreach (char character in value)
        {
            if (!char.IsAsciiDigit(character))
            {
                return false;
            }
        }

        // The first digit identifies the kind of taxpayer. 0 is not issued.
        if (value[0] == '0')
        {
            return false;
        }

        int sum = 0;
        for (int i = 0; i < 8; i++)
        {
            sum += (value[i] - '0') * (9 - i);
        }

        int remainder = sum % 11;
        int checkDigit = remainder < 2 ? 0 : 11 - remainder;

        return checkDigit == value[8] - '0';
    }

    private static string Normalize(string value, string countryCode)
    {
        string trimmed = value.Trim().ToUpperInvariant();

        // People paste "PT 501 234 567" as often as "501234567".
        if (trimmed.StartsWith(countryCode, StringComparison.Ordinal))
        {
            trimmed = trimmed[countryCode.Length..];
        }

        Span<char> buffer = stackalloc char[trimmed.Length];
        int length = 0;

        foreach (char character in trimmed)
        {
            if (char.IsLetterOrDigit(character))
            {
                buffer[length++] = character;
            }
        }

        return new string(buffer[..length]);
    }

    /// <inheritdoc />
    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return CountryCode;
        yield return Value;
    }

    /// <inheritdoc />
    public override string ToString() => Formatted;
}
