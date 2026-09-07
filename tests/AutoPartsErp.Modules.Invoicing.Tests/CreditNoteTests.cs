using AutoPartsErp.Modules.Invoicing.Domain;
using AutoPartsErp.Modules.Invoicing.Domain.Invoices;
using AutoPartsErp.Modules.Invoicing.Domain.Series;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;
using static AutoPartsErp.Modules.Invoicing.Tests.InvoicingTestData;

namespace AutoPartsErp.Modules.Invoicing.Tests;

/// <summary>
/// Reversing an invoice, which is the only thing anybody will need in their first week and the
/// only correct way to undo a document that has been numbered, signed and reported.
/// </summary>
public sealed class CreditNoteTests
{
    private static readonly IReadOnlyDictionary<InvoiceLineId, Quantity> Everything =
        new Dictionary<InvoiceLineId, Quantity>();

    [Fact]
    public void A_credit_note_copies_the_customer_the_currency_and_the_region()
    {
        Invoice original = Issued();

        Invoice note = Invoice.DraftCreditNote(
            original, Everything, "Wrong customer account", new DateOnly(2026, 9, 10)).Value;

        note.Type.Should().Be(DocumentType.CreditNote);
        note.IsCreditNote.Should().BeTrue();
        note.CustomerId.Should().Be(original.CustomerId);
        note.CustomerTaxNumber.Should().Be(original.CustomerTaxNumber);
        note.CurrencyCode.Should().Be(original.CurrencyCode);
        note.TaxRegion.Should().Be(original.TaxRegion);
        note.CreditedInvoiceId.Should().Be(original.Id);
        note.CreditedDocumentNumber.Should().Be("FT SERIE2026/1");
        note.CreditReason.Should().Be("Wrong customer account");
        note.IsDraft.Should().BeTrue();
    }

    /// <summary>
    /// Rates move. A credit issued after they move has to reverse at the rate that was actually
    /// charged, or the VAT return is out by the difference and the customer is refunded the wrong
    /// amount.
    /// </summary>
    [Fact]
    public void Every_figure_is_copied_from_the_original_rather_than_recomputed()
    {
        Invoice original = Issued();
        InvoiceLine invoiced = original.Lines.Single();

        Invoice note = Invoice.DraftCreditNote(
            original, Everything, "Returned", new DateOnly(2026, 9, 10)).Value;

        InvoiceLine credited = note.Lines.Single();

        credited.PartId.Should().Be(invoiced.PartId);
        credited.Sku.Should().Be(invoiced.Sku);
        credited.Description.Should().Be(invoiced.Description);
        credited.UnitPrice.Amount.Should().Be(invoiced.UnitPrice.Amount);
        credited.DiscountPercent.Should().Be(invoiced.DiscountPercent);
        credited.VatRate.Percent.Should().Be(invoiced.VatRate.Percent);
        credited.VatRate.Category.Should().Be(invoiced.VatRate.Category);
        credited.NetAmount.Amount.Should().Be(invoiced.NetAmount.Amount);
    }

    /// <summary>
    /// Matched on the line rather than the part, because nothing stops one document listing the
    /// same part twice and the part is therefore not a key.
    /// </summary>
    [Fact]
    public void Each_credited_line_points_back_at_the_line_it_credits()
    {
        Invoice original = Issued();
        InvoiceLine invoiced = original.Lines.Single();

        Invoice note = Invoice.DraftCreditNote(
            original, Everything, "Returned", new DateOnly(2026, 9, 10)).Value;

        note.Lines.Single().CreditsLineId.Should().Be(invoiced.Id);
    }

    [Fact]
    public void An_unissued_document_has_nothing_to_credit()
    {
        Invoice draft = Draft();
        AddLine(draft);

        Invoice.DraftCreditNote(draft, Everything, "Whatever", new DateOnly(2026, 9, 10))
            .Error.Code.Should().Be("invoicing.credit.original_not_issued");
    }

    /// <summary>
    /// A voided document bills nobody, so crediting it would give money back against a demand
    /// that was already withdrawn — a second error rather than a correction.
    /// </summary>
    [Fact]
    public void A_voided_document_cannot_be_credited()
    {
        Invoice original = Issued();
        original.Void("Raised in error", EntryUtc);

        Invoice.DraftCreditNote(original, Everything, "Returned", new DateOnly(2026, 9, 10))
            .Error.Code.Should().Be("invoicing.credit.original_voided");
    }

    [Fact]
    public void A_credit_note_cannot_itself_be_credited()
    {
        Invoice original = Issued();
        Invoice note = Invoice.DraftCreditNote(
            original, Everything, "Returned", new DateOnly(2026, 9, 10)).Value;

        note.Issue(
            ActiveSeries(DocumentType.CreditNote, "NC2026"),
            new RecordingSigner(),
            null,
            "501234567",
            EntryUtc);

        Invoice.DraftCreditNote(note, Everything, "Changed my mind", new DateOnly(2026, 9, 11))
            .Error.Code.Should().Be("invoicing.credit.original_is_credit_note");
    }

    [Fact]
    public void A_credit_note_needs_a_reason()
    {
        Invoice original = Issued();

        Invoice.DraftCreditNote(original, Everything, "   ", new DateOnly(2026, 9, 10))
            .Error.Code.Should().Be("invoicing.credit.reason_required");
    }

