using AutoPartsErp.Modules.Invoicing.Domain;
using AutoPartsErp.Modules.Invoicing.Domain.Invoices;
using AutoPartsErp.Modules.Invoicing.Domain.Series;
using AutoPartsErp.SharedKernel.ValueObjects;
using static AutoPartsErp.Modules.Invoicing.Tests.InvoicingTestData;

namespace AutoPartsErp.Modules.Invoicing.Tests;

/// <summary>
/// A document being built, issued and — where it has to be — withdrawn.
/// </summary>
public sealed class InvoiceTests
{
    /// <summary>
    /// Extend, discount, net, VAT, rounding at each step. That order is what a Portuguese invoice
    /// shows line by line, and computing it any other way gives totals a cent out from the page.
    /// </summary>
    [Fact]
    public void Line_arithmetic_runs_in_the_order_the_document_prints()
    {
        Invoice invoice = Draft();

        invoice.AddLine(
            new PartRef(Guid.NewGuid()),
            "BP-1188",
            "Brake pad set, front axle",
            Quantity.Of(10m, UnitOfMeasure.Each),
            Money.Of(24.50m, Currency.Eur),
            10m,
            VatRate.PortugalStandard);

        InvoiceLine line = invoice.Lines.Single();

        line.ExtendedPrice.Amount.Should().Be(245.00m);
        line.DiscountAmount.Amount.Should().Be(24.50m);
        line.NetAmount.Amount.Should().Be(220.50m);
        line.VatAmount.Amount.Should().Be(50.72m);
        line.GrossAmount.Amount.Should().Be(271.22m);
    }

    /// <summary>
    /// Every prescribed output wants the split rather than the grand total: a pair of QR fields
    /// per category, a TaxTable entry per rate, and a VAT return that is nothing but this summed.
    /// </summary>
    [Fact]
    public void Totals_are_split_by_vat_category()
    {
        Invoice invoice = Draft();

        invoice.AddLine(
            new PartRef(Guid.NewGuid()), "BP-1188", "Brake pad set",
            Quantity.Of(10m, UnitOfMeasure.Each), Money.Of(24.50m, Currency.Eur), 10m,
            VatRate.PortugalStandard);

        invoice.AddLine(
            new PartRef(Guid.NewGuid()), "OIL-5W30", "Engine oil 5W30, 1L",
            Quantity.Of(4m, UnitOfMeasure.Litre), Money.Of(10m, Currency.Eur), 0m,
            VatRate.Of(VatCategory.Reduced, 6m).Value);

        TaxSummary taxes = invoice.Taxes;

        taxes.StandardBase.Should().Be(220.50m);
        taxes.StandardVat.Should().Be(50.72m);
        taxes.ReducedBase.Should().Be(40.00m);
        taxes.ReducedVat.Should().Be(2.40m);
        taxes.ExemptBase.Should().Be(0m);
        taxes.NetTotal.Should().Be(260.50m);
        taxes.VatTotal.Should().Be(53.12m);
        taxes.GrossTotal.Should().Be(313.62m);
    }

    /// <summary>
    /// A SAF-T export carries LineNumber and a reprint has to match the original exactly, so the
    /// position is stored rather than taken from whatever order rows come back in.
    /// </summary>
    [Fact]
    public void Lines_are_numbered_from_one_in_the_order_they_were_added()
    {
        Invoice invoice = Draft();
        AddLine(invoice);
        AddLine(invoice);
        AddLine(invoice);

        invoice.Lines.Select(line => line.Number).Should().Equal(1, 2, 3);
    }

    [Fact]
    public void A_line_in_another_currency_is_refused()
    {
        Invoice invoice = Draft();

        invoice.AddLine(
            new PartRef(Guid.NewGuid()), "X", "Something",
            Quantity.Of(1m, UnitOfMeasure.Each), Money.Of(1m, Currency.Usd), 0m,
            VatRate.PortugalStandard)
            .Error.Code.Should().Be("invoicing.line.currency_mismatch");
    }

