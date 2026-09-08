using AutoPartsErp.Modules.Sales.Domain;
using AutoPartsErp.Modules.Sales.Domain.Orders;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Sales.Tests;

/// <summary>
/// What an order knows about having been billed.
/// <para>
/// The rule these tests exist to hold is one line long and easy to break: an order can be charged
/// for what has shipped, and never for more. Everything below is that sentence under the awkward
/// conditions a message queue produces — the same document twice, a document arriving after the
/// goods it charged for were already billed, and a void turning up long after the order moved on.
/// </para>
/// <para>
/// There is no <c>InvoiceId</c> on the order any more, and that absence is the design: an order
/// that ships in three lorries is billed by three documents, so the order keeps per-line
/// quantities and Invoicing keeps the documents.
/// </para>
/// </summary>
public sealed class SalesOrderInvoicingTests
{
    private static readonly DateOnly IssuedOn = new(2026, 9, 7);

    /// <summary>
    /// Invoicing goods that have not shipped is a promise, and a promise with a document number on
    /// it has been declared to the tax authority and has VAT falling due against it.
    /// </summary>
    [Fact]
    public void An_order_cannot_be_invoiced_until_something_on_it_has_gone_out()
    {
        (SalesOrder order, SalesOrderLineId lineId) = Fixture.ConfirmedWithLine(10);

        order.CanInvoice.Should().BeFalse();

        order.DispatchLine(lineId, Quantity.Each(4));

        order.Status.Should().Be(SalesOrderStatus.PartiallyDispatched);
        order.CanInvoice.Should().BeTrue();
    }

    /// <summary>
    /// The change this whole feature is about. Under the old rule the customer waited for the
    /// back-ordered half of the line before being charged for the half they already had.
    /// </summary>
    [Fact]
    public void A_partly_dispatched_order_is_billable_for_exactly_what_shipped()
    {
        (SalesOrder order, SalesOrderLineId lineId) = Fixture.ConfirmedWithLine(10);
        order.DispatchLine(lineId, Quantity.Each(4));

        SalesOrderLine line = order.Lines.Single();

        line.BillableQuantity.Should().Be(Quantity.Each(4));
        line.IsBillable.Should().BeTrue();
        line.IsFullyInvoiced.Should().BeFalse();
    }

    [Fact]
    public void A_cancelled_order_can_never_be_invoiced()
    {
        (SalesOrder order, _) = Fixture.ConfirmedWithLine();
        order.Cancel("Customer changed their mind");

        order.CanInvoice.Should().BeFalse();
    }

    [Fact]
    public void Billing_what_shipped_leaves_the_order_partly_invoiced_and_still_billable_later()
    {
        (SalesOrder order, SalesOrderLineId lineId) = Fixture.ConfirmedWithLine(10);
        order.DispatchLine(lineId, Quantity.Each(4));

        order.RecordBilling(Billed(lineId, 4), IssuedOn).IsSuccess.Should().BeTrue();

        order.InvoicingStatus.Should().Be(SalesOrderInvoicingStatus.PartiallyInvoiced);
        order.LastInvoicedOn.Should().Be(IssuedOn);
        order.Lines.Single().InvoicedQuantity.Should().Be(Quantity.Each(4));

        // Nothing left to bill today. Not because the order is finished - because the rest of it
        // is still in the warehouse.
        order.CanInvoice.Should().BeFalse();

        order.DispatchLine(lineId, Quantity.Each(6));

        order.CanInvoice.Should().BeTrue();
        order.Lines.Single().BillableQuantity.Should().Be(Quantity.Each(6));
    }

