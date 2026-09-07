namespace AutoPartsErp.Modules.Invoicing.Application.Saft;

/// <summary>
/// A SAF-T (PT) audit file, as data, before anything has been written as XML.
/// <para>
/// The shape mirrors the schema rather than the database on purpose. Every element in this tree
/// exists because the XSD says so, in the order the XSD says, and having that visible in C# is
/// what lets the writer be a dumb translation instead of a place where two mental models argue.
/// </para>
/// <para>
/// It carries only what an invoicing-only file needs: <c>TaxAccountingBasis</c> is <c>F</c>, so
/// there are no ledger accounts, no suppliers and no purchase documents. The parts that are
/// absent are absent because they belong to modules this one cannot see, which is the same reason
/// they are absent from the obligation.
/// </para>
/// </summary>
public sealed class SaftAuditFile
{
    /// <summary>Who, when, and which program.</summary>
    public required SaftHeader Header { get; init; }

    /// <summary>The customers referenced by the documents in the period.</summary>
    public required IReadOnlyList<SaftCustomer> Customers { get; init; }

    /// <summary>The products referenced by the lines in the period.</summary>
    public required IReadOnlyList<SaftProduct> Products { get; init; }

    /// <summary>Every distinct tax rate used in the period.</summary>
    public required IReadOnlyList<SaftTaxTableEntry> TaxTable { get; init; }

    /// <summary>The documents themselves.</summary>
    public required IReadOnlyList<SaftInvoice> Invoices { get; init; }

    /// <summary>
    /// The sum of every line's debit amount. Debits are credit-note lines, so on a month with no
    /// credit notes this is zero — which is correct rather than suspicious.
    /// </summary>
    public decimal TotalDebit => Invoices
        .Where(invoice => invoice.IsCreditNote)
        .Sum(invoice => invoice.Lines.Sum(line => line.NetAmount));

    /// <summary>The sum of every line's credit amount: everything that is not a credit note.</summary>
    public decimal TotalCredit => Invoices
        .Where(invoice => !invoice.IsCreditNote)
        .Sum(invoice => invoice.Lines.Sum(line => line.NetAmount));
}

/// <summary>The file header.</summary>
public sealed class SaftHeader
{
    /// <summary>The company's registry identifier, or its tax number when it has none.</summary>
    public required string CompanyId { get; init; }

    /// <summary>The company's NIF, digits only.</summary>
    public required string TaxRegistrationNumber { get; init; }

    /// <summary>The registered name.</summary>
    public required string CompanyName { get; init; }

    /// <summary>The trading name, when it differs.</summary>
    public string? BusinessName { get; init; }

    /// <summary>Street and number.</summary>
    public required string AddressDetail { get; init; }

    /// <summary>The town.</summary>
    public required string City { get; init; }

    /// <summary>The postcode.</summary>
    public required string PostalCode { get; init; }

    /// <summary>The year the period falls in.</summary>
    public required int FiscalYear { get; init; }

    /// <summary>The first day covered.</summary>
    public required DateOnly StartDate { get; init; }

    /// <summary>The last day covered.</summary>
    public required DateOnly EndDate { get; init; }

    /// <summary>The day the file was produced.</summary>
    public required DateOnly DateCreated { get; init; }

    /// <summary>The software vendor's NIF.</summary>
    public required string ProductCompanyTaxId { get; init; }

    /// <summary>The certification number the tax authority issued, or <c>0</c> when uncertified.</summary>
    public required string SoftwareCertificateNumber { get; init; }

    /// <summary>The program, as <c>name/vendor</c>.</summary>
    public required string ProductId { get; init; }

    /// <summary>The program's version.</summary>
    public required string ProductVersion { get; init; }
}

/// <summary>One customer in the master file.</summary>
/// <param name="CustomerId">The identifier the documents reference.</param>
/// <param name="AccountId">
/// The general-ledger account. Always <c>Desconhecido</c> here: this is an invoicing-only file
/// and the module has no chart of accounts to name.
/// </param>
/// <param name="CustomerTaxId">Their NIF, or the final-consumer placeholder.</param>
/// <param name="CompanyName">Their name.</param>
/// <param name="AddressDetail">Street and number, or <c>Desconhecido</c>.</param>
/// <param name="City">The town, or <c>Desconhecido</c>.</param>
/// <param name="PostalCode">The postcode, or <c>Desconhecido</c>.</param>
/// <param name="Country">ISO two-letter, or <c>Desconhecido</c>.</param>
public sealed record SaftCustomer(
    string CustomerId,
    string AccountId,
    string CustomerTaxId,
    string CompanyName,
    string AddressDetail,
    string City,
    string PostalCode,
    string Country);

/// <summary>One product in the master file.</summary>
/// <param name="ProductType">
/// <c>P</c> for a physical product, which every line in a parts distributor's file is.
/// </param>
/// <param name="ProductCode">The SKU, as the document recorded it.</param>
/// <param name="ProductDescription">The description, as the document recorded it.</param>
/// <param name="ProductNumberCode">
/// The manufacturer's or EAN code. The SKU again when there is nothing better, which the tax
/// authority accepts.
/// </param>
public sealed record SaftProduct(
    string ProductType,
    string ProductCode,
    string ProductDescription,
    string ProductNumberCode);

