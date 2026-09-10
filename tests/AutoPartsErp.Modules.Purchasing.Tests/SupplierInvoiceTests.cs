using AutoPartsErp.Modules.Purchasing.Domain;
using AutoPartsErp.Modules.Purchasing.Domain.Invoices;
using AutoPartsErp.Modules.Purchasing.Domain.Invoices.Events;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Purchasing.Tests;

/// <summary>
/// The supplier's document, written by the system from what the warehouse counted.
/// <para>
/// Automatic entry is not automatic acceptance. The whole reason for computing the figure
/// independently — from receipts the warehouse confirmed and prices agreed months ago — is to have
/// something to disagree with when their paper says otherwise. A system that simply recorded
/// whatever arrived would be a filing cabinet.
/// </para>
/// </summary>
public sealed class SupplierInvoiceTests
{
    private static readonly DateOnly Delivered = new(2026, 8, 12);

    private static Money Eur(decimal amount) => Money.Of(amount, Currency.Eur);

    private static Money Cents(int cents) => Eur(cents / 100m);

    /// <summary>
    /// Nobody typed a line. The quantity is what came off the van and the price is what was
    /// agreed, and the document adds itself up.
    /// </summary>
    [Fact]
    public void A_document_prices_itself_from_the_receipt_and_the_agreement()
    {
        SupplierInvoice invoice = Draft();

        Receive(invoice, 100, 4.50m);
        Receive(invoice, 20, 12.00m);

        invoice.Lines.Should().HaveCount(2);
        invoice.LinesTotal.Amount.Should().Be(690.00m);
        invoice.NetTotal.Amount.Should().Be(690.00m);
        invoice.VatTotal.Amount.Should().Be(158.70m);
        invoice.GrossTotal.Amount.Should().Be(848.70m);
    }

    /// <summary>
    /// The rebate is worked out on the lines together and rounded once. Four per cent taken off
    /// each of eleven lines and added up is a different figure from four per cent of the eleven
    /// together, and the supplier's document will have done it the second way.
    /// </summary>
    [Fact]
    public void The_rebate_comes_off_the_document_and_the_vat_follows_it_down()
    {
        SupplierInvoice invoice = Draft(rappel: 4m);

        Receive(invoice, 100, 4.50m);
        Receive(invoice, 20, 12.00m);

        invoice.LinesTotal.Amount.Should().Be(690.00m);
        invoice.RappelAmount.Amount.Should().Be(27.60m);
        invoice.NetTotal.Amount.Should().Be(662.40m);

        // VAT on 662,40 and not on 690,00. A rebate reduces the taxable amount, so VAT charged on
        // the pre-rebate figure is VAT the company would be deducting without having paid it.
        invoice.VatTotal.Amount.Should().Be(152.35m);
        invoice.GrossTotal.Amount.Should().Be(814.75m);
    }

    /// <summary>
    /// A van carrying parts at 23 and books at 6 is ordinary. One blended percentage would match
    /// neither the supplier's document nor the return the company has to file, so the rebate is
    /// spread across the bands in proportion to what each is worth.
    /// </summary>
    [Fact]
    public void Vat_is_computed_per_rate_with_the_rebate_shared_between_them()
    {
        SupplierInvoice invoice = Draft(rappel: 4m);

        Receive(invoice, 100, 4.50m, vat: 23m);
        Receive(invoice, 20, 12.00m, vat: 6m);

        invoice.RappelAmount.Amount.Should().Be(27.60m);
        invoice.NetTotal.Amount.Should().Be(662.40m);

        // 450,00 less 18,00 at 23% is 99,36; 240,00 less 9,60 at 6% is 13,82.
        invoice.VatTotal.Amount.Should().Be(113.18m);
        invoice.GrossTotal.Amount.Should().Be(775.58m);
    }

