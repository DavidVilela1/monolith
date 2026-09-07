using System.Globalization;
using AutoPartsErp.ModuleContracts.Partners;
using AutoPartsErp.Modules.Invoicing.Application.Abstractions;
using AutoPartsErp.Modules.Invoicing.Application.Options;
using AutoPartsErp.Modules.Invoicing.Domain;
using AutoPartsErp.SharedKernel.Abstractions;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Invoicing.Application.Saft;

/// <summary>
/// One document as the export reads it out of the database, before the master files exist.
/// <para>
/// It carries the customer's name and tax number as the <i>document</i> recorded them, which is
/// not the same thing as the customer master record and is needed for the case where the two
/// disagree: a customer deleted from Partners after being invoiced still has to appear in the
/// file, because a line references them.
/// </para>
/// </summary>
/// <param name="Invoice">The document itself.</param>
/// <param name="CustomerName">Their name, as the document recorded it.</param>
/// <param name="CustomerTaxNumber">Their NIF, as the document recorded it, or null.</param>
public sealed record SaftDocument(
    SaftInvoice Invoice,
    string CustomerName,
    string? CustomerTaxNumber);

/// <summary>
/// Produces the SAF-T (PT) file for a period.
/// <para>
/// A query rather than a command, and it changes nothing: the file is a rendering of documents
/// that already exist. Producing it twice produces the same bytes, which matters, because the
/// first thing anybody does with a rejected file is fix something and produce it again.
/// </para>
/// </summary>
/// <param name="From">The first day to include.</param>
/// <param name="To">The last day to include.</param>
public sealed record ExportSaftQuery(DateOnly From, DateOnly To) : IQuery<SaftExport>;

/// <summary>The finished file and what to call it.</summary>
/// <param name="FileName">The conventional name, which the tax authority's portal expects.</param>
/// <param name="Xml">The file.</param>
/// <param name="DocumentCount">How many documents are in it, for a caller that wants to say so.</param>
public sealed record SaftExport(string FileName, string Xml, int DocumentCount);

/// <summary>Builds the file.</summary>
public sealed class ExportSaftQueryHandler : IQueryHandler<ExportSaftQuery, SaftExport>
{
    private readonly IInvoicingReadStore _readStore;
    private readonly IBillingPartyDirectory _customers;
    private readonly IDateTimeProvider _clock;
    private readonly InvoicingOptions _options;

    /// <summary>Initializes the handler.</summary>
    public ExportSaftQueryHandler(
        IInvoicingReadStore readStore,
        IBillingPartyDirectory customers,
        IDateTimeProvider clock,
        InvoicingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _readStore = readStore;
        _customers = customers;
        _clock = clock;
        _options = options;
    }

    /// <inheritdoc />
    public async Task<Result<SaftExport>> HandleAsync(
        ExportSaftQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.To < request.From)
        {
            return Result.Failure<SaftExport>(InvoicingErrors.Saft.PeriodBackwards);
        }

        // A file has one fiscal year in its header, so a period crossing new year would need two
        // files. Refusing is better than quietly reporting December under the wrong year.
        if (request.From.Year != request.To.Year)
        {
            return Result.Failure<SaftExport>(InvoicingErrors.Saft.PeriodSpansYears);
        }

        // Checked here rather than at startup, deliberately. Everything else this module does
        // works without knowing the company's registered address, so refusing to boot over it
        // would stop somebody invoicing for the sake of a file they may not produce for a month.
        // The moment they do ask for it, they are told exactly which setting is missing.
        if (string.IsNullOrWhiteSpace(_options.Company.Name))
        {
            return Result.Failure<SaftExport>(
                InvoicingErrors.Saft.CompanyNotConfigured("Company:Name"));
        }

        if (string.IsNullOrWhiteSpace(_options.Product.CompanyTaxId))
        {
            return Result.Failure<SaftExport>(
                InvoicingErrors.Saft.CompanyNotConfigured("Product:CompanyTaxId"));
        }

        IReadOnlyList<SaftDocument> documents = await _readStore
            .GetSaftDocumentsAsync(request.From, request.To, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<SaftCustomer> customers =
            await BuildCustomersAsync(documents, cancellationToken).ConfigureAwait(false);

        var file = new SaftAuditFile
        {
            Header = BuildHeader(request),
            Customers = customers,
            Products = BuildProducts(documents),
            TaxTable = BuildTaxTable(documents),
            // The key version is stamped on here rather than read from the database, because it
            // is configuration and not a property of the document. Every document in one file was
            // signed by the same key that this deployment is holding right now.
            Invoices = [.. documents.Select(document =>
                document.Invoice with { HashControl = _options.PrivateKeyVersion })],
        };

        string name = string.Create(
            CultureInfo.InvariantCulture,
            $"SAFT-PT_{_options.IssuerTaxNumber}_{request.From:yyyyMMdd}_{request.To:yyyyMMdd}.xml");

        return new SaftExport(name, SaftXmlWriter.Write(file), documents.Count);
    }