/// <summary>One rate in the tax table.</summary>
/// <param name="TaxType">Always <c>IVA</c> here.</param>
/// <param name="TaxCountryRegion">PT, PT-AC or PT-MA.</param>
/// <param name="TaxCode">ISE, RED, INT or NOR.</param>
/// <param name="Description">What the rate is called, in words.</param>
/// <param name="TaxPercentage">The rate.</param>
public sealed record SaftTaxTableEntry(
    string TaxType,
    string TaxCountryRegion,
    string TaxCode,
    string Description,
    decimal TaxPercentage);

/// <summary>
/// One document.
/// <para>
/// A record rather than a class so that the export can take what the database gave it and set the
/// one field the database has no business knowing — which key version signed it, a fact about the
/// deployment's configuration rather than about the document.
/// </para>
/// </summary>
public sealed record SaftInvoice
{
    /// <summary>Its number, e.g. <c>FT SERIE2026/35</c>.</summary>
    public required string InvoiceNo { get; init; }

    /// <summary>Its unique code.</summary>
    public required string Atcud { get; init; }

    /// <summary><c>N</c> for a live document, <c>A</c> for a voided one.</summary>
    public required string Status { get; init; }

    /// <summary>When it reached that status.</summary>
    public required DateTimeOffset StatusDate { get; init; }

    /// <summary>Why it was voided. Required by the schema when the status is <c>A</c>.</summary>
    public string? StatusReason { get; init; }

    /// <summary>Who issued it.</summary>
    public required string SourceId { get; init; }

    /// <summary>The full signature.</summary>
    public required string Hash { get; init; }

    /// <summary>Which key version signed it.</summary>
    public required string HashControl { get; init; }

    /// <summary>The month, 1 to 12.</summary>
    public required int Period { get; init; }

    /// <summary>The date on the document.</summary>
    public required DateOnly InvoiceDate { get; init; }

    /// <summary>FT, FS, FR, NC or ND.</summary>
    public required string InvoiceType { get; init; }

    /// <summary>When the record was created in the system.</summary>
    public required DateTimeOffset SystemEntryDate { get; init; }

    /// <summary>The customer it is addressed to.</summary>
    public required string CustomerId { get; init; }

    /// <summary>Its lines.</summary>
    public required IReadOnlyList<SaftInvoiceLine> Lines { get; init; }

    /// <summary>The VAT.</summary>
    public required decimal TaxPayable { get; init; }

    /// <summary>The value before VAT.</summary>
    public required decimal NetTotal { get; init; }

    /// <summary>What the customer was asked to pay.</summary>
    public required decimal GrossTotal { get; init; }

    /// <summary>
    /// True when the document reduces what the customer owes, which only a credit note does.
    /// <para>
    /// It decides whether each line is written as a debit or a credit, and that single letter is
    /// the difference between a month's credit notes reducing the reported turnover and doubling
    /// it.
    /// </para>
    /// </summary>
    public bool IsCreditNote => string.Equals(InvoiceType, "NC", StringComparison.Ordinal);
}

/// <summary>One line of a document.</summary>
/// <param name="LineNumber">Its position, from 1.</param>
/// <param name="ProductCode">The SKU.</param>
/// <param name="ProductDescription">The description.</param>
/// <param name="Quantity">How much was sold.</param>
/// <param name="UnitOfMeasure">The unit.</param>
/// <param name="UnitPrice">The price per unit, before discount.</param>
/// <param name="TaxPointDate">The date the tax became due, which is the document date.</param>
/// <param name="Reference">
/// The document this line reverses, on a credit note. Null on everything else. The schema puts it
/// between the tax point date and the description, and a credit note without it is a credit
/// against nothing in particular as far as an auditor is concerned.
/// </param>
/// <param name="ReferenceReason">Why the credit was raised, alongside the reference.</param>
/// <param name="NetAmount">What the line is worth before VAT, after discount.</param>
/// <param name="TaxCountryRegion">PT, PT-AC or PT-MA.</param>
/// <param name="TaxCode">ISE, RED, INT or NOR.</param>
/// <param name="TaxPercentage">The rate applied.</param>
/// <param name="TaxExemptionReason">The legal basis, when the line is exempt.</param>
/// <param name="TaxExemptionCode">The tax authority's code, when the line is exempt.</param>
public sealed record SaftInvoiceLine(
    int LineNumber,
    string ProductCode,
    string ProductDescription,
    decimal Quantity,
    string UnitOfMeasure,
    decimal UnitPrice,
    DateOnly TaxPointDate,
    string? Reference,
    string? ReferenceReason,
    decimal NetAmount,
    string TaxCountryRegion,
    string TaxCode,
    decimal TaxPercentage,
    string? TaxExemptionReason,
    string? TaxExemptionCode);