    [Fact]
    public void Part_of_a_line_can_be_credited()
    {
        Invoice original = Issued(quantity: 10m);
        InvoiceLine invoiced = original.Lines.Single();

        Invoice note = Invoice.DraftCreditNote(
            original,
            new Dictionary<InvoiceLineId, Quantity> { [invoiced.Id] = Quantity.Each(4) },
            "Four came back damaged",
            new DateOnly(2026, 9, 10)).Value;

        note.Lines.Single().Quantity.Value.Should().Be(4m);
    }

    [Fact]
    public void More_than_was_invoiced_is_refused_and_says_how_much_is_left()
    {
        Invoice original = Issued(quantity: 10m);
        InvoiceLine invoiced = original.Lines.Single();

        Result<Invoice> note = Invoice.DraftCreditNote(
            original,
            new Dictionary<InvoiceLineId, Quantity> { [invoiced.Id] = Quantity.Each(11) },
            "Optimistic",
            new DateOnly(2026, 9, 10));

        note.Error.Code.Should().Be("invoicing.credit.exceeds_invoiced");
        note.Error.Description.Should().Contain("10");
    }

    /// <summary>
    /// The invariant that matters. Two credit notes of six against an invoice of ten would give
    /// back two more than was ever charged.
    /// </summary>
    [Fact]
    public void Issuing_a_credit_note_consumes_what_it_credited()
    {
        Invoice original = Issued(quantity: 10m);
        InvoiceLine invoiced = original.Lines.Single();

        Invoice first = CreditFor(original, invoiced, 6, "Six came back");
        original.ApplyCredit(first).IsSuccess.Should().BeTrue();

        invoiced.CreditedQuantity.Value.Should().Be(6m);
        invoiced.CreditableQuantity.Value.Should().Be(4m);
        invoiced.IsFullyCredited.Should().BeFalse();
        original.HasCreditableLines.Should().BeTrue();

        Result<Invoice> second = Invoice.DraftCreditNote(
            original,
            new Dictionary<InvoiceLineId, Quantity> { [invoiced.Id] = Quantity.Each(6) },
            "Six more",
            new DateOnly(2026, 9, 11));

        second.Error.Code.Should().Be("invoicing.credit.exceeds_invoiced");
    }

    [Fact]
    public void The_rest_of_a_partly_credited_invoice_can_still_be_credited()
    {
        Invoice original = Issued(quantity: 10m);
        InvoiceLine invoiced = original.Lines.Single();

        original.ApplyCredit(CreditFor(original, invoiced, 6, "Six came back"));

        // No selection at all means "whatever is left", which is four rather than ten.
        Invoice rest = Invoice.DraftCreditNote(
            original, Everything, "The rest too", new DateOnly(2026, 9, 12)).Value;

        rest.Lines.Single().Quantity.Value.Should().Be(4m);

        original.ApplyCredit(rest);

        invoiced.IsFullyCredited.Should().BeTrue();
        original.HasCreditableLines.Should().BeFalse();
    }

    [Fact]
    public void An_invoice_credited_in_full_has_nothing_left_to_credit()
    {
        Invoice original = Issued(quantity: 10m);

        original.ApplyCredit(Invoice.DraftCreditNote(
            original, Everything, "All of it", new DateOnly(2026, 9, 10)).Value);

        Invoice.DraftCreditNote(original, Everything, "Again", new DateOnly(2026, 9, 11))
            .Error.Code.Should().Be("invoicing.credit.nothing_left");
    }

    [Fact]
    public void A_note_raised_against_another_document_is_refused()
    {
        Invoice original = Issued();
        Invoice other = Issued();

        Invoice note = Invoice.DraftCreditNote(
            original, Everything, "Returned", new DateOnly(2026, 9, 10)).Value;

        other.ApplyCredit(note).Error.Code.Should().Be("invoicing.credit.wrong_document");
    }

    /// <summary>
    /// A credit note takes its number from a series registered for NC, like any other document.
    /// Issuing one into the FT series would report a credit as an invoice.
    /// </summary>
    [Fact]
    public void A_credit_note_issues_from_a_credit_note_series()
    {
        Invoice original = Issued();
        Invoice note = Invoice.DraftCreditNote(
            original, Everything, "Returned", new DateOnly(2026, 9, 10)).Value;

        note.Issue(ActiveSeries(), new RecordingSigner(), null, "501234567", EntryUtc)
            .Error.Code.Should().Be("invoicing.document.series_type_mismatch");

        note.Issue(
                ActiveSeries(DocumentType.CreditNote, "NC2026"),
                new RecordingSigner(),
                null,
                "501234567",
                EntryUtc)
            .IsSuccess.Should().BeTrue();

        note.DocumentNumber.Should().Be("NC NC2026/1");
    }

    private static Invoice CreditFor(
        Invoice original,
        InvoiceLine line,
        int quantity,
        string reason) =>
        Invoice.DraftCreditNote(
            original,
            new Dictionary<InvoiceLineId, Quantity> { [line.Id] = Quantity.Each(quantity) },
            reason,
            new DateOnly(2026, 9, 10)).Value;

    private static Invoice Issued(decimal quantity = 1m)
    {
        Invoice invoice = Draft();
        AddLine(invoice, quantity: quantity, unitPrice: 100m);
        invoice.Issue(ActiveSeries(), new RecordingSigner(), null, "501234567", EntryUtc);

        return invoice;
    }
}