    /// <summary>
    /// The trap the agreement already refuses on its side, closed again here. A rebate settled by
    /// credit note takes nothing off this document — the company pays the full figure and is
    /// credited later — and a draft opened at that rate would take the same discount twice.
    /// </summary>
    [Fact]
    public void A_draft_with_no_rebate_rate_takes_nothing_off()
    {
        SupplierInvoice invoice = Draft(rappel: 0m);

        Receive(invoice, 100, 4.50m);

        invoice.RappelAmount.Amount.Should().Be(0m);
        invoice.NetTotal.Should().Be(invoice.LinesTotal);
    }

    /// <summary>Their paper agrees with what was counted, and that is the end of it.</summary>
    [Fact]
    public void A_document_that_agrees_is_settled_and_says_what_is_owed()
    {
        SupplierInvoice invoice = Draft(rappel: 4m);
        Receive(invoice, 100, 4.50m);
        Receive(invoice, 20, 12.00m);
        invoice.ClearDomainEvents();

        invoice.Reconcile("FT 2026/14872", Delivered, Eur(814.75m), Cents(2))
            .IsSuccess.Should().BeTrue();

        invoice.Status.Should().Be(SupplierInvoiceStatus.Matched);
        invoice.Difference!.Amount.Should().Be(0m);

        SupplierInvoiceSettledDomainEvent settled =
            invoice.DomainEvents.OfType<SupplierInvoiceSettledDomainEvent>().Should().ContainSingle().Subject;

        settled.SupplierDocumentNumber.Should().Be("FT 2026/14872");
        settled.NetAmount.Should().Be(662.40m);
        settled.VatAmount.Should().Be(152.35m);
        settled.GrossAmount.Should().Be(814.75m);
    }

    /// <summary>
    /// Both sides round to the cent, and a document of forty lines at two rates will differ by one
    /// or two of them however carefully either side computes it. A tolerance of zero puts every
    /// delivery in front of a person to approve a cent, which is how people learn to approve
    /// everything without looking.
    /// </summary>
    [Fact]
    public void A_difference_of_a_cent_is_arithmetic_and_not_a_dispute()
    {
        SupplierInvoice invoice = Draft(rappel: 4m);
        Receive(invoice, 100, 4.50m);
        Receive(invoice, 20, 12.00m);

        invoice.Reconcile("FT 2026/14872", Delivered, Eur(814.76m), Cents(2));

        invoice.Status.Should().Be(SupplierInvoiceStatus.Matched);
        invoice.Difference!.Amount.Should().Be(0.01m);

        // And the payable is opened for their figure, not ours. A payable for anything else would
        // never match the money leaving the bank.
        invoice.DomainEvents.OfType<SupplierInvoiceSettledDomainEvent>()
            .Single().GrossAmount.Should().Be(814.76m);
    }

    /// <summary>
    /// Twelve euros apart is somebody's decision, not a rounding difference. The event carries
    /// both figures, because "we are 12,40 apart" is a number somebody has to go and reconstruct.
    /// </summary>
    [Fact]
    public void A_real_difference_is_put_in_front_of_somebody_with_both_figures()
    {
        SupplierInvoice invoice = Draft(rappel: 4m);
        Receive(invoice, 100, 4.50m);
        Receive(invoice, 20, 12.00m);
        invoice.ClearDomainEvents();

        invoice.Reconcile("FT 2026/14872", Delivered, Eur(827.15m), Cents(2))
            .IsSuccess.Should().BeTrue();

        invoice.Status.Should().Be(SupplierInvoiceStatus.Disputed);

        SupplierInvoiceDisputedDomainEvent disputed =
            invoice.DomainEvents.OfType<SupplierInvoiceDisputedDomainEvent>().Should().ContainSingle().Subject;

        disputed.ComputedGrossTotal.Amount.Should().Be(814.75m);
        disputed.StatedGrossTotal.Amount.Should().Be(827.15m);
        disputed.Difference.Amount.Should().Be(12.40m);

        // Nothing is owed yet. A disputed document has not settled anything.
        invoice.DomainEvents.OfType<SupplierInvoiceSettledDomainEvent>().Should().BeEmpty();
    }