    /// <summary>
    /// Fully invoiced is a statement about the lines, not about a document existing. It is only
    /// reached when everything ordered has shipped and everything shipped has been charged for.
    /// </summary>
    [Fact]
    public void An_order_is_fully_invoiced_only_once_every_line_is()
    {
        (SalesOrder order, SalesOrderLineId lineId) = Fixture.ConfirmedWithLine(10);
        order.DispatchLine(lineId, Quantity.Each(10));

        order.RecordBilling(Billed(lineId, 6), IssuedOn);
        order.InvoicingStatus.Should().Be(SalesOrderInvoicingStatus.PartiallyInvoiced);

        order.RecordBilling(Billed(lineId, 4), IssuedOn);

        order.InvoicingStatus.Should().Be(SalesOrderInvoicingStatus.Invoiced);
        order.IsInvoiced.Should().BeTrue();
        order.CanInvoice.Should().BeFalse();
        order.Lines.Single().IsFullyInvoiced.Should().BeTrue();
    }

    /// <summary>
    /// The one thing that must never happen: two documents charging the customer for the same
    /// goods. The order is the last thing standing between a redelivered message and a double
    /// charge, so it refuses and names the SKU and what was actually left.
    /// </summary>
    [Fact]
    public void Billing_more_than_went_out_is_refused_and_says_what_was_left()
    {
        (SalesOrder order, SalesOrderLineId lineId) = Fixture.ConfirmedWithLine(10);
        order.DispatchLine(lineId, Quantity.Each(4));

        Result over = order.RecordBilling(Billed(lineId, 5), IssuedOn);

        over.Error.Code.Should().Be("sales.line.over_billed");
        over.Error.Description.Should().Contain("BP-1188");
        over.Error.Description.Should().Contain("4");

        // And nothing was applied: a refusal has to leave the order exactly as it was, because
        // there is no transaction underneath an in-memory mutation to roll back.
        order.Lines.Single().InvoicedQuantity.Value.Should().Be(0m);
        order.InvoicingStatus.Should().Be(SalesOrderInvoicingStatus.NotInvoiced);
    }

    /// <summary>
    /// A document billing two lines, one of which is over the line. The first line must not be
    /// left charged: the check runs over everything before anything is applied.
    /// </summary>
    [Fact]
    public void A_document_that_fails_on_its_second_line_bills_neither()
    {
        SalesOrder order = Fixture.NewDraft();

        SalesOrderLineId first = order.AddLine(
            Fixture.NewPart(), "BP-1188", "Brake pad set", Quantity.Each(10),
            Money.Of(24.50m, Currency.Eur), 0m, 23m).Value;

        SalesOrderLineId second = order.AddLine(
            Fixture.NewPart(), "OF-4471", "Oil filter", Quantity.Each(10),
            Money.Of(9.90m, Currency.Eur), 0m, 23m).Value;

        order.Confirm(Fixture.Today);
        order.DispatchLine(first, Quantity.Each(10));
        order.DispatchLine(second, Quantity.Each(2));

        var billed = new Dictionary<SalesOrderLineId, Quantity>
        {
            [first] = Quantity.Each(10),
            [second] = Quantity.Each(5),
        };

        order.RecordBilling(billed, IssuedOn).Error.Code.Should().Be("sales.line.over_billed");

        order.Lines.Should().OnlyContain(line => line.InvoicedQuantity.Value == 0m);
        order.InvoicingStatus.Should().Be(SalesOrderInvoicingStatus.NotInvoiced);
    }

    [Fact]
    public void Billing_nothing_at_all_is_refused()
    {
        (SalesOrder order, SalesOrderLineId lineId) = Fixture.ConfirmedWithLine(10);
        order.DispatchLine(lineId, Quantity.Each(10));

        order.RecordBilling(new Dictionary<SalesOrderLineId, Quantity>(), IssuedOn)
            .Error.Code.Should().Be("sales.order.nothing_billed");
    }

    [Fact]
    public void Billing_a_line_that_is_not_on_the_order_is_refused()
    {
        (SalesOrder order, SalesOrderLineId lineId) = Fixture.ConfirmedWithLine(10);
        order.DispatchLine(lineId, Quantity.Each(10));

        order.RecordBilling(Billed(new SalesOrderLineId(Guid.NewGuid()), 1), IssuedOn)
            .Error.Code.Should().Be("sales.line.not_found");
    }

