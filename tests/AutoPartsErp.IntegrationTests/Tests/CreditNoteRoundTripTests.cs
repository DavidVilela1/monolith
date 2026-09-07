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
/// A credit note drawn from an invoice, written to PostgreSQL and read back.
/// <para>
/// The credit path is the best-covered thing in the module at the unit level and, until this file,
/// the least covered at the database level. That combination is exactly where a mapping bug hides:
/// every rule about crediting is proven, and none of the five columns those rules write had ever
/// been round-tripped.
/// </para>
/// <para>
/// Those columns only exist when this path is taken. An ordinary invoice leaves
/// <c>credited_invoice_id</c>, <c>credited_document_number</c>, <c>credit_reason</c> and every
/// line's <c>credits_line_id</c> null for its whole life, and <c>credited_quantity</c> at zero —
/// so the existing round-trip test asserts they are empty and could not tell you whether they work
/// when they are not. The partial index on <c>credited_invoice_id IS NOT NULL</c> is in the same
/// position: <c>SchemaTests</c> proves it exists, and nothing had ever put a row in it.
/// </para>
/// </summary>
[Collection(ErpCollection.Name)]
public sealed class CreditNoteRoundTripTests
{
    private const string ValidationCode = "CSDF7T5H";

    private readonly ErpFixture _fixture;