    private SaftHeader BuildHeader(ExportSaftQuery request) => new()
    {
        // The registry number when there is one, the tax number when there is not — which is what
        // the tax authority accepts from a sole trader with no registration to quote.
        CompanyId = string.IsNullOrWhiteSpace(_options.Company.CompanyId)
            ? _options.IssuerTaxNumber
            : _options.Company.CompanyId,
        TaxRegistrationNumber = _options.IssuerTaxNumber,
        CompanyName = _options.Company.Name,
        BusinessName = _options.Company.BusinessName,
        AddressDetail = _options.Company.AddressDetail,
        City = _options.Company.City,
        PostalCode = _options.Company.PostalCode,
        FiscalYear = request.From.Year,
        StartDate = request.From,
        EndDate = request.To,
        DateCreated = _clock.TodayUtc,
        ProductCompanyTaxId = _options.Product.CompanyTaxId,
        SoftwareCertificateNumber = _options.CertificateNumber,
        ProductId = _options.Product.ProductId,
        ProductVersion = _options.Product.Version,
    };

    private async Task<IReadOnlyList<SaftCustomer>> BuildCustomersAsync(
        IReadOnlyList<SaftDocument> documents,
        CancellationToken cancellationToken)
    {
        List<string> ids = [.. documents
            .Select(document => document.Invoice.CustomerId)
            .Distinct(StringComparer.Ordinal)];

        List<Guid> guids = [.. ids
            .Select(id => Guid.TryParse(id, out Guid parsed) ? parsed : Guid.Empty)
            .Where(id => id != Guid.Empty)];

        IReadOnlyDictionary<Guid, BillingParty> known =
            await _customers.GetManyAsync(guids, cancellationToken).ConfigureAwait(false);

        var customers = new List<SaftCustomer>(ids.Count);

        foreach (string id in ids)
        {
            SaftDocument first = documents.First(document =>
                string.Equals(document.Invoice.CustomerId, id, StringComparison.Ordinal));

            BillingParty? party =
                Guid.TryParse(id, out Guid guid) && known.TryGetValue(guid, out BillingParty? found)
                    ? found
                    : null;

            // Partners is the master record and wins where it exists. Where it does not — a
            // customer deleted after being invoiced — the document's own snapshot stands in,
            // because a line references this identifier and a file missing the customer it
            // references is rejected as inconsistent.
            BillingPartyAddress? address = party?.BillingAddress;

            customers.Add(new SaftCustomer(
                id,

                // No chart of accounts in an invoicing-only file, and the element is mandatory.
                SaftXmlWriter.Unknown,
                party?.TaxNumber ?? first.CustomerTaxNumber ?? SaftXmlWriter.FinalConsumerTaxId,
                party?.LegalName ?? first.CustomerName,
                address?.AddressDetail ?? SaftXmlWriter.Unknown,
                address?.City ?? SaftXmlWriter.Unknown,
                address?.PostalCode ?? SaftXmlWriter.Unknown,
                address?.CountryCode ?? SaftXmlWriter.Unknown));
        }

        return customers;
    }

    // Built from the lines rather than from the catalogue, and that is the safer direction. A part
    // withdrawn or renamed since it was sold still has to appear here exactly as the document
    // names it, or the file references a product it does not declare.
    private static IReadOnlyList<SaftProduct> BuildProducts(IReadOnlyList<SaftDocument> documents) =>
    [
        .. documents
            .SelectMany(document => document.Invoice.Lines)
            .GroupBy(line => line.ProductCode, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new SaftProduct(
                // P, because everything a parts distributor puts on a line is a physical thing.
                "P",
                group.Key,
                group.First().ProductDescription,
                group.Key)),
    ];

    private static IReadOnlyList<SaftTaxTableEntry> BuildTaxTable(
        IReadOnlyList<SaftDocument> documents) =>
    [
        .. documents
            .SelectMany(document => document.Invoice.Lines)
            .GroupBy(line => (line.TaxCountryRegion, line.TaxCode, line.TaxPercentage))
            .OrderBy(group => group.Key.TaxCountryRegion, StringComparer.Ordinal)
            .ThenBy(group => group.Key.TaxPercentage)
            .Select(group => new SaftTaxTableEntry(
                "IVA",
                group.Key.TaxCountryRegion,
                group.Key.TaxCode,
                Describe(group.Key.TaxCode, group.Key.TaxPercentage),
                group.Key.TaxPercentage)),
    ];

    private static string Describe(string taxCode, decimal percent) => taxCode switch
    {
        "ISE" => "Isento",
        "RED" => string.Create(CultureInfo.InvariantCulture, $"Taxa reduzida {percent:0.##}%"),
        "INT" => string.Create(CultureInfo.InvariantCulture, $"Taxa intermédia {percent:0.##}%"),
        "NOR" => string.Create(CultureInfo.InvariantCulture, $"Taxa normal {percent:0.##}%"),
        _ => taxCode,
    };
}