    /// <summary>
    /// Without this, voiding a document raised against the wrong customer would leave those goods
    /// permanently unbillable, and the only remedy would be re-keying the order.
    /// </summary>
    [Fact]
    public void Voiding_the_document_makes_what_it_charged_for_billable_again()
    {
        (SalesOrder order, SalesOrderLineId lineId) = Fixture.ConfirmedWithLine(10);
        order.DispatchLine(lineId, Quantity.Each(10));
        order.RecordBilling(Billed(lineId, 10), IssuedOn);

        order.ReverseBilling(Billed(lineId, 10)).IsSuccess.Should().BeTrue();

        order.InvoicingStatus.Should().Be(SalesOrderInvoicingStatus.NotInvoiced);
        order.IsInvoiced.Should().BeFalse();
        order.CanInvoice.Should().BeTrue();
        order.Lines.Single().BillableQuantity.Should().Be(Quantity.Each(10));
    }

    /// <summary>
    /// Voiding one of two documents gives back only what that one charged for. Getting this wrong
    /// in either direction bills the customer twice or never.
    /// </summary>
    [Fact]
    public void Voiding_one_document_leaves_what_the_other_one_charged_for_alone()
    {
        (SalesOrder order, SalesOrderLineId lineId) = Fixture.ConfirmedWithLine(10);
        order.DispatchLine(lineId, Quantity.Each(10));
        order.RecordBilling(Billed(lineId, 6), IssuedOn);
        order.RecordBilling(Billed(lineId, 4), IssuedOn);

        order.ReverseBilling(Billed(lineId, 4));

        order.Lines.Single().InvoicedQuantity.Should().Be(Quantity.Each(6));
        order.InvoicingStatus.Should().Be(SalesOrderInvoicingStatus.PartiallyInvoiced);
    }

    /// <summary>
    /// A void that gives back more than the line currently carries is applied as far as it goes
    /// rather than refused. By the time it arrives the line may have been credited by another
    /// route, and refusing would stop the whole message instead of the part of it that no longer
    /// applies — sending a correct void to the dead letters.
    /// </summary>
    [Fact]
    public void A_void_for_more_than_is_there_gives_back_what_is_there()
    {
        (SalesOrder order, SalesOrderLineId lineId) = Fixture.ConfirmedWithLine(10);
        order.DispatchLine(lineId, Quantity.Each(10));
        order.RecordBilling(Billed(lineId, 4), IssuedOn);

        order.ReverseBilling(Billed(lineId, 10)).IsSuccess.Should().BeTrue();

        order.Lines.Single().InvoicedQuantity.Value.Should().Be(0m);
        order.InvoicingStatus.Should().Be(SalesOrderInvoicingStatus.NotInvoiced);
    }

    /// <summary>A void naming a line this order does not have is ignored, not refused.</summary>
    [Fact]
    public void A_void_naming_a_line_that_is_not_here_changes_nothing()
    {
        (SalesOrder order, SalesOrderLineId lineId) = Fixture.ConfirmedWithLine(10);
        order.DispatchLine(lineId, Quantity.Each(10));
        order.RecordBilling(Billed(lineId, 10), IssuedOn);

        order.ReverseBilling(Billed(new SalesOrderLineId(Guid.NewGuid()), 3))
            .IsSuccess.Should().BeTrue();

        order.Lines.Single().InvoicedQuantity.Should().Be(Quantity.Each(10));
        order.InvoicingStatus.Should().Be(SalesOrderInvoicingStatus.Invoiced);
    }

    private static Dictionary<SalesOrderLineId, Quantity> Billed(SalesOrderLineId lineId, int amount)
        => new() { [lineId] = Quantity.Each(amount) };
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
