using AutoPartsErp.Modules.Sales.Domain;
using AutoPartsErp.Modules.Sales.Domain.Orders;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Sales.Tests;

/// <summary>
/// What an order knows about having been invoiced.
/// <para>
/// Everything here arrives from an integration event rather than from a person, so the interesting
/// cases are the awkward ones a message queue produces: the same message twice, a message about a
/// document the order has moved on from, and two documents claiming the same order.
/// </para>
/// </summary>
public sealed class SalesOrderInvoicingTests
{
    private static readonly InvoiceRef Invoice = new(Guid.NewGuid());
    private static readonly InvoiceRef Another = new(Guid.NewGuid());
    private static readonly DateOnly IssuedOn = new(2026, 9, 7);

    /// <summary>
    /// Invoicing goods that have not shipped is a promise, and a promise with a document number
    /// on it has been reported to the tax authority and has VAT falling due against it.
    /// </summary>
    [Fact]
    public void An_order_cannot_be_invoiced_until_everything_on_it_has_gone_out()
    {
        (SalesOrder order, SalesOrderLineId lineId) = Fixture.ConfirmedWithLine(10);

        order.CanInvoice.Should().BeFalse();

        order.DispatchLine(lineId, Quantity.Each(4));
        order.Status.Should().Be(SalesOrderStatus.PartiallyDispatched);
        order.CanInvoice.Should().BeFalse();

        order.DispatchLine(lineId, Quantity.Each(6));
        order.Status.Should().Be(SalesOrderStatus.Dispatched);
        order.CanInvoice.Should().BeTrue();
    }

    [Fact]
    public void A_cancelled_order_can_never_be_invoiced()
    {
        (SalesOrder order, _) = Fixture.ConfirmedWithLine();
        order.Cancel("Customer changed their mind");

        order.CanInvoice.Should().BeFalse();
    }

    [Fact]
    public void Marking_it_invoiced_records_the_document_and_closes_it_to_a_second_one()
    {
        SalesOrder order = Dispatched();

        order.MarkInvoiced(Invoice, "FT SERIE2026/35", IssuedOn).IsSuccess.Should().BeTrue();

        order.IsInvoiced.Should().BeTrue();
        order.InvoiceId.Should().Be(Invoice);
        order.InvoiceDocumentNumber.Should().Be("FT SERIE2026/35");
        order.InvoicedOn.Should().Be(IssuedOn);
        order.CanInvoice.Should().BeFalse();
    }

    /// <summary>
    /// The outbox delivers at-least-once, so the same message arriving twice is the ordinary case
    /// and not an error. Refusing it would send a perfectly good message to the dead letters.
    /// </summary>
    [Fact]
    public void Being_told_about_the_same_document_twice_is_not_an_error()
    {
        SalesOrder order = Dispatched();

        order.MarkInvoiced(Invoice, "FT SERIE2026/35", IssuedOn);

        order.MarkInvoiced(Invoice, "FT SERIE2026/35", IssuedOn).IsSuccess.Should().BeTrue();
        order.InvoiceDocumentNumber.Should().Be("FT SERIE2026/35");
    }

    /// <summary>
    /// Two different documents claiming one order is a real contradiction rather than a late
    /// delivery, and it should reach a person.
    /// </summary>
    [Fact]
    public void Being_told_about_a_second_different_document_is_refused_and_names_the_first()
    {
        SalesOrder order = Dispatched();
        order.MarkInvoiced(Invoice, "FT SERIE2026/35", IssuedOn);

        Result second = order.MarkInvoiced(Another, "FT SERIE2026/36", IssuedOn);

        second.Error.Code.Should().Be("sales.order.already_invoiced");
        second.Error.Description.Should().Contain("FT SERIE2026/35");
        order.InvoiceDocumentNumber.Should().Be("FT SERIE2026/35");
    }

