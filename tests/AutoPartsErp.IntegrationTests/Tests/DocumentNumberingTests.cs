using AutoPartsErp.Modules.Invoicing.Application.Documents.Commands;
using AutoPartsErp.Modules.Invoicing.Application.Series.Commands;
using AutoPartsErp.Modules.Invoicing.Domain;
using AutoPartsErp.Modules.Invoicing.Domain.Invoices;
using AutoPartsErp.Modules.Invoicing.Domain.Series;
using AutoPartsErp.Modules.Invoicing.Infrastructure.Persistence;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AutoPartsErp.IntegrationTests.Tests;

/// <summary>
/// Gapless numbering, under the only conditions that can disprove it.
/// <para>
/// Everything else about the series aggregate is covered by unit tests, and none of those tests
/// can fail the way this one can. The row lock lives in a repository, it is written in SQL, and
/// what it does only exists when two connections want the same row at the same moment. A unit
/// test of it would be a test of a mock.
/// </para>
/// <para>
/// What this replaces is a comment. The repository says two tills issuing at once will queue
/// rather than collide, and until this ran, that was an assertion.
/// </para>
/// </summary>
[Collection(ErpCollection.Name)]
public sealed class DocumentNumberingTests
{
    private const int Tills = 8;

    private readonly ErpFixture _fixture;