    /// <summary>Initializes the tests.</summary>
    public CreditNoteRoundTripTests(ErpFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// Crediting a whole invoice writes every back-reference, and they all survive a reload.
    /// </summary>
    [Fact]
    public async Task A_full_credit_note_survives_being_written_and_read_back()
    {
        Guid tenant = Guid.NewGuid();

        await OpenSeriesAsync(tenant, "FT", "FT" + Suffix());
        await OpenSeriesAsync(tenant, "NC", "NC" + Suffix());

        Guid invoiceId = await DraftInvoiceAsync(tenant);
        (await IssueAsync(tenant, invoiceId)).IsSuccess.Should().BeTrue();

        // No lines given, which means everything still creditable - the common case, and the one
        // the endpoint documents as "the lot came back, or it went to the wrong company".
        Guid creditNoteId = await DraftCreditAsync(
            tenant, invoiceId, "Raised against the wrong customer account");

        (await IssueAsync(tenant, creditNoteId)).IsSuccess.Should().BeTrue();

        // A second scope, so nothing comes back out of the first context's identity map.
        await _fixture.Application.AsTenantAsync(tenant, async provider =>
        {
            var context = provider.GetRequiredService<InvoicingDbContext>();

            Invoice note = await LoadAsync(context, creditNoteId);

            note.IsCreditNote.Should().BeTrue();
            note.Type.Should().Be(DocumentType.CreditNote);

            note.CreditedInvoiceId.Should().NotBeNull();
            note.CreditedInvoiceId!.Value.Value.Should().Be(invoiceId);
            note.CreditReason.Should().Be("Raised against the wrong customer account");

            // Copied at drafting rather than read through the identifier, because the number is
            // what a person reads on the paper and it must not change if anything upstream does.
            Invoice original = await LoadAsync(context, invoiceId);
            note.CreditedDocumentNumber.Should().Be(original.DocumentNumber);

            note.Lines.Should().HaveCount(2);

            // Each line points back at the line it reverses. Null on every line of every ordinary
            // document, so this is the only test that can tell the mapping works.
            foreach (InvoiceLine line in note.Lines)
            {
                line.CreditsLineId.Should().NotBeNull();
            }

            note.Lines.Select(line => line.CreditsLineId!.Value)
                .Should().BeEquivalentTo(original.Lines.Select(line => line.Id));

            // The other half of it: the original's lines record what has been given back, and
            // they record it on the invoice rather than by counting up the notes against it.
            original.Lines.Should().AllSatisfy(line => line.IsFullyCredited.Should().BeTrue());
            original.HasCreditableLines.Should().BeFalse();
        });
    }

    /// <summary>
    /// Crediting part of a line leaves the rest creditable, across a reload.
    /// <para>
    /// The quantity that has been given back is the one number here that a later credit reads and
    /// acts on. If it did not persist, the second credit note would be drawn against the full
    /// original quantity and the customer would be refunded twice — with every unit test still
    /// green, because in memory it was always right.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_partial_credit_persists_what_is_left_to_credit()
    {
        Guid tenant = Guid.NewGuid();

        await OpenSeriesAsync(tenant, "FT", "FT" + Suffix());
        await OpenSeriesAsync(tenant, "NC", "NC" + Suffix());

        Guid invoiceId = await DraftInvoiceAsync(tenant);
        (await IssueAsync(tenant, invoiceId)).IsSuccess.Should().BeTrue();

        Guid firstLineId = await _fixture.Application.AsTenantAsync(tenant, async provider =>
        {
            var context = provider.GetRequiredService<InvoicingDbContext>();
            Invoice invoice = await LoadAsync(context, invoiceId);

            return invoice.Lines[0].Id.Value;
        });

        // Four of the ten on the first line.
        Guid creditNoteId = await DraftCreditAsync(
            tenant,
            invoiceId,
            "Four came back damaged",
            [new CreditLine(firstLineId, 4m)]);

        (await IssueAsync(tenant, creditNoteId)).IsSuccess.Should().BeTrue();

        await _fixture.Application.AsTenantAsync(tenant, async provider =>
        {
            var context = provider.GetRequiredService<InvoicingDbContext>();

            Invoice note = await LoadAsync(context, creditNoteId);
            note.Lines.Should().ContainSingle("only the credited line belongs on the note");
            note.Lines[0].Quantity.Value.Should().Be(4m);

            Invoice original = await LoadAsync(context, invoiceId);

            InvoiceLine credited = original.Lines.Single(line => line.Id.Value == firstLineId);
            credited.CreditedQuantity.Value.Should().Be(4m);
            credited.CreditableQuantity.Value.Should().Be(6m);
            credited.IsFullyCredited.Should().BeFalse();

            InvoiceLine untouched = original.Lines.Single(line => line.Id.Value != firstLineId);
            untouched.CreditedQuantity.Value.Should().Be(0m);

            original.HasCreditableLines.Should().BeTrue(
                "six of the first line and all of the second are still owed back");
        });
    }

    /// <summary>
    /// A credit note takes its number from the credit-note series, leaving the invoice series
    /// where it was.
    /// <para>
    /// A series is declared to the tax authority for one document type, so a credit numbered in
    /// the invoice series is reported as an invoice. Both series are rows in one table reached
    /// through one repository, and which one is picked is decided by a query — which is a thing
    /// only a database can answer.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_credit_note_is_numbered_from_the_credit_note_series()
    {
        Guid tenant = Guid.NewGuid();

        Guid invoiceSeries = await OpenSeriesAsync(tenant, "FT", "FT" + Suffix());
        Guid creditSeries = await OpenSeriesAsync(tenant, "NC", "NC" + Suffix());

        Guid invoiceId = await DraftInvoiceAsync(tenant);
        (await IssueAsync(tenant, invoiceId)).IsSuccess.Should().BeTrue();

        Guid creditNoteId = await DraftCreditAsync(tenant, invoiceId, "Returned in full");
        (await IssueAsync(tenant, creditNoteId)).IsSuccess.Should().BeTrue();

        await _fixture.Application.AsTenantAsync(tenant, async provider =>
        {
            var context = provider.GetRequiredService<InvoicingDbContext>();

            Invoice note = await LoadAsync(context, creditNoteId);

            note.SeriesId.Should().NotBeNull();
            note.SeriesId!.Value.Value.Should().Be(
                creditSeries, "a credit in the invoice series is reported as an invoice");

            note.SeriesNumber.Should().Be(1);
            note.Atcud.Should().NotBeNull();
            note.Signature.Should().NotBeNull();
            note.QrCode.Should().NotBeNull();

            // One document issued in each, so each counter stands at two and neither has been
            // advanced by the other.
            (await NextNumberAsync(context, invoiceSeries)).Should().Be(2);
            (await NextNumberAsync(context, creditSeries)).Should().Be(2);
        });
    }

    private static string Suffix() => Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    private static async Task<Invoice> LoadAsync(InvoicingDbContext context, Guid id)
    {
        var invoiceId = new InvoiceId(id);

        Invoice? found = await context.Invoices
            .AsNoTracking()
            .FirstOrDefaultAsync(invoice => invoice.Id == invoiceId);

        found.Should().NotBeNull();

        return found!;
    }

    private static Task<int> NextNumberAsync(InvoicingDbContext context, Guid seriesId)
    {
        var id = new DocumentSeriesId(seriesId);

        return context.DocumentSeries
            .AsNoTracking()
            .Where(series => series.Id == id)
            .Select(series => series.NextNumber)
            .SingleAsync();
    }

    private async Task<Guid> OpenSeriesAsync(Guid tenant, string type, string code) =>
        await _fixture.Application.AsTenantAsync(tenant, async provider =>
        {
            var dispatcher = provider.GetRequiredService<IDispatcher>();

            Result<Guid> opened = await dispatcher.SendAsync(
                new OpenDocumentSeriesCommand(type, code, DateTime.UtcNow.Year));

            opened.IsSuccess.Should().BeTrue(opened.IsFailure ? opened.Error.Description : null);

            await dispatcher.SendAsync(
                new ValidateDocumentSeriesCommand(opened.Value, ValidationCode));

            await dispatcher.SendAsync(new ActivateDocumentSeriesCommand(opened.Value));

            return opened.Value;
        });

    private async Task<Guid> DraftInvoiceAsync(Guid tenant) =>
        await _fixture.Application.AsTenantAsync(tenant, async provider =>
        {
            var invoices = provider.GetRequiredService<IInvoiceRepository>();
            var unitOfWork = provider.GetRequiredService<IInvoicingUnitOfWork>();

            Invoice invoice = Invoice.Draft(
                DocumentType.Invoice,
                new CustomerRef(Guid.NewGuid()),
                "Oficina da Nota de Credito, Lda.",
                "501234567",
                "PT",
                Currency.Eur,
                TaxRegion.Mainland,
                DateOnly.FromDateTime(DateTime.UtcNow)).Value;

            invoice.AddLine(
                new PartRef(Guid.NewGuid()),
                "BP-1188",
                "Brake pad set, front axle",
                Quantity.Each(10),
                Money.Of(24.50m, Currency.Eur),
                0m,
                VatRate.PortugalStandard);

            invoice.AddLine(
                new PartRef(Guid.NewGuid()),
                "OF-2200",
                "Oil filter",
                Quantity.Each(2),
                Money.Of(9.90m, Currency.Eur),
                0m,
                VatRate.PortugalStandard);

            invoices.Add(invoice);
            await unitOfWork.SaveChangesAsync();

            return invoice.Id.Value;
        });

    private async Task<Guid> DraftCreditAsync(
        Guid tenant,
        Guid invoiceId,
        string reason,
        IReadOnlyList<CreditLine>? lines = null) =>
        await _fixture.Application.AsTenantAsync(tenant, async provider =>
        {
            Result<Guid> drafted = await provider.GetRequiredService<IDispatcher>()
                .SendAsync(new DraftCreditNoteCommand(invoiceId, reason, lines));

            drafted.IsSuccess.Should().BeTrue(drafted.IsFailure ? drafted.Error.Description : null);

            return drafted.Value;
        });

    private Task<Result> IssueAsync(Guid tenant, Guid documentId) =>
        _fixture.Application.AsTenantAsync(tenant, provider =>
            provider.GetRequiredService<IDispatcher>()
                .SendAsync(new IssueDocumentCommand(documentId)));
}
