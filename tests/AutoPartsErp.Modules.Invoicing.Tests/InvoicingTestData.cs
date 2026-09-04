using AutoPartsErp.Modules.Invoicing.Domain;
using AutoPartsErp.Modules.Invoicing.Domain.Invoices;
using AutoPartsErp.Modules.Invoicing.Domain.Series;
using AutoPartsErp.Modules.Invoicing.Domain.Signing;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Invoicing.Tests;

/// <summary>The scaffolding these tests share.</summary>
internal static class InvoicingTestData
{
    /// <summary>A fixed "today", so nothing here depends on when it is run.</summary>
    public static readonly DateOnly Today = new(2026, 9, 4);

    /// <summary>A fixed moment of entry, in UTC, so the signed string is predictable.</summary>
    public static readonly DateTimeOffset EntryUtc = new(2026, 9, 4, 9, 30, 0, TimeSpan.Zero);

    /// <summary>A series that has been declared to the tax authority and is live.</summary>
    public static DocumentSeries ActiveSeries(
        DocumentType type = DocumentType.Invoice,
        string code = "SERIE2026")
    {
        DocumentSeries series = DocumentSeries.Open(type, code, 2026).Value;
        series.Validate("CSDF7T5H", EntryUtc);
        series.Activate();
        series.ClearDomainEvents();
        return series;
    }

    /// <summary>An unissued invoice for an identified customer.</summary>
    public static Invoice Draft(DocumentType type = DocumentType.Invoice, string? customerNif = "501234567") =>
        Invoice.Draft(
            type,
            new CustomerRef(Guid.NewGuid()),
            "Garagem Central, Lda.",
            customerNif,
            "PT",
            Currency.Eur,
            TaxRegion.Mainland,
            Today).Value;

    /// <summary>Adds a standard-rated line worth <paramref name="unitPrice"/> each.</summary>
    public static void AddLine(Invoice invoice, decimal quantity = 1m, decimal unitPrice = 100m)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        invoice.AddLine(
            new PartRef(Guid.NewGuid()),
            "BP-1188",
            "Brake pad set, front axle",
            Quantity.Of(quantity, UnitOfMeasure.Each),
            Money.Of(unitPrice, Currency.Eur),
            0m,
            VatRate.PortugalStandard);
    }

    /// <summary>
    /// A base64 string shaped like a real signature, with known characters at the four positions
    /// the legislation reads — so the printed value is the <c>kLp0</c> the specification's own
    /// worked example shows in field Q.
    /// </summary>
    public static string PredictableSignature =>
        "k" + new string('A', 9) + "L" + new string('A', 9)
        + "p" + new string('A', 9) + "0" + new string('A', 141);

    /// <summary>A signer that records what it was asked to sign.</summary>
    internal sealed class RecordingSigner : IDocumentSigner
    {
        public string CertificateNumber => "1234";

        public string? LastSource { get; private set; }

        public string Sign(string source)
        {
            LastSource = source;
            return PredictableSignature;
        }
    }
}