    /// <summary>Initializes the tests.</summary>
    public DocumentNumberingTests(ErpFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// Eight documents issued at once take eight consecutive numbers, and no number twice.
    /// <para>
    /// Without the lock every one of them reads the same next number: they do not fail, they all
    /// succeed with the same document number, and the series counter still ends up in the right
    /// place because the increment itself is atomic. That is the shape of this bug — nothing looks
    /// wrong until somebody reads the documents.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Eight_tills_issuing_at_once_take_eight_different_numbers()
    {
        Guid tenant = Guid.NewGuid();
        await OpenSeriesAsync(tenant, "CONC" + Suffix());

        List<Guid> drafts = [];

        for (int index = 0; index < Tills; index++)
        {
            drafts.Add(await DraftAsync(tenant));
        }

        // Started together and awaited together. Each runs in its own scope, so each has its own
        // DbContext and its own connection — which is what makes them contend for the row rather
        // than queue behind one another in a single context.
        Task<Result>[] issuing =
            [.. drafts.Select(draft => Task.Run(() => IssueAsync(tenant, draft)))];

        Result[] results = await Task.WhenAll(issuing);

        results.Should().OnlyContain(result => result.IsSuccess);

        IReadOnlyList<string> numbers = await NumbersAsync(tenant);

        numbers.Should().HaveCount(Tills);
        numbers.Distinct(StringComparer.Ordinal).Should().HaveCount(
            Tills,
            "two documents sharing a number is the failure this lock exists to prevent");

        IReadOnlyList<int> positions = await PositionsAsync(tenant);

        positions.Should().Equal(Enumerable.Range(1, Tills).ToArray());
    }

    /// <summary>
    /// A document that fails after taking a number leaves no gap.
    /// <para>
    /// The number is taken by mutating the series, so the only thing that puts it back is the
    /// transaction rolling back. Here the failure is a document with no lines, which is refused
    /// after the series has been locked and before anything is written.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_refused_issue_does_not_consume_a_number()
    {
        Guid tenant = Guid.NewGuid();
        Guid seriesId = await OpenSeriesAsync(tenant, "GAP" + Suffix());

        Guid empty = await _fixture.Application.AsTenantAsync(tenant, async provider =>
        {
            var dispatcher = provider.GetRequiredService<IDispatcher>();

            Result<Guid> created = await dispatcher.SendAsync(new CreateDocumentCommand(
                "FT", Guid.NewGuid(), "Oficina Sem Linhas, Lda.", "501234567"));

            return created.Value;
        });

        // No lines, so this is refused - after the series has been locked.
        Result refused = await IssueAsync(tenant, empty);
        refused.Error.Code.Should().Be("invoicing.document.no_lines");

        Guid good = await DraftAsync(tenant);
        (await IssueAsync(tenant, good)).IsSuccess.Should().BeTrue();

        (await PositionsAsync(tenant)).Should().Equal(1);

        int next = await _fixture.Application.AsTenantAsync(tenant, async provider =>
        {
            var context = provider.GetRequiredService<InvoicingDbContext>();
            var id = new DocumentSeriesId(seriesId);

            return await context.DocumentSeries
                .Where(series => series.Id == id)
                .Select(series => series.NextNumber)
                .SingleAsync();
        });

        next.Should().Be(2, "the refused document must not have consumed number 1");
    }

    /// <summary>
    /// Each document's signature covers the one before it in the same series.
    /// <para>
    /// The chain is what makes the documents tamper-evident, and it depends on the previous
    /// signature being read after the lock is held. Read before, two documents issued at once
    /// would both chain onto the same predecessor and the chain would fork.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Each_document_chains_onto_the_one_before_it()
    {
        Guid tenant = Guid.NewGuid();
        await OpenSeriesAsync(tenant, "CHAIN" + Suffix());

        for (int index = 0; index < 3; index++)
        {
            (await IssueAsync(tenant, await DraftAsync(tenant))).IsSuccess.Should().BeTrue();
        }

        IReadOnlyList<string> signatures = await _fixture.Application.AsTenantAsync(
            tenant,
            async provider =>
            {
                var context = provider.GetRequiredService<InvoicingDbContext>();

                List<string> found = await context.Invoices
                    .AsNoTracking()
                    .Where(invoice => invoice.SeriesNumber > 0)
                    .OrderBy(invoice => invoice.SeriesNumber)
                    .Select(invoice => invoice.Signature!.Value)
                    .ToListAsync();

                return (IReadOnlyList<string>)found;
            });

        signatures.Should().HaveCount(3);
        signatures.Should().OnlyContain(signature => signature.Length > 0);
        signatures.Distinct(StringComparer.Ordinal).Should().HaveCount(
            3, "each document signs a different string, so no two signatures can match");
    }

    private static string Suffix() => Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    private async Task<Guid> OpenSeriesAsync(Guid tenant, string code) =>
        await _fixture.Application.AsTenantAsync(tenant, async provider =>
        {
            var dispatcher = provider.GetRequiredService<IDispatcher>();

            Result<Guid> opened =
                await dispatcher.SendAsync(new OpenDocumentSeriesCommand("FT", code, DateTime.UtcNow.Year));

            opened.IsSuccess.Should().BeTrue(opened.IsFailure ? opened.Error.Description : null);

            await dispatcher.SendAsync(
                new ValidateDocumentSeriesCommand(opened.Value, "CSDF7T5H"));

            await dispatcher.SendAsync(new ActivateDocumentSeriesCommand(opened.Value));

            return opened.Value;
        });

    private async Task<Guid> DraftAsync(Guid tenant) =>
        await _fixture.Application.AsTenantAsync(tenant, async provider =>
        {
            var invoices = provider.GetRequiredService<IInvoiceRepository>();
            var unitOfWork = provider.GetRequiredService<IInvoicingUnitOfWork>();

            // Built through the aggregate rather than through AddDocumentLine, because that
            // handler asks the Catalog for the part and this test is not about the catalogue.
            Invoice invoice = Invoice.Draft(
                DocumentType.Invoice,
                new CustomerRef(Guid.NewGuid()),
                "Oficina Concorrente, Lda.",
                "501234567",
                "PT",
                Currency.Eur,
                TaxRegion.Mainland,
                DateOnly.FromDateTime(DateTime.UtcNow)).Value;

            invoice.AddLine(
                new PartRef(Guid.NewGuid()),
                "BP-1188",
                "Brake pad set, front axle",
                Quantity.Each(1),
                Money.Of(100m, Currency.Eur),
                0m,
                VatRate.PortugalStandard);

            invoices.Add(invoice);
            await unitOfWork.SaveChangesAsync();

            return invoice.Id.Value;
        });

    private Task<Result> IssueAsync(Guid tenant, Guid invoiceId) =>
        _fixture.Application.AsTenantAsync(tenant, provider =>
            provider.GetRequiredService<IDispatcher>()
                .SendAsync(new IssueDocumentCommand(invoiceId)));

    private Task<IReadOnlyList<string>> NumbersAsync(Guid tenant) =>
        _fixture.Application.AsTenantAsync(tenant, async provider =>
        {
            var context = provider.GetRequiredService<InvoicingDbContext>();

            List<string> numbers = await context.Invoices
                .AsNoTracking()
                .Where(invoice => invoice.SeriesNumber > 0)
                .Select(invoice => invoice.DocumentNumber)
                .ToListAsync();

            return (IReadOnlyList<string>)numbers;
        });

    private Task<IReadOnlyList<int>> PositionsAsync(Guid tenant) =>
        _fixture.Application.AsTenantAsync(tenant, async provider =>
        {
            var context = provider.GetRequiredService<InvoicingDbContext>();

            List<int> positions = await context.Invoices
                .AsNoTracking()
                .Where(invoice => invoice.SeriesNumber > 0)
                .OrderBy(invoice => invoice.SeriesNumber)
                .Select(invoice => invoice.SeriesNumber)
                .ToListAsync();

            return (IReadOnlyList<int>)positions;
        });
}
