using System.Globalization;

namespace AutoPartsErp.Modules.Invoicing.Domain.Signing;

/// <summary>
/// Builds the exact string a document's signature is computed over.
/// <para>
/// Five fields, semicolons, no spaces:
/// <c>InvoiceDate;SystemEntryDate;InvoiceNo;GrossTotal;PreviousHash</c>
/// </para>
/// <para>
/// Every character of this is prescribed. A date in the wrong format, a total with a comma, a
/// stray space after a semicolon — any of them produces a signature that verifies against
/// nothing, and the failure does not surface until the AT rejects a SAF-T file containing months
/// of documents. So the formatting lives here, as a pure function, and gets tested against the
/// worked examples in the legislation rather than against what looks right.
/// </para>
/// <para>
/// The last field is the previous document's signature <i>in the same series</i>, which is what
/// makes the run tamper-evident: altering document 35 invalidates 36 and everything after it.
/// The first document in a series has nothing before it, and its last field is empty — the
/// trailing semicolon still goes in.
/// </para>
/// </summary>
public static class SignatureSource
{
    /// <summary>The date format the tax authority prescribes for the document date.</summary>
    public const string DateFormat = "yyyy-MM-dd";

    /// <summary>The date-and-time format prescribed for when the record was created.</summary>
    public const string TimestampFormat = "yyyy-MM-ddTHH:mm:ss";

    /// <summary>
    /// Builds the string to sign.
    /// </summary>
    /// <param name="documentDate">The date on the document.</param>
    /// <param name="systemEntryDateUtc">
    /// When the record was created in the system. Distinct from the document date on purpose:
    /// backdating a document is legal, backdating its entry into the system is not, and the pair
    /// is what shows the difference.
    /// </param>
    /// <param name="documentNumber">The full number, e.g. <c>FT SERIE2026/35</c>.</param>
    /// <param name="grossTotal">The document total including VAT.</param>
    /// <param name="previousSignature">
    /// The previous document's base64 signature in the same series, or null for the first one.
    /// </param>
    public static string Build(
        DateOnly documentDate,
        DateTimeOffset systemEntryDateUtc,
        string documentNumber,
        decimal grossTotal,
        string? previousSignature)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentNumber);

        string date = documentDate.ToString(DateFormat, CultureInfo.InvariantCulture);
        string entry = systemEntryDateUtc.UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture);
        string total = FormatAmount(grossTotal);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{date};{entry};{documentNumber};{total};{previousSignature ?? string.Empty}");
    }

    /// <summary>
    /// Formats a monetary amount the way every prescribed field expects it: two decimal places,
    /// a dot, no thousands separator, and a leading minus where it applies.
    /// <para>
    /// Shared with the QR code, because the two disagreeing on a rounding boundary would be a
    /// document whose printed total and signed total are a cent apart.
    /// </para>
    /// </summary>
    /// <param name="amount">The amount.</param>
    public static string FormatAmount(decimal amount) =>
        Math.Round(amount, 2, MidpointRounding.ToEven).ToString("0.00", CultureInfo.InvariantCulture);
}
