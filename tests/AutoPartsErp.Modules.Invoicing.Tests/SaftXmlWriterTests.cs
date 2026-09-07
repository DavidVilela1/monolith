using System.Xml.Linq;
using AutoPartsErp.Modules.Invoicing.Application.Saft;

namespace AutoPartsErp.Modules.Invoicing.Tests;

/// <summary>
/// The SAF-T file, element by element.
/// <para>
/// Worth testing this closely because of how the tax authority's validator fails. The XSD declares
/// sequences, so an element in the wrong place is rejected with a message about an unexpected
/// element — and nothing in that message says which of the two neighbours is actually misplaced.
/// A wrong figure is worse: the file is accepted and the mistake surfaces months later as a
/// discrepancy nobody can trace back to a line of code.
/// </para>
/// </summary>
public sealed class SaftXmlWriterTests
{
    private static readonly XNamespace Ns = SaftXmlWriter.Namespace;

    [Fact]
    public void The_file_declares_the_schema_the_validator_expects()
    {
        string xml = SaftXmlWriter.Write(Sample());

        xml.Should().StartWith("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");

        XElement root = Parse(xml);
        root.Name.Should().Be(Ns + "AuditFile");
        root.Element(Ns + "Header")!.Element(Ns + "AuditFileVersion")!.Value.Should().Be("1.04_01");
    }

    /// <summary>
    /// The three top-level blocks, in the order the schema declares them. Getting this wrong is
    /// the cheapest possible way to have a whole month's filing bounce.
    /// </summary>
    [Fact]
    public void The_three_top_level_blocks_come_in_schema_order()
    {
        XElement root = Parse(SaftXmlWriter.Write(Sample()));

        Names(root).Should().Equal("Header", "MasterFiles", "SourceDocuments");
    }

    [Fact]
    public void The_header_names_the_company_the_period_and_the_program_in_order()
    {
        XElement header = Parse(SaftXmlWriter.Write(Sample())).Element(Ns + "Header")!;

        Names(header).Should().Equal(
            "AuditFileVersion",
            "CompanyID",
            "TaxRegistrationNumber",
            "TaxAccountingBasis",
            "CompanyName",
            "CompanyAddress",
            "FiscalYear",
            "StartDate",
            "EndDate",
            "CurrencyCode",
            "DateCreated",
            "TaxEntity",
            "ProductCompanyTaxID",
            "SoftwareCertificateNumber",
            "ProductID",
            "ProductVersion");

        header.Element(Ns + "TaxAccountingBasis")!.Value.Should().Be("F");
        header.Element(Ns + "TaxEntity")!.Value.Should().Be("Global");
        header.Element(Ns + "StartDate")!.Value.Should().Be("2026-09-01");
        header.Element(Ns + "EndDate")!.Value.Should().Be("2026-09-30");
    }

    /// <summary>The company's own address is fixed to PT by the schema; a customer's is not.</summary>
    [Fact]
    public void The_company_address_is_always_portuguese()
    {
        XElement address = Parse(SaftXmlWriter.Write(Sample()))
            .Element(Ns + "Header")!
            .Element(Ns + "CompanyAddress")!;

        Names(address).Should().Equal("AddressDetail", "City", "PostalCode", "Country");
        address.Element(Ns + "Country")!.Value.Should().Be("PT");
    }

    [Fact]
    public void The_invoice_elements_come_in_schema_order()
    {
        XElement invoice = FirstInvoice(Sample());

        Names(invoice).Should().Equal(
            "InvoiceNo",
            "ATCUD",
            "DocumentStatus",
            "Hash",
            "HashControl",
            "Period",
            "InvoiceDate",
            "InvoiceType",
            "SpecialRegimes",
            "SourceID",
            "SystemEntryDate",
            "CustomerID",
            "Line",
            "DocumentTotals");
    }

    [Fact]
    public void A_line_comes_in_schema_order_and_is_a_credit_on_an_invoice()
    {
        XElement line = FirstInvoice(Sample()).Element(Ns + "Line")!;

        Names(line).Should().Equal(
            "LineNumber",
            "ProductCode",
            "ProductDescription",
            "Quantity",
            "UnitOfMeasure",
            "UnitPrice",
            "TaxPointDate",
            "Description",
            "CreditAmount",
            "Tax");

        Names(line.Element(Ns + "Tax")!).Should().Equal(
            "TaxType", "TaxCountryRegion", "TaxCode", "TaxPercentage");

        line.Element(Ns + "Tax")!.Element(Ns + "TaxType")!.Value.Should().Be("IVA");
    }