    /// <summary>
    /// A sale to somebody who is not identified is a simplified invoice, which is a different
    /// document type with different rules — not an invoice with a field left blank.
    /// </summary>
    [Fact]
    public void An_invoice_identifies_its_customer_and_a_simplified_invoice_need_not()
    {
        Invoice.Draft(
            DocumentType.Invoice, new CustomerRef(Guid.NewGuid()), "Counter customer", null,
            "PT", Currency.Eur, TaxRegion.Mainland, Today)
            .Error.Code.Should().Be("invoicing.document.invoice_needs_tax_number");

        Invoice.Draft(
            DocumentType.SimplifiedInvoice, new CustomerRef(Guid.NewGuid()), "Counter customer", null,
            "PT", Currency.Eur, TaxRegion.Mainland, Today)
            .IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void An_empty_document_cannot_be_issued()
    {
        Invoice invoice = Draft();

        invoice.Issue(ActiveSeries(), new RecordingSigner(), null, "501234567", EntryUtc)
            .Error.Code.Should().Be("invoicing.document.no_lines");
    }

    [Fact]
    public void Issuing_takes_a_number_builds_the_codes_and_signs_the_total()
    {
        DocumentSeries series = ActiveSeries();
        var signer = new RecordingSigner();
        Invoice invoice = Draft();
        AddLine(invoice, quantity: 1m, unitPrice: 100m);

        invoice.IsDraft.Should().BeTrue();

        invoice.Issue(series, signer, null, "501234567", EntryUtc).IsSuccess.Should().BeTrue();

        invoice.DocumentNumber.Should().Be("FT SERIE2026/1");
        invoice.Atcud!.Value.Should().Be("CSDF7T5H-1");
        invoice.Signature!.Printed.Should().Be("kLp0");
        invoice.SystemEntryDateUtc.Should().Be(EntryUtc);
        invoice.IsIssued.Should().BeTrue();
        series.NextNumber.Should().Be(2);

        signer.LastSource.Should().Be("2026-09-04;2026-09-04T09:30:00;FT SERIE2026/1;123.00;");
        invoice.DomainEvents.Should().ContainSingle();
    }

    [Fact]
    public void An_issued_document_is_frozen()
    {
        DocumentSeries series = ActiveSeries();
        Invoice invoice = Draft();
        AddLine(invoice);
        invoice.Issue(series, new RecordingSigner(), null, "501234567", EntryUtc);

        invoice.Issue(series, new RecordingSigner(), null, "501234567", EntryUtc)
            .Error.Code.Should().Be("invoicing.document.already_issued");

        invoice.AddLine(
            new PartRef(Guid.NewGuid()), "X", "Something",
            Quantity.Of(1m, UnitOfMeasure.Each), Money.Of(1m, Currency.Eur), 0m,
            VatRate.PortugalStandard)
            .Error.Code.Should().Be("invoicing.document.already_issued");
    }

    /// <summary>
    /// Altering document 35 invalidates 36 and everything after it. That is the whole point of
    /// the chain, and it only works if each document actually signs the one before.
    /// </summary>
    [Fact]
    public void The_second_document_in_a_series_signs_onto_the_first()
    {
        DocumentSeries series = ActiveSeries();
        var signer = new RecordingSigner();

        Invoice first = Draft();
        AddLine(first);
        first.Issue(series, signer, null, "501234567", EntryUtc);

        Invoice second = Draft();
        AddLine(second);
        second.Issue(series, signer, first.Signature!.Value, "501234567", EntryUtc);

        signer.LastSource.Should().EndWith(first.Signature!.Value);
        second.DocumentNumber.Should().Be("FT SERIE2026/2");
    }

    /// <summary>
    /// The series number exists so that "the last document in this series" — the link the next
    /// signature chains onto — can be found by an ordering that is actually the sequence. Sorted
    /// as text, FT SERIE2026/9 comes after FT SERIE2026/10, so a chain built on the document
    /// number would break at every tenth document and nowhere else.
    /// </summary>
    [Fact]
    public void The_series_number_is_recorded_and_orders_the_way_the_number_does_not()
    {
        DocumentSeries series = ActiveSeries();
        var signer = new RecordingSigner();

        Invoice draft = Draft();
        AddLine(draft);
        draft.SeriesNumber.Should().Be(0);

        var issued = new List<Invoice>();

        for (int index = 0; index < 10; index++)
        {
            Invoice invoice = index == 0 ? draft : Draft();

            if (index > 0)
            {
                AddLine(invoice);
            }

            invoice.Issue(series, signer, null, "501234567", EntryUtc).IsSuccess.Should().BeTrue();
            issued.Add(invoice);
        }

        issued[8].DocumentNumber.Should().Be("FT SERIE2026/9");
        issued[9].DocumentNumber.Should().Be("FT SERIE2026/10");

        issued[8].SeriesNumber.Should().Be(9);
        issued[9].SeriesNumber.Should().Be(10);

        // The point of the whole field: by number the ninth wins, by sequence the tenth does.
        issued.OrderByDescending(invoice => invoice.DocumentNumber, StringComparer.Ordinal)
            .First().SeriesNumber.Should().Be(9);

        issued.OrderByDescending(invoice => invoice.SeriesNumber)
            .First().SeriesNumber.Should().Be(10);
    }

    /// <summary>
    /// A series is declared to the tax authority for one document type. Issuing the wrong kind
    /// into it would be reporting a document that does not exist in the series it claims.
    /// </summary>
    [Fact]
    public void A_series_for_another_document_type_is_refused_and_does_not_move()
    {
        DocumentSeries creditNotes = ActiveSeries(DocumentType.CreditNote, "NC2026");
        Invoice invoice = Draft();
        AddLine(invoice);

        invoice.Issue(creditNotes, new RecordingSigner(), null, "501234567", EntryUtc)
            .Error.Code.Should().Be("invoicing.document.series_type_mismatch");

        creditNotes.NextNumber.Should().Be(1);
    }

    /// <summary>
    /// The number was reported to the tax authority the moment it was issued, and a missing
    /// number is worse than a cancelled one.
    /// </summary>
    [Fact]
    public void Voiding_keeps_the_number_the_figures_and_the_place_in_the_chain()
    {
        DocumentSeries series = ActiveSeries();
        Invoice invoice = Draft();
        AddLine(invoice, unitPrice: 100m);
        invoice.Issue(series, new RecordingSigner(), null, "501234567", EntryUtc);
        invoice.ClearDomainEvents();

        invoice.Void("Raised against the wrong customer.", EntryUtc).IsSuccess.Should().BeTrue();

        invoice.Status.Should().Be(InvoiceStatus.Voided);
        invoice.DocumentNumber.Should().Be("FT SERIE2026/1");
        invoice.Taxes.GrossTotal.Should().Be(123.00m);
        invoice.Signature.Should().NotBeNull();
        invoice.VoidReason.Should().Be("Raised against the wrong customer.");
        invoice.DomainEvents.Should().ContainSingle();
    }

    [Fact]
    public void A_void_needs_a_reason_and_only_happens_once()
    {
        Invoice invoice = Draft();
        AddLine(invoice);
        invoice.Issue(ActiveSeries(), new RecordingSigner(), null, "501234567", EntryUtc);

        invoice.Void("  ", EntryUtc).Error.Code.Should().Be("invoicing.document.void_reason_required");
        invoice.Void("Wrong customer.", EntryUtc).IsSuccess.Should().BeTrue();
        invoice.Void("Again.", EntryUtc).Error.Code.Should().Be("invoicing.document.already_voided");
    }

    [Fact]
    public void A_draft_has_nothing_to_void()
    {
        Draft().Void("Never issued.", EntryUtc)
            .Error.Code.Should().Be("invoicing.document.not_issued");
    }

    /// <summary>
    /// The QR is a record of what was printed and handed over, not a live view. Rewriting field E
    /// when the document is voided would produce a payload that never matched any piece of paper.
    /// </summary>
    [Fact]
    public void The_qr_code_is_fixed_at_issue_and_survives_voiding_unchanged()
    {
        Invoice invoice = Draft();
        AddLine(invoice);
        invoice.Issue(ActiveSeries(), new RecordingSigner(), null, "501234567", EntryUtc);

        string issued = invoice.QrCode!;
        issued.Should().Contain("E:N");

        invoice.Void("Wrong customer.", EntryUtc);

        invoice.QrCode.Should().Be(issued);
    }
}
