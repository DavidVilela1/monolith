using System.Globalization;
using System.Text;
using AutoPartsErp.Modules.Invoicing.Domain.Series;
using AutoPartsErp.Modules.Invoicing.Domain.Signing;

namespace AutoPartsErp.Modules.Invoicing.Domain.Invoices;

/// <summary>
/// Builds the string that goes inside the QR code printed on every document.
/// <para>
/// Fields joined with <c>*</c>, each written <c>Code:Value</c> with no spaces, in the order the
/// specification lists them. Monetary fields carry exactly two decimals with a dot. Optional
/// fields with nothing in them are left out entirely rather than written as zero — an omitted
/// field means "no lines in this category", and <c>I4:0.00</c> means "lines at the reduced rate
/// that somehow produced no VAT", which is a different and suspicious claim.
/// </para>
/// <para>
/// A pure function over data the aggregate already holds. That is deliberate: this string is the
/// one artefact a tax inspector can scan off a printed page, and it should be testable against
/// the worked example in the specification without a database, a renderer or a running host.
/// </para>
/// <para>
/// The tax-region blocks J and K are not produced. They exist for a document that mixes mainland
/// and island rates on one page, which needs establishments in both — see the note on
/// <see cref="Invoice.TaxRegion"/>.
/// </para>
/// </summary>
public static class QrCodePayload
{
    /// <summary>
    /// The NIF used for a customer who is not identified, which is most of a trade counter's day.
    /// </summary>
    public const string FinalConsumerNif = "999999990";

    /// <summary>Builds the payload.</summary>
    /// <param name="issuerNif">The company's own NIF, without a country prefix.</param>
    /// <param name="customerNif">
    /// The customer's NIF, or null for an unidentified customer — in which case
    /// <see cref="FinalConsumerNif"/> goes in instead.
    /// </param>
    /// <param name="customerCountry">The customer's country, ISO two-letter, or "Desconhecido".</param>
    /// <param name="type">The document type.</param>
    /// <param name="status">The document status.</param>
    /// <param name="documentDate">The date on the document.</param>
    /// <param name="documentNumber">The full number, e.g. <c>FT SERIE2026/35</c>.</param>
    /// <param name="atcud">The document's unique code.</param>
    /// <param name="region">The tax region the rates belong to.</param>
    /// <param name="taxes">The document's totals split by VAT category.</param>
    /// <param name="signature">The document's signature, for its four printed characters.</param>
    /// <param name="certificateNumber">The software's AT certification number.</param>
    public static string Build(
        string issuerNif,
        string? customerNif,
        string customerCountry,
        DocumentType type,
        InvoiceStatus status,
        DateOnly documentDate,
        string documentNumber,
        Atcud atcud,
        TaxRegion region,
        TaxSummary taxes,
        DocumentSignature signature,
        string certificateNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuerNif);
        ArgumentException.ThrowIfNullOrWhiteSpace(customerCountry);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentNumber);
        ArgumentNullException.ThrowIfNull(atcud);
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentException.ThrowIfNullOrWhiteSpace(certificateNumber);

        var builder = new StringBuilder(256);

        Append(builder, "A", issuerNif);
        Append(builder, "B", string.IsNullOrWhiteSpace(customerNif) ? FinalConsumerNif : customerNif);
        Append(builder, "C", customerCountry);
        Append(builder, "D", type.Code());
        Append(builder, "E", status.Code());
        Append(builder, "F", documentDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
        Append(builder, "G", documentNumber);
        Append(builder, "H", atcud.Value);
        Append(builder, "I1", region.Code());

        AppendAmountIfAny(builder, "I2", taxes.ExemptBase);
        AppendAmountIfAny(builder, "I3", taxes.ReducedBase);
        AppendAmountIfAny(builder, "I4", taxes.ReducedVat);
        AppendAmountIfAny(builder, "I5", taxes.IntermediateBase);
        AppendAmountIfAny(builder, "I6", taxes.IntermediateVat);
        AppendAmountIfAny(builder, "I7", taxes.StandardBase);
        AppendAmountIfAny(builder, "I8", taxes.StandardVat);

        // N and O are mandatory, so they are written even when zero. A credit note for the full
        // value of an invoice legitimately nets to nothing, and leaving the fields out would make
        // it look like a document with no totals rather than one that balances.
        Append(builder, "N", SignatureSource.FormatAmount(taxes.VatTotal));
        Append(builder, "O", SignatureSource.FormatAmount(taxes.GrossTotal));
        Append(builder, "Q", signature.Printed);
        Append(builder, "R", certificateNumber);

        return builder.ToString();
    }

    private static void Append(StringBuilder builder, string code, string value)
    {
        if (builder.Length > 0)
        {
            builder.Append('*');
        }

        builder.Append(code).Append(':').Append(value);
    }

    private static void AppendAmountIfAny(StringBuilder builder, string code, decimal amount)
    {
        if (amount != 0m)
        {
            Append(builder, code, SignatureSource.FormatAmount(amount));
        }
    }
}