    /// <summary>
    /// One letter, and it decides whether a month's credit notes reduce the reported turnover or
    /// double it.
    /// </summary>
    [Fact]
    public void A_credit_notes_lines_are_debits_and_it_totals_on_the_other_side()
    {
        SaftAuditFile file = Sample(type: "NC");
        XElement line = FirstInvoice(file).Element(Ns + "Line")!;

        line.Element(Ns + "DebitAmount").Should().NotBeNull();
        line.Element(Ns + "CreditAmount").Should().BeNull();

        XElement sales = Parse(SaftXmlWriter.Write(file))
            .Element(Ns + "SourceDocuments")!
            .Element(Ns + "SalesInvoices")!;

        sales.Element(Ns + "TotalDebit")!.Value.Should().Be("220.50");
        sales.Element(Ns + "TotalCredit")!.Value.Should().Be("0.00");
    }

    /// <summary>
    /// What ties the reversal to the document being reversed. Without it an auditor sees a credit
    /// against nothing in particular.
    /// </summary>
    [Fact]
    public void A_credit_notes_lines_reference_the_document_they_reverse()
    {
        SaftAuditFile file = Sample(
            type: "NC",
            line: RatedLine(220.50m, 24.50m, 10m, "FT SERIE2026/35", "Goods returned"));

        XElement line = FirstInvoice(file).Element(Ns + "Line")!;

        // Between the tax point date and the description, which is where the schema puts it.
        Names(line).Should().Equal(
            "LineNumber",
            "ProductCode",
            "ProductDescription",
            "Quantity",
            "UnitOfMeasure",
            "UnitPrice",
            "TaxPointDate",
            "References",
            "Description",
            "DebitAmount",
            "Tax");

        XElement references = line.Element(Ns + "References")!;
        Names(references).Should().Equal("Reference", "Reason");
        references.Element(Ns + "Reference")!.Value.Should().Be("FT SERIE2026/35");
        references.Element(Ns + "Reason")!.Value.Should().Be("Goods returned");
    }

    [Fact]
    public void An_invoice_carries_no_references_block()
    {
        FirstInvoice(Sample()).Element(Ns + "Line")!
            .Element(Ns + "References").Should().BeNull();
    }

    [Fact]
    public void An_invoices_lines_total_as_credits()
    {
        XElement sales = Parse(SaftXmlWriter.Write(Sample()))
            .Element(Ns + "SourceDocuments")!
            .Element(Ns + "SalesInvoices")!;

        Names(sales).Should().Equal("NumberOfEntries", "TotalDebit", "TotalCredit", "Invoice");

        sales.Element(Ns + "NumberOfEntries")!.Value.Should().Be("1");
        sales.Element(Ns + "TotalDebit")!.Value.Should().Be("0.00");
        sales.Element(Ns + "TotalCredit")!.Value.Should().Be("220.50");
    }

    /// <summary>
    /// Both are mandatory on an exempt line and must not appear on a rated one. An exempt line
    /// without them is the single most common reason a file comes back rejected.
    /// </summary>
    [Fact]
    public void An_exempt_line_carries_its_reason_and_its_code()
    {
        SaftAuditFile file = Sample(line: ExemptLine());
        XElement line = FirstInvoice(file).Element(Ns + "Line")!;

        line.Element(Ns + "TaxExemptionReason")!.Value.Should().Be("Isento artigo 9.o do CIVA");
        line.Element(Ns + "TaxExemptionCode")!.Value.Should().Be("M07");
    }

    [Fact]
    public void A_rated_line_carries_neither()
    {
        XElement line = FirstInvoice(Sample()).Element(Ns + "Line")!;

        line.Element(Ns + "TaxExemptionReason").Should().BeNull();
        line.Element(Ns + "TaxExemptionCode").Should().BeNull();
    }

    /// <summary>A voided document keeps everything and gains a status and a reason.</summary>
    [Fact]
    public void A_voided_document_says_so_and_says_why()
    {
        SaftAuditFile file = Sample(status: "A", statusReason: "Raised against the wrong account");
        XElement status = FirstInvoice(file).Element(Ns + "DocumentStatus")!;

        Names(status).Should().Equal(
            "InvoiceStatus", "InvoiceStatusDate", "Reason", "SourceID", "SourceBilling");

        status.Element(Ns + "InvoiceStatus")!.Value.Should().Be("A");
        status.Element(Ns + "Reason")!.Value.Should().Be("Raised against the wrong account");
        status.Element(Ns + "SourceBilling")!.Value.Should().Be("P");
    }

