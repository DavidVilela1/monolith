namespace AutoPartsErp.Modules.Invoicing.Application.Contracts;

/// <summary>A document series, as a list of them is rendered.</summary>
/// <param name="Id">The series.</param>
/// <param name="Type">FT, FS, FR, NC or ND.</param>
/// <param name="Code">Its identifier, e.g. <c>SERIE2026</c>.</param>
/// <param name="Year">The year it belongs to.</param>
/// <param name="Status">Registered, Active or Closed.</param>
/// <param name="ValidationCode">The code the tax authority returned, or null until it has.</param>
/// <param name="NextNumber">The next number it will hand out.</param>
/// <param name="IssuedCount">How many documents it has issued.</param>
public sealed record DocumentSeriesDto(
    Guid Id,
    string Type,
    string Code,
    int Year,
    string Status,
    string? ValidationCode,
    int NextNumber,
    int IssuedCount);

/// <summary>A document in a list.</summary>
/// <param name="Id">The document.</param>
/// <param name="Type">What kind it is.</param>
/// <param name="DocumentNumber">Its number, or empty while it is a draft.</param>
/// <param name="Atcud">Its unique code, or null while it is a draft.</param>
/// <param name="Status">Normal, Voided or Billed.</param>
/// <param name="DocumentDate">The date on it.</param>
/// <param name="CustomerId">Who it is for.</param>
/// <param name="CustomerName">Their name, as it was on the day.</param>
/// <param name="CustomerTaxNumber">Their NIF, or null when they were not identified.</param>
/// <param name="NetTotal">The value before VAT.</param>
/// <param name="VatTotal">The VAT.</param>
/// <param name="GrossTotal">What they are asked to pay.</param>
/// <param name="CurrencyCode">Currency of all three.</param>
public sealed record InvoiceSummary(
    Guid Id,
    string Type,
    string DocumentNumber,
    string? Atcud,
    string Status,
    DateOnly DocumentDate,
    Guid CustomerId,
    string CustomerName,
    string? CustomerTaxNumber,
    decimal NetTotal,
    decimal VatTotal,
    decimal GrossTotal,
    string CurrencyCode);

/// <summary>
/// One document in full, with everything a page has to print on it.
/// <para>
/// The four printed characters of the signature and the QR payload are on here because a document
/// that cannot show them is not a legal document. A renderer turns <see cref="QrCode"/> into the
/// square; nothing recomputes it.
/// </para>
/// </summary>
public sealed record InvoiceDetail
{
    /// <summary>The document.</summary>
    public required Guid Id { get; init; }

    /// <summary>What kind it is.</summary>
    public required string Type { get; init; }

    /// <summary>Its number, or empty while it is a draft.</summary>
    public required string DocumentNumber { get; init; }

    /// <summary>Its unique code, or null while it is a draft.</summary>
    public string? Atcud { get; init; }

    /// <summary>Normal, Voided or Billed.</summary>
    public required string Status { get; init; }

    /// <summary>The date on it.</summary>
    public required DateOnly DocumentDate { get; init; }

    /// <summary>When the record was created in the system.</summary>
    public DateTimeOffset? SystemEntryDateUtc { get; init; }

    /// <summary>Who it is for.</summary>
    public required Guid CustomerId { get; init; }

    /// <summary>Their name, as it was on the day.</summary>
    public required string CustomerName { get; init; }

    /// <summary>Their NIF, or null when they were not identified.</summary>
    public string? CustomerTaxNumber { get; init; }

    /// <summary>Their country.</summary>
    public required string CustomerCountry { get; init; }

    /// <summary>Which set of rates applies.</summary>
    public required string TaxRegion { get; init; }

    /// <summary>Currency of every figure.</summary>
    public required string CurrencyCode { get; init; }

    /// <summary>The order it was raised against, when there was one.</summary>
    public Guid? SalesOrderId { get; init; }

    /// <summary>Value of exempt lines.</summary>
    public required decimal ExemptBase { get; init; }

    /// <summary>Value of lines at the reduced rate.</summary>
    public required decimal ReducedBase { get; init; }

    /// <summary>VAT on those lines.</summary>
    public required decimal ReducedVat { get; init; }

    /// <summary>Value of lines at the intermediate rate.</summary>
    public required decimal IntermediateBase { get; init; }

    /// <summary>VAT on those lines.</summary>
    public required decimal IntermediateVat { get; init; }

    /// <summary>Value of lines at the standard rate.</summary>
    public required decimal StandardBase { get; init; }

    /// <summary>VAT on those lines.</summary>
    public required decimal StandardVat { get; init; }

    /// <summary>The value before VAT.</summary>
    public required decimal NetTotal { get; init; }

    /// <summary>The VAT.</summary>
    public required decimal VatTotal { get; init; }

    /// <summary>What the customer is asked to pay.</summary>
    public required decimal GrossTotal { get; init; }

    /// <summary>The four characters of the signature that go on the page.</summary>
    public string? SignatureCharacters { get; init; }

    /// <summary>The payload a renderer turns into the QR square.</summary>
    public string? QrCode { get; init; }

    /// <summary>Why it was voided, when it was.</summary>
    public string? VoidReason { get; init; }

    /// <summary>True while it can still be changed.</summary>
    public required bool IsDraft { get; init; }

    /// <summary>Its lines, in the order they appear on the page.</summary>
    public required IReadOnlyList<InvoiceLineDto> Lines { get; init; }
}

/// <summary>One line of a document.</summary>
/// <param name="Id">The line.</param>
/// <param name="Number">Its position on the page, from 1.</param>
/// <param name="PartId">The part sold.</param>
/// <param name="Sku">Its SKU, as it was on the day.</param>
/// <param name="Description">Its description, as it was on the day.</param>
/// <param name="Quantity">How much was sold.</param>
/// <param name="UnitCode">The unit it was sold in.</param>
/// <param name="UnitPrice">The price per unit, before discount.</param>
/// <param name="DiscountPercent">The discount given.</param>
/// <param name="NetAmount">What the line is worth before VAT.</param>
/// <param name="VatCode">ISE, RED, INT or NOR.</param>
/// <param name="VatPercent">The rate applied.</param>
/// <param name="VatAmount">The VAT on the line.</param>
/// <param name="GrossAmount">What it adds to the total.</param>
/// <param name="ExemptionCode">The exemption code, when the line is exempt.</param>
/// <param name="ExemptionReason">The legal basis, which has to be printed.</param>
public sealed record InvoiceLineDto(
    Guid Id,
    int Number,
    Guid PartId,
    string Sku,
    string Description,
    decimal Quantity,
    string UnitCode,
    decimal UnitPrice,
    decimal DiscountPercent,
    decimal NetAmount,
    string VatCode,
    decimal VatPercent,
    decimal VatAmount,
    decimal GrossAmount,
    string? ExemptionCode,
    string? ExemptionReason);

/// <summary>What to look for when searching documents.</summary>
public sealed record InvoiceSearchCriteria
{
    /// <summary>Free text, matched against the document number and the customer name.</summary>
    public string? Term { get; init; }

    /// <summary>Restrict to one customer.</summary>
    public Guid? CustomerId { get; init; }

    /// <summary>Restrict to one document type.</summary>
    public string? Type { get; init; }

    /// <summary>Restrict to one status.</summary>
    public string? Status { get; init; }

    /// <summary>The earliest document date to include.</summary>
    public DateOnly? From { get; init; }

    /// <summary>The latest document date to include.</summary>
    public DateOnly? To { get; init; }

    /// <summary>True to return only drafts — the work in progress rather than the record.</summary>
    public bool DraftsOnly { get; init; }
}
