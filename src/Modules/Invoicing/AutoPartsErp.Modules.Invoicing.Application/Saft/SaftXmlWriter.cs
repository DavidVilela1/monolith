using System.Globalization;
using System.Xml.Linq;

namespace AutoPartsErp.Modules.Invoicing.Application.Saft;

/// <summary>
/// Writes a <see cref="SaftAuditFile"/> as the XML the tax authority validates.
/// <para>
/// A pure function: an audit file in, a string out, no database and no clock. That is deliberate,
/// because this is the one part of the export that can be tested exhaustively — every element
/// name, every ordering, every rounded figure — and a version of it that reached into a
/// repository could not be.
/// </para>
/// <para>
/// The order of the elements is not a style choice. The XSD declares them as sequences, so a file
/// with <c>Period</c> before <c>InvoiceDate</c> is rejected outright with a message about
/// unexpected elements, and nothing about that message says which of the two is in the wrong
/// place. Each block below is written in schema order and left in schema order.
/// </para>
/// </summary>
public static class SaftXmlWriter
{
    /// <summary>The namespace every element lives in.</summary>
    public const string Namespace = "urn:OECD:StandardAuditFile-Tax:PT_1.04_01";

    /// <summary>The version this writer produces.</summary>
    public const string AuditFileVersion = "1.04_01";

    /// <summary>
    /// The tax authority's placeholder for information that is genuinely not held.
    /// <para>
    /// It is not a shrug. The address elements are mandatory, so a customer with none on file
    /// would fail the file outright; the AT's own guidance is to write this word rather than omit
    /// the element, and a file that says "unknown" is a file that can be filed.
    /// </para>
    /// </summary>
    public const string Unknown = "Desconhecido";

    /// <summary>The NIF that stands for a customer who was not identified.</summary>
    public const string FinalConsumerTaxId = "999999990";

    /// <summary>
    /// <c>F</c> — a file produced by billing software, carrying documents and no accounts.
    /// </summary>
    private const string InvoicingOnlyBasis = "F";

    /// <summary>Writes the file.</summary>
    /// <param name="file">What to write.</param>
    /// <returns>The XML, as a UTF-8 declared document.</returns>
    public static string Write(SaftAuditFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        XNamespace ns = Namespace;

        var document = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(
                ns + "AuditFile",
                WriteHeader(ns, file.Header),
                WriteMasterFiles(ns, file),
                WriteSourceDocuments(ns, file)));