    /// <summary>Reason is optional in the schema and meaningless on a live document.</summary>
    [Fact]
    public void A_live_document_carries_no_void_reason()
    {
        XElement status = FirstInvoice(Sample()).Element(Ns + "DocumentStatus")!;

        Names(status).Should().Equal(
            "InvoiceStatus", "InvoiceStatusDate", "SourceID", "SourceBilling");

        status.Element(Ns + "InvoiceStatus")!.Value.Should().Be("N");
    }

    /// <summary>
    /// Two decimals, a dot, no thousands separator — the same shape and the same banker's rounding
    /// as the string that gets signed. A file whose totals disagree with the signature by a cent
    /// is a file whose every document fails verification.
    /// </summary>
    [Fact]
    public void Money_is_written_the_way_the_signature_was_computed()
    {
        SaftAuditFile file = Sample(net: 1234.565m, unitPrice: 1234.565m);
        XElement line = FirstInvoice(file).Element(Ns + "Line")!;

        // 1234.565 to two places is 1234.56 under banker's rounding, not 1234.57.
        line.Element(Ns + "CreditAmount")!.Value.Should().Be("1234.56");
        line.Element(Ns + "UnitPrice")!.Value.Should().Be("1234.56");
    }

    /// <summary>Half a metre of hose is a real line, and two decimals would change what was sold.</summary>
    [Fact]
    public void Quantities_keep_more_precision_than_money()
    {
        SaftAuditFile file = Sample(quantity: 0.5m);

        FirstInvoice(file).Element(Ns + "Line")!
            .Element(Ns + "Quantity")!.Value.Should().Be("0.5");
    }

    [Fact]
    public void Timestamps_have_no_offset_and_no_fraction()
    {
        XElement invoice = FirstInvoice(Sample());

        invoice.Element(Ns + "SystemEntryDate")!.Value.Should().Be("2026-09-07T09:30:00");
        invoice.Element(Ns + "InvoiceDate")!.Value.Should().Be("2026-09-07");
        invoice.Element(Ns + "Period")!.Value.Should().Be("9");
    }

    /// <summary>
    /// The address elements are mandatory, so a customer with none on file would fail the file
    /// outright. The tax authority's own guidance is to write this word rather than omit them.
    /// </summary>
    [Fact]
    public void A_customer_with_nothing_on_file_gets_the_tax_authoritys_placeholder()
    {
        SaftAuditFile file = Sample(customer: new SaftCustomer(
            "11111111-1111-1111-1111-111111111111",
            SaftXmlWriter.Unknown,
            SaftXmlWriter.FinalConsumerTaxId,
            "Consumidor final",
            "  ",
            "",
            null!,
            "PT"));

        XElement address = Parse(SaftXmlWriter.Write(file))
            .Element(Ns + "MasterFiles")!
            .Element(Ns + "Customer")!
            .Element(Ns + "BillingAddress")!;

        address.Element(Ns + "AddressDetail")!.Value.Should().Be("Desconhecido");
        address.Element(Ns + "City")!.Value.Should().Be("Desconhecido");
        address.Element(Ns + "PostalCode")!.Value.Should().Be("Desconhecido");
        address.Element(Ns + "Country")!.Value.Should().Be("PT");
    }

    [Fact]
    public void The_master_files_come_in_schema_order()
    {
        XElement master = Parse(SaftXmlWriter.Write(Sample())).Element(Ns + "MasterFiles")!;

        Names(master).Should().Equal("Customer", "Product", "TaxTable");

        Names(master.Element(Ns + "Customer")!).Should().Equal(
            "CustomerID",
            "AccountID",
            "CustomerTaxID",
            "CompanyName",
            "BillingAddress",
            "SelfBillingIndicator");

        Names(master.Element(Ns + "Product")!).Should().Equal(
            "ProductType", "ProductCode", "ProductDescription", "ProductNumberCode");

        Names(master.Element(Ns + "TaxTable")!.Element(Ns + "TaxTableEntry")!).Should().Equal(
            "TaxType", "TaxCountryRegion", "TaxCode", "Description", "TaxPercentage");
    }

    [Fact]
    public void The_document_totals_come_in_schema_order()
    {
        XElement totals = FirstInvoice(Sample()).Element(Ns + "DocumentTotals")!;

        Names(totals).Should().Equal("TaxPayable", "NetTotal", "GrossTotal");

        totals.Element(Ns + "TaxPayable")!.Value.Should().Be("50.72");
        totals.Element(Ns + "NetTotal")!.Value.Should().Be("220.50");
        totals.Element(Ns + "GrossTotal")!.Value.Should().Be("271.22");
    }

