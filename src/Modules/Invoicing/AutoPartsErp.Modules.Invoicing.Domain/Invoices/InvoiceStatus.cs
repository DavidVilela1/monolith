namespace AutoPartsErp.Modules.Invoicing.Domain.Invoices;

/// <summary>
/// Where a document stands, using the SAF-T (PT) codes.
/// <para>
/// There is no "deleted". A document that has been issued has a number in a gapless series and
/// has been reported; the only way to withdraw it is to void it, which leaves it in place with
/// its status changed. That is not a limitation of this design — it is the point of the design,
/// and it is why the accounting profession trusts the numbers.
/// </para>
/// </summary>
public enum InvoiceStatus
{
    /// <summary>Unspecified. Never persisted.</summary>
    Unknown = 0,

    /// <summary><c>N</c> — normal. The document stands as issued.</summary>
    Normal = 1,

    /// <summary>
    /// <c>A</c> — voided. The document was cancelled, keeps its number, and stays in every
    /// report with a reason attached.
    /// </summary>
    Voided = 2,

    /// <summary>
    /// <c>F</c> — billed. A working document that has since been turned into an invoice. Kept
    /// here for completeness of the typology; nothing in this module issues one yet.
    /// </summary>
    Billed = 3,
}

/// <summary>Turns an <see cref="InvoiceStatus"/> into the code the tax authority expects.</summary>
public static class InvoiceStatusCodes
{
    /// <summary>The SAF-T (PT) single-letter code, which is also field E of the QR code.</summary>
    /// <param name="status">The status.</param>
    /// <exception cref="ArgumentOutOfRangeException">The status is <see cref="InvoiceStatus.Unknown"/>.</exception>
    public static string Code(this InvoiceStatus status) => status switch
    {
        InvoiceStatus.Normal => "N",
        InvoiceStatus.Voided => "A",
        InvoiceStatus.Billed => "F",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "No SAF-T code for that status."),
    };
}
