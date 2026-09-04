namespace AutoPartsErp.Modules.Invoicing.Domain.Series;

/// <summary>
/// The kinds of document this module issues, using the SAF-T (PT) codes.
/// <para>
/// The two-letter codes are not an internal choice — they are what goes in field D of the QR
/// code, in the <c>InvoiceType</c> element of a SAF-T export, and in the series registration at
/// the AT. Naming the enum members after anything else would mean a translation table that
/// somebody eventually gets backwards.
/// </para>
/// </summary>
public enum DocumentType
{
    /// <summary>Unspecified. Never persisted.</summary>
    Unknown = 0,

    /// <summary>
    /// <c>FT</c> — an invoice. The normal case: goods sold on account, paid later.
    /// </summary>
    Invoice = 1,

    /// <summary>
    /// <c>FS</c> — a simplified invoice. What a trade counter issues for a small cash sale, where
    /// the law allows the customer not to be identified.
    /// </summary>
    SimplifiedInvoice = 2,

    /// <summary>
    /// <c>FR</c> — an invoice-receipt. Issued and settled in the same act, which is most of what
    /// crosses a parts counter.
    /// </summary>
    InvoiceReceipt = 3,

    /// <summary>
    /// <c>NC</c> — a credit note. The only correct way to reverse an invoice: the original stands
    /// and this cancels it, because a document that has been issued and reported cannot be
    /// unissued.
    /// </summary>
    CreditNote = 4,

    /// <summary>
    /// <c>ND</c> — a debit note. Charges something that was missed, without reissuing.
    /// </summary>
    DebitNote = 5,
}

/// <summary>Turns a <see cref="DocumentType"/> into the code the tax authority expects.</summary>
public static class DocumentTypeCodes
{
    /// <summary>The SAF-T (PT) two-letter code, e.g. <c>FT</c>.</summary>
    /// <param name="type">The document type.</param>
    /// <exception cref="ArgumentOutOfRangeException">The type is <see cref="DocumentType.Unknown"/>.</exception>
    public static string Code(this DocumentType type) => type switch
    {
        DocumentType.Invoice => "FT",
        DocumentType.SimplifiedInvoice => "FS",
        DocumentType.InvoiceReceipt => "FR",
        DocumentType.CreditNote => "NC",
        DocumentType.DebitNote => "ND",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "No SAF-T code for that document type."),
    };

    /// <summary>Reads a SAF-T (PT) code back, case-insensitively.</summary>
    /// <param name="code">The two-letter code.</param>
    /// <param name="type">The type it denotes.</param>
    /// <returns>True when the code is one this module issues.</returns>
    public static bool TryFromCode(string? code, out DocumentType type)
    {
        switch (code?.Trim().ToUpperInvariant())
        {
            case "FT": type = DocumentType.Invoice; return true;
            case "FS": type = DocumentType.SimplifiedInvoice; return true;
            case "FR": type = DocumentType.InvoiceReceipt; return true;
            case "NC": type = DocumentType.CreditNote; return true;
            case "ND": type = DocumentType.DebitNote; return true;
            default: type = DocumentType.Unknown; return false;
        }
    }

    /// <summary>
    /// True when the document reduces what the customer owes.
    /// <para>
    /// Only the credit note does. It matters because its totals go onto a SAF-T export and a VAT
    /// return with the opposite sign to everything else, and because the running balance a
    /// customer sees has to agree with the paperwork.
    /// </para>
    /// </summary>
    /// <param name="type">The document type.</param>
    public static bool IsCredit(this DocumentType type) => type == DocumentType.CreditNote;
}