    /// <summary>An empty month is a real filing, and it has to produce a valid file.</summary>
    [Fact]
    public void A_month_with_no_documents_still_produces_a_file()
    {
        var file = new SaftAuditFile
        {
            Header = Header(),
            Customers = [],
            Products = [],
            TaxTable = [],
            Invoices = [],
        };

        XElement sales = Parse(SaftXmlWriter.Write(file))
            .Element(Ns + "SourceDocuments")!
            .Element(Ns + "SalesInvoices")!;

        sales.Element(Ns + "NumberOfEntries")!.Value.Should().Be("0");
        sales.Element(Ns + "TotalDebit")!.Value.Should().Be("0.00");
        sales.Element(Ns + "TotalCredit")!.Value.Should().Be("0.00");
    }

    private static XElement Parse(string xml) => XDocument.Parse(xml).Root!;

    private static IEnumerable<string> Names(XElement element) =>
        element.Elements().Select(child => child.Name.LocalName);

    private static XElement FirstInvoice(SaftAuditFile file) =>
        Parse(SaftXmlWriter.Write(file))
            .Element(Ns + "SourceDocuments")!
            .Element(Ns + "SalesInvoices")!
            .Element(Ns + "Invoice")!;

    private static SaftHeader Header() => new()
    {
        CompanyId = "Lisboa 12345",
        TaxRegistrationNumber = "501234567",
        CompanyName = "AutoPecas Central, Lda.",
        AddressDetail = "Rua das Oficinas 12",
        City = "Lisboa",
        PostalCode = "1000-001",
        FiscalYear = 2026,
        StartDate = new DateOnly(2026, 9, 1),
        EndDate = new DateOnly(2026, 9, 30),
        DateCreated = new DateOnly(2026, 10, 1),
        ProductCompanyTaxId = "501234567",
        SoftwareCertificateNumber = "0",
        ProductId = "AutoPartsErp/AutoPartsErp",
        ProductVersion = "1.0",
    };

    private static SaftInvoiceLine RatedLine(
        decimal net,
        decimal unitPrice,
        decimal quantity,
        string? reference = null,
        string? reason = null) => new(
        1,
        "BP-1188",
        "Brake pad set, front axle",
        quantity,
        "UN",
        unitPrice,
        new DateOnly(2026, 9, 7),
        reference,
        reason,
        net,
        "PT",
        "NOR",
        23m,
        null,
        null);

    private static SaftInvoiceLine ExemptLine() => new(
        1,
        "BP-1188",
        "Brake pad set, front axle",
        1m,
        "UN",
        220.50m,
        new DateOnly(2026, 9, 7),
        null,
        null,
        220.50m,
        "PT",
        "ISE",
        0m,
        "Isento artigo 9.o do CIVA",
        "M07");

    private static SaftAuditFile Sample(
        string type = "FT",
        string status = "N",
        string? statusReason = null,
        decimal net = 220.50m,
        decimal unitPrice = 24.50m,
        decimal quantity = 10m,
        SaftInvoiceLine? line = null,
        SaftCustomer? customer = null)
    {
        SaftInvoiceLine only = line ?? RatedLine(net, unitPrice, quantity);

        var invoice = new SaftInvoice
        {
            InvoiceNo = "FT SERIE2026/35",
            Atcud = "CSDF7T5H-35",
            Status = status,
            StatusDate = new DateTimeOffset(2026, 9, 7, 9, 30, 0, TimeSpan.Zero),
            StatusReason = statusReason,
            SourceId = "miguel",
            Hash = "kLp0abc...",
            HashControl = "1",
            Period = 9,
            InvoiceDate = new DateOnly(2026, 9, 7),
            InvoiceType = type,
            SystemEntryDate = new DateTimeOffset(2026, 9, 7, 9, 30, 0, TimeSpan.Zero),
            CustomerId = "11111111-1111-1111-1111-111111111111",
            Lines = [only],
            TaxPayable = 50.72m,
            NetTotal = 220.50m,
            GrossTotal = 271.22m,
        };

        return new SaftAuditFile
        {
            Header = Header(),
            Customers =
            [
                customer ?? new SaftCustomer(
                    "11111111-1111-1111-1111-111111111111",
                    SaftXmlWriter.Unknown,
                    "501234567",
                    "Oficina Central, Lda.",
                    "Rua do Norte 3",
                    "Porto",
                    "4000-002",
                    "PT"),
            ],
            Products = [new SaftProduct("P", "BP-1188", "Brake pad set, front axle", "BP-1188")],
            TaxTable = [new SaftTaxTableEntry("IVA", "PT", "NOR", "Taxa normal 23%", 23m)],
            Invoices = [invoice],
        };
    }
}