    /// <summary>
    /// A supplier charging less than was agreed is just as much a difference as one charging more.
    /// A check that only looked upwards would wave through the credit that never arrived.
    /// </summary>
    [Fact]
    public void A_supplier_charging_less_than_agreed_is_also_a_difference()
    {
        SupplierInvoice invoice = Draft();
        Receive(invoice, 100, 4.50m);

        invoice.Reconcile("FT 2026/14873", Delivered, Eur(500.00m), Cents(2));

        invoice.Status.Should().Be(SupplierInvoiceStatus.Disputed);
        invoice.Difference!.Amount.Should().Be(-53.50m);
    }

    /// <summary>
    /// A deliberate act with a name on it, not a button that makes a warning go away. In a year's
    /// time the sentence is the only thing that will explain paying more than was counted.
    /// </summary>
    [Fact]
    public void Accepting_their_figure_needs_a_reason_and_then_owes_their_figure()
    {
        SupplierInvoice invoice = Disputed();

        invoice.AcceptSupplierFigure(null)
            .Error.Code.Should().Be("purchasing.supplier_invoice.accept_reason_required");

        invoice.ClearDomainEvents();

        invoice.AcceptSupplierFigure("Carriage they warned us about on the phone; agreed with Rui.")
            .IsSuccess.Should().BeTrue();

        invoice.Status.Should().Be(SupplierInvoiceStatus.Matched);
        invoice.Reason.Should().Contain("Rui");

        invoice.DomainEvents.OfType<SupplierInvoiceSettledDomainEvent>()
            .Single().GrossAmount.Should().Be(827.15m);
    }

    /// <summary>
    /// The other way out of a dispute: their price was right and ours was stale. The line moves,
    /// the document agrees, and the agreed price is left alone — one unchallenged invoice does not
    /// get to quietly become the new contract.
    /// </summary>
    [Fact]
    public void A_line_can_be_corrected_to_their_price_without_touching_the_agreement()
    {
        SupplierInvoice invoice = Draft();
        SupplierInvoiceLineId lineId = Receive(invoice, 100, 4.50m);

        invoice.Reconcile("FT 2026/14874", Delivered, Eur(567.03m), Cents(2));
        invoice.Status.Should().Be(SupplierInvoiceStatus.Disputed);

        // Correcting is only reachable after somebody has sent the document back, so it is always
        // something done after looking at a difference rather than before anyone noticed one.
        invoice.AcceptLinePrice(lineId, Eur(4.61m))
            .Error.Code.Should().Be("purchasing.supplier_invoice.not_open");

        invoice.Reopen("Their October rise; our agreed price is stale.").IsSuccess.Should().BeTrue();
        invoice.AcceptLinePrice(lineId, Eur(4.61m)).IsSuccess.Should().BeTrue();

        invoice.Lines.Single().UnitPrice.Amount.Should().Be(4.61m);
        invoice.Lines.Single().PriceSource.Should().BeNull();
        invoice.GrossTotal.Amount.Should().Be(567.03m);

        invoice.Reconcile("FT 2026/14874", Delivered, Eur(567.03m), Cents(2)).IsSuccess.Should().BeTrue();
        invoice.Status.Should().Be(SupplierInvoiceStatus.Matched);
    }

    /// <summary>
    /// The thread from what was ordered, through what was counted, to what is being charged.
    /// Without it, reconciling a statement means matching on description, and two brake pad sets
    /// with different part numbers have the same description.
    /// </summary>
    [Fact]
    public void Every_line_points_back_at_the_order_line_it_arrived_against()
    {
        SupplierInvoice invoice = Draft();
        var orderId = PurchaseOrderId.New();
        var orderLineId = PurchaseOrderLineId.New();

        invoice.AddReceivedLine(
            orderId, orderLineId, Fixture.NewPart(), "0986452041", "Oil filter",
            Quantity.Each(100), Eur(4.50m), 23m).IsSuccess.Should().BeTrue();

        SupplierInvoiceLine line = invoice.Lines.Single();

        line.PurchaseOrderId.Should().Be(orderId);
        line.PurchaseOrderLineId.Should().Be(orderLineId);
    }

