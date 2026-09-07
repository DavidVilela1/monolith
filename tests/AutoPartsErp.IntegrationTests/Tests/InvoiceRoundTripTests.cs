using AutoPartsErp.Modules.Invoicing.Domain;
using AutoPartsErp.Modules.Invoicing.Domain.Invoices;
using AutoPartsErp.Modules.Invoicing.Domain.Series;
using AutoPartsErp.Modules.Invoicing.Domain.Signing;
using AutoPartsErp.Modules.Invoicing.Infrastructure.Persistence;
using AutoPartsErp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AutoPartsErp.IntegrationTests.Tests;

/// <summary>
/// An invoice written to PostgreSQL and read back, field by field.
/// <para>
/// The Invoicing mapping is the densest in the system — two owned single values on the document,
/// three owned values per line, a strongly-typed identifier on almost every column, and a unit of
/// measure that round-trips through a converter with a custom comparer. Every one of those can be
/// wrong in a way that compiles, migrates, and only shows up as a null or a zero after a restart.
/// </para>
/// </summary>
[Collection(ErpCollection.Name)]
public sealed class InvoiceRoundTripTests
{
    private readonly ErpFixture _fixture;

    /// <summary>Initializes the tests.</summary>
    public InvoiceRoundTripTests(ErpFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// Everything that goes onto an issued document survives a round trip through the database.
    /// </summary>
    [Fact]
    public async Task An_issued_document_survives_being_written_and_read_back()
    {
        Guid tenant = Guid.NewGuid();
        Guid partId = Guid.NewGuid();
        Guid customerId = Guid.NewGuid();

        Guid invoiceId = await _fixture.Application.AsTenantAsync(tenant, async provider =>
        {
            var invoices = provider.GetRequiredService<IInvoiceRepository>();
            var seriesRepository = provider.GetRequiredService<IDocumentSeriesRepository>();
            var unitOfWork = provider.GetRequiredService<IInvoicingUnitOfWork>();
            var signer = provider.GetRequiredService<IDocumentSigner>();

            DocumentSeries series = DocumentSeries
                .Open(DocumentType.Invoice, "RT" + Guid.NewGuid().ToString("N")[..6], 2026).Value;

            series.Validate("CSDF7T5H", DateTimeOffset.UtcNow);
            series.Activate();
            seriesRepository.Add(series);

            Invoice invoice = Invoice.Draft(
                DocumentType.Invoice,
                new CustomerRef(customerId),
                "Oficina do Round Trip, Lda.",
                "501234567",
                "PT",
                Currency.Eur,
                TaxRegion.Madeira,
                new DateOnly(2026, 9, 7)).Value;

            // A rated line and an exempt one, because the exempt path carries two extra columns
            // that only exist when it is taken.
            invoice.AddLine(
                new PartRef(partId),
                "BP-1188",
                "Brake pad set, front axle",
                Quantity.Of(2.5m, UnitOfMeasure.Litre),
                Money.Of(24.50m, Currency.Eur),
                10m,
                VatRate.Of(VatCategory.Standard, 22m).Value);

            invoice.AddLine(
                new PartRef(Guid.NewGuid()),
                "SVC-1",
                "Isento",
                Quantity.Each(1),
                Money.Of(50m, Currency.Eur),
                0m,
                VatRate.ExemptWith("M07", "Isento artigo 9.o do CIVA").Value);

            invoice.Issue(series, signer, null, "501234567", DateTimeOffset.UtcNow);

            invoices.Add(invoice);
            await unitOfWork.SaveChangesAsync();

            return invoice.Id.Value;
        });

        // A second scope, so nothing comes back out of the first context's identity map. Reading
        // an entity you just wrote from the same context proves nothing about the mapping.
        await _fixture.Application.AsTenantAsync(tenant, async provider =>
        {
            var context = provider.GetRequiredService<InvoicingDbContext>();
            var id = new InvoiceId(invoiceId);

            Invoice? stored = await context.Invoices
                .AsNoTracking()
                .FirstOrDefaultAsync(invoice => invoice.Id == id);

            stored.Should().NotBeNull();

            stored!.CustomerId.Value.Should().Be(customerId);
            stored.CustomerTaxNumber.Should().Be("501234567");
            stored.TaxRegion.Should().Be(TaxRegion.Madeira);
            stored.CurrencyCode.Should().Be("EUR");
            stored.DocumentDate.Should().Be(new DateOnly(2026, 9, 7));

            // The two owned values on the document. Both are a pair of facts that only mean
            // anything together, which is why they are owned types rather than loose strings.
            stored.Atcud.Should().NotBeNull();
            stored.Atcud!.ValidationCode.Should().Be("CSDF7T5H");
            stored.Atcud.Number.Should().Be(1);

            stored.Signature.Should().NotBeNull();
            stored.Signature!.Value.Should().NotBeEmpty();
            stored.Signature.Printed.Should().HaveLength(4);

            stored.QrCode.Should().NotBeNull();
            stored.SeriesNumber.Should().Be(1);
            stored.SystemEntryDateUtc.Should().NotBeNull();

            IReadOnlyList<InvoiceLine> lines = stored.Lines;
            lines.Should().HaveCount(2);

            InvoiceLine rated = lines[0];
            rated.Sku.Should().Be("BP-1188");
            rated.PartId.Value.Should().Be(partId);

            // The unit round-trips through a converter with a hand-written comparer. A wrong
            // comparer does not fail here; it fails by making EF think nothing ever changed.
            rated.Quantity.Value.Should().Be(2.5m);
            rated.Quantity.Unit.Should().Be(UnitOfMeasure.Litre);

            rated.UnitPrice.Amount.Should().Be(24.50m);
            rated.UnitPrice.Currency.Should().Be(Currency.Eur);
            rated.DiscountPercent.Should().Be(10m);
            rated.VatRate.Category.Should().Be(VatCategory.Standard);
            rated.VatRate.Percent.Should().Be(22m);
            rated.VatRate.ExemptionCode.Should().BeNull();
            rated.CreditedQuantity.Value.Should().Be(0m);

            InvoiceLine exempt = lines[1];
            exempt.VatRate.Category.Should().Be(VatCategory.Exempt);
            exempt.VatRate.ExemptionCode.Should().Be("M07");
            exempt.VatRate.ExemptionReason.Should().Be("Isento artigo 9.o do CIVA");

            // Computed from the lines rather than stored, so this is really a check that the
            // lines came back whole.
            stored.Taxes.StandardBase.Should().Be(55.13m);
            stored.Taxes.ExemptBase.Should().Be(50.00m);
        });
    }

    /// <summary>
    /// The tenant filter, which is the only thing between two companies' documents.
    /// <para>
    /// It is a global query filter, so it is invisible at every call site — which is the point,
    /// and also why nothing else would notice if it stopped being applied.
    /// </para>
    /// </summary>
    [Fact]
    public async Task One_tenants_documents_are_invisible_to_another()
    {
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();

        await DraftForAsync(first);
        await DraftForAsync(second);

        int seenByFirst = await CountAsync(first);
        int seenBySecond = await CountAsync(second);

        seenByFirst.Should().Be(1);
        seenBySecond.Should().Be(1);
    }

    private Task DraftForAsync(Guid tenant) =>
        _fixture.Application.AsTenantAsync(tenant, async provider =>
        {
            var invoices = provider.GetRequiredService<IInvoiceRepository>();
            var unitOfWork = provider.GetRequiredService<IInvoicingUnitOfWork>();

            Invoice invoice = Invoice.Draft(
                DocumentType.Invoice,
                new CustomerRef(Guid.NewGuid()),
                "Oficina Isolada, Lda.",
                "501234567",
                "PT",
                Currency.Eur,
                TaxRegion.Mainland,
                new DateOnly(2026, 9, 7)).Value;

            invoices.Add(invoice);
            await unitOfWork.SaveChangesAsync();
        });

    private Task<int> CountAsync(Guid tenant) =>
        _fixture.Application.AsTenantAsync(tenant, provider =>
            provider.GetRequiredService<InvoicingDbContext>().Invoices.CountAsync());
}