    /// <summary>
    /// Without this, voiding an invoice raised against the wrong customer would leave the order
    /// permanently unbillable — the document is cancelled, the goods are still gone, and the only
    /// remedy would be re-keying the order.
    /// </summary>
    [Fact]
    public void Voiding_the_document_makes_the_order_billable_again()
    {
        SalesOrder order = Dispatched();
        order.MarkInvoiced(Invoice, "FT SERIE2026/35", IssuedOn);

        order.ClearInvoice(Invoice);

        order.IsInvoiced.Should().BeFalse();
        order.InvoiceDocumentNumber.Should().BeNull();
        order.InvoicedOn.Should().BeNull();
        order.CanInvoice.Should().BeTrue();
    }

    /// <summary>
    /// A void for a superseded document must not unpick the one that replaced it. Redelivery
    /// makes this ordering genuinely possible, and getting it wrong bills the customer twice.
    /// </summary>
    [Fact]
    public void A_void_for_some_other_document_leaves_the_current_one_alone()
    {
        SalesOrder order = Dispatched();
        order.MarkInvoiced(Invoice, "FT SERIE2026/35", IssuedOn);
        order.ClearInvoice(Invoice);
        order.MarkInvoiced(Another, "FT SERIE2026/36", IssuedOn);

        order.ClearInvoice(Invoice);

        order.InvoiceId.Should().Be(Another);
        order.InvoiceDocumentNumber.Should().Be("FT SERIE2026/36");
    }

    [Fact]
    public void Marking_it_invoiced_needs_both_the_document_and_its_number()
    {
        SalesOrder order = Dispatched();

        order.MarkInvoiced(InvoiceRef.Empty, "FT SERIE2026/35", IssuedOn)
            .Error.Code.Should().Be("sales.order.invoice_reference_required");

        order.MarkInvoiced(Invoice, "  ", IssuedOn)
            .Error.Code.Should().Be("sales.order.invoice_number_required");

        order.IsInvoiced.Should().BeFalse();
    }

    private static SalesOrder Dispatched()
    {
        (SalesOrder order, SalesOrderLineId lineId) = Fixture.ConfirmedWithLine(10);
        order.DispatchLine(lineId, Quantity.Each(10));

        return order;
    }
}

/// <summary>
/// Where the price on a line came from, and what happens to that answer when somebody types over
/// it.
/// </summary>
public sealed class SalesOrderPriceSourceTests
{
    /// <summary>
    /// Three weeks later, "why did we charge that?" has two honest answers — the list said so, or
    /// a person decided. A line that still named the list after a person overrode it would give
    /// the third one, which is the only one nobody thinks to check.
    /// </summary>
    [Fact]
    public void Overriding_the_price_by_hand_forgets_the_list_it_came_from()
    {
        SalesOrder order = Fixture.NewDraft();

        SalesOrderLineId lineId = order.AddLine(
            Fixture.NewPart(),
            "BP-1188",
            "Brake pad set",
            Quantity.Each(4),
            Money.Of(24.50m, Currency.Eur),
            10m,
            23m,
            "TRADE26").Value;

        order.Lines.Single().PriceSource.Should().Be("TRADE26");

        order.ChangeLinePricing(lineId, Money.Of(21.00m, Currency.Eur), 0m)
            .IsSuccess.Should().BeTrue();

        SalesOrderLine line = order.Lines.Single();
        line.UnitPrice.Amount.Should().Be(21.00m);
        line.PriceSource.Should().BeNull();
    }

    /// <summary>A refused change leaves the line exactly as it was, source included.</summary>
    [Fact]
    public void A_refused_change_does_not_forget_anything()
    {
        SalesOrder order = Fixture.NewDraft();

        SalesOrderLineId lineId = order.AddLine(
            Fixture.NewPart(),
            "BP-1188",
            "Brake pad set",
            Quantity.Each(4),
            Money.Of(24.50m, Currency.Eur),
            0m,
            23m,
            "TRADE26").Value;

        order.ChangeLinePricing(lineId, Money.Of(21.00m, Currency.Eur), 150m)
            .Error.Code.Should().Be("sales.line.discount_range");

        SalesOrderLine line = order.Lines.Single();
        line.UnitPrice.Amount.Should().Be(24.50m);
        line.PriceSource.Should().Be("TRADE26");
    }
}