    /// <summary>A document that recorded nothing has nothing to check their figure against.</summary>
    [Fact]
    public void An_empty_draft_cannot_be_reconciled()
    {
        Draft().Reconcile("FT 2026/1", Delivered, Eur(10m), Cents(2))
            .Error.Code.Should().Be("purchasing.supplier_invoice.no_lines");
    }

    /// <summary>
    /// Either the date was typed wrong or this document belongs to a different delivery. Both are
    /// worth stopping, and neither is worth guessing at.
    /// </summary>
    [Fact]
    public void A_document_dated_before_the_goods_arrived_is_refused()
    {
        SupplierInvoice invoice = Draft();
        Receive(invoice, 100, 4.50m);

        invoice.Reconcile("FT 2026/1", Delivered.AddDays(-1), Eur(553.50m), Cents(2))
            .Error.Code.Should().Be("purchasing.supplier_invoice.document_before_delivery");
    }

    /// <summary>
    /// A settled document is money somebody owes. It goes away by being credited, not by being
    /// cancelled out from under the payable that was opened against it.
    /// </summary>
    [Fact]
    public void A_settled_document_cannot_be_cancelled_or_added_to()
    {
        SupplierInvoice invoice = Draft();
        Receive(invoice, 100, 4.50m);
        invoice.Reconcile("FT 2026/1", Delivered, Eur(553.50m), Cents(2));

        invoice.Cancel("Changed my mind")
            .Error.Code.Should().Be("purchasing.supplier_invoice.already_settled");

        invoice.AddReceivedLine(
            PurchaseOrderId.New(), PurchaseOrderLineId.New(), Fixture.NewPart(), "X", "Y",
            Quantity.Each(1), Eur(1m), 23m)
            .Error.Code.Should().Be("purchasing.supplier_invoice.not_open");
    }

    /// <summary>A purchase document that quietly converts is where exchange-rate losses hide.</summary>
    [Fact]
    public void A_price_in_another_currency_never_reaches_the_document()
    {
        SupplierInvoice invoice = Draft();

        invoice.AddReceivedLine(
            PurchaseOrderId.New(), PurchaseOrderLineId.New(), Fixture.NewPart(), "0986452041",
            "Oil filter", Quantity.Each(100), Money.Of(4.50m, Currency.Usd), 23m)
            .Error.Code.Should().Be("purchasing.supplier_invoice.currency_mismatch");

        invoice.Lines.Should().BeEmpty();
    }

    private static SupplierInvoice Draft(decimal rappel = 0m) =>
        SupplierInvoice.DraftFor(Fixture.Supplier, "BOSCH", Currency.Eur, Delivered, rappel).Value;

    private static SupplierInvoiceLineId Receive(
        SupplierInvoice invoice, int quantity, decimal unitPrice, decimal vat = 23m)
    {
        Result<SupplierInvoiceLineId> line = invoice.AddReceivedLine(
            PurchaseOrderId.New(),
            PurchaseOrderLineId.New(),
            Fixture.NewPart(),
            "0986452041",
            "Oil filter",
            Quantity.Each(quantity),
            Money.Of(unitPrice, Currency.Eur),
            vat);

        line.IsSuccess.Should().BeTrue();

        return line.Value;
    }

    /// <summary>A document their paper puts 12,40 above what was counted.</summary>
    private static SupplierInvoice Disputed()
    {
        SupplierInvoice invoice = Draft(rappel: 4m);
        Receive(invoice, 100, 4.50m);
        Receive(invoice, 20, 12.00m);
        invoice.Reconcile("FT 2026/14872", Delivered, Eur(827.15m), Cents(2));

        return invoice;
    }
}