        // The declaration is written by hand because XDocument.ToString() drops it, and the
        // validator wants the encoding stated. Saving through a writer would keep it, at the cost
        // of a stream this method does not otherwise need.
        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + Environment.NewLine + document;
    }

    private static XElement WriteHeader(XNamespace ns, SaftHeader header) =>
        new(
            ns + "Header",
            new XElement(ns + "AuditFileVersion", AuditFileVersion),
            new XElement(ns + "CompanyID", header.CompanyId),
            new XElement(ns + "TaxRegistrationNumber", header.TaxRegistrationNumber),
            new XElement(ns + "TaxAccountingBasis", InvoicingOnlyBasis),
            new XElement(ns + "CompanyName", header.CompanyName),
            Optional(ns, "BusinessName", header.BusinessName),
            new XElement(
                ns + "CompanyAddress",
                new XElement(ns + "AddressDetail", Text(header.AddressDetail)),
                new XElement(ns + "City", Text(header.City)),
                new XElement(ns + "PostalCode", Text(header.PostalCode)),

                // Fixed to PT by the schema for the company's own address, unlike a customer's.
                new XElement(ns + "Country", "PT")),
            new XElement(ns + "FiscalYear", Number(header.FiscalYear)),
            new XElement(ns + "StartDate", Date(header.StartDate)),
            new XElement(ns + "EndDate", Date(header.EndDate)),

            // The currency of the file, which is not the currency of any one document. A document
            // in sterling still reports its totals here in euro; nothing in this module issues one
            // yet, and when it does this is the line that will need company.
            new XElement(ns + "CurrencyCode", "EUR"),
            new XElement(ns + "DateCreated", Date(header.DateCreated)),

            // "Global" is what an invoicing file says: the documents are the company's, not one
            // establishment's. A per-establishment file would name the establishment here.
            new XElement(ns + "TaxEntity", "Global"),
            new XElement(ns + "ProductCompanyTaxID", header.ProductCompanyTaxId),
            new XElement(ns + "SoftwareCertificateNumber", header.SoftwareCertificateNumber),
            new XElement(ns + "ProductID", header.ProductId),
            new XElement(ns + "ProductVersion", header.ProductVersion));

    private static XElement WriteMasterFiles(XNamespace ns, SaftAuditFile file)
    {
        var master = new XElement(ns + "MasterFiles");

        foreach (SaftCustomer customer in file.Customers)
        {
            master.Add(new XElement(
                ns + "Customer",
                new XElement(ns + "CustomerID", customer.CustomerId),
                new XElement(ns + "AccountID", customer.AccountId),
                new XElement(ns + "CustomerTaxID", customer.CustomerTaxId),
                new XElement(ns + "CompanyName", customer.CompanyName),
                new XElement(
                    ns + "BillingAddress",
                    new XElement(ns + "AddressDetail", Text(customer.AddressDetail)),
                    new XElement(ns + "City", Text(customer.City)),
                    new XElement(ns + "PostalCode", Text(customer.PostalCode)),
                    new XElement(ns + "Country", Text(customer.Country))),

                // 0 means we do not issue documents on this customer's behalf. Self-billing is a
                // written agreement between two companies, not something a flag turns on.
                new XElement(ns + "SelfBillingIndicator", "0")));
        }

        foreach (SaftProduct product in file.Products)
        {
            master.Add(new XElement(
                ns + "Product",
                new XElement(ns + "ProductType", product.ProductType),
                new XElement(ns + "ProductCode", product.ProductCode),
                new XElement(ns + "ProductDescription", product.ProductDescription),
                new XElement(ns + "ProductNumberCode", product.ProductNumberCode)));
        }

        var taxTable = new XElement(ns + "TaxTable");

        foreach (SaftTaxTableEntry entry in file.TaxTable)
        {
            taxTable.Add(new XElement(
                ns + "TaxTableEntry",
                new XElement(ns + "TaxType", entry.TaxType),
                new XElement(ns + "TaxCountryRegion", entry.TaxCountryRegion),
                new XElement(ns + "TaxCode", entry.TaxCode),
                new XElement(ns + "Description", entry.Description),
                new XElement(ns + "TaxPercentage", Amount(entry.TaxPercentage))));
        }

        master.Add(taxTable);

        return master;
    }

    private static XElement WriteSourceDocuments(XNamespace ns, SaftAuditFile file)
    {
        var sales = new XElement(
            ns + "SalesInvoices",
            new XElement(ns + "NumberOfEntries", Number(file.Invoices.Count)),
            new XElement(ns + "TotalDebit", Amount(file.TotalDebit)),
            new XElement(ns + "TotalCredit", Amount(file.TotalCredit)));

        foreach (SaftInvoice invoice in file.Invoices)
        {
            sales.Add(WriteInvoice(ns, invoice));
        }

        return new XElement(ns + "SourceDocuments", sales);
    }

    private static XElement WriteInvoice(XNamespace ns, SaftInvoice invoice)
    {
        var element = new XElement(
            ns + "InvoiceNo", invoice.InvoiceNo);

        var document = new XElement(
            ns + "Invoice",
            element,
            new XElement(ns + "ATCUD", invoice.Atcud),
            new XElement(
                ns + "DocumentStatus",
                new XElement(ns + "InvoiceStatus", invoice.Status),
                new XElement(ns + "InvoiceStatusDate", Timestamp(invoice.StatusDate)),

                // Mandatory when the status is anything but N, and meaningless otherwise. A
                // voided document without its reason is a file the validator rejects.
                Optional(ns, "Reason", invoice.StatusReason),
                new XElement(ns + "SourceID", invoice.SourceId),

                // P: produced in the program. The alternatives are for documents keyed in from
                // another system or recovered from one, and neither is what this is.
                new XElement(ns + "SourceBilling", "P")),
            new XElement(ns + "Hash", invoice.Hash),
            new XElement(ns + "HashControl", invoice.HashControl),
            new XElement(ns + "Period", Number(invoice.Period)),
            new XElement(ns + "InvoiceDate", Date(invoice.InvoiceDate)),
            new XElement(ns + "InvoiceType", invoice.InvoiceType),
            new XElement(
                ns + "SpecialRegimes",
                new XElement(ns + "SelfBillingIndicator", "0"),
                new XElement(ns + "CashVATSchemeIndicator", "0"),
                new XElement(ns + "ThirdPartiesBillingIndicator", "0")),
            new XElement(ns + "SourceID", invoice.SourceId),
            new XElement(ns + "SystemEntryDate", Timestamp(invoice.SystemEntryDate)),
            new XElement(ns + "CustomerID", invoice.CustomerId));

        foreach (SaftInvoiceLine line in invoice.Lines)
        {
            document.Add(WriteLine(ns, line, invoice.IsCreditNote));
        }

        document.Add(new XElement(
            ns + "DocumentTotals",
            new XElement(ns + "TaxPayable", Amount(invoice.TaxPayable)),
            new XElement(ns + "NetTotal", Amount(invoice.NetTotal)),
            new XElement(ns + "GrossTotal", Amount(invoice.GrossTotal))));

        return document;
    }

    private static XElement WriteLine(XNamespace ns, SaftInvoiceLine line, bool isCreditNote)
    {
        var element = new XElement(
            ns + "Line",
            new XElement(ns + "LineNumber", Number(line.LineNumber)),
            new XElement(ns + "ProductCode", line.ProductCode),
            new XElement(ns + "ProductDescription", line.ProductDescription),
            new XElement(ns + "Quantity", Quantity(line.Quantity)),
            new XElement(ns + "UnitOfMeasure", line.UnitOfMeasure),
            new XElement(ns + "UnitPrice", Amount(line.UnitPrice)),
            new XElement(ns + "TaxPointDate", Date(line.TaxPointDate)),

            // Between the tax point date and the description, which is where the schema puts it.
            // Only a credit note has one, and it is what ties the reversal to the document being
            // reversed - without it an auditor sees a credit against nothing in particular.
            line.Reference is { Length: > 0 } reference
                ? new XElement(
                    ns + "References",
                    new XElement(ns + "Reference", reference),
                    new XElement(ns + "Reason", Text(line.ReferenceReason)))
                : null,
            new XElement(ns + "Description", line.ProductDescription),

            // A credit note's lines are debits and everything else's are credits. One letter, and
            // it decides whether a month's credit notes reduce the reported turnover or double it.
            new XElement(ns + (isCreditNote ? "DebitAmount" : "CreditAmount"), Amount(line.NetAmount)),
            new XElement(
                ns + "Tax",
                new XElement(ns + "TaxType", "IVA"),
                new XElement(ns + "TaxCountryRegion", line.TaxCountryRegion),
                new XElement(ns + "TaxCode", line.TaxCode),
                new XElement(ns + "TaxPercentage", Amount(line.TaxPercentage))));

        // Both are mandatory on an exempt line and forbidden on a rated one. An exempt line
        // without them is the single most common reason a file comes back rejected.
        if (line.TaxPercentage == 0m)
        {
            element.Add(new XElement(ns + "TaxExemptionReason", Text(line.TaxExemptionReason)));
            element.Add(new XElement(ns + "TaxExemptionCode", Text(line.TaxExemptionCode)));
        }

        return element;
    }

    private static XElement? Optional(XNamespace ns, string name, string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : new XElement(ns + name, value);

    private static string Text(string? value) =>
        string.IsNullOrWhiteSpace(value) ? Unknown : value.Trim();

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Date(DateOnly value) =>
        value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Timestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);

    // Two decimals, a dot, no thousands separator, and banker's rounding — the same shape and the
    // same rounding as the string that gets signed. A file whose totals disagree with the
    // signature by a cent is a file whose every document fails verification.
    private static string Amount(decimal value) =>
        Math.Round(value, 2, MidpointRounding.ToEven).ToString("0.00", CultureInfo.InvariantCulture);

    // Quantities carry more precision than money, because half a metre of hose is a real line and
    // rounding it to two decimals would change what was sold.
    private static string Quantity(decimal value) =>
        Math.Round(value, 4, MidpointRounding.ToEven).ToString("0.0###", CultureInfo.InvariantCulture);
}
