using AutoPartsErp.Modules.Finance.Domain;
using AutoPartsErp.Modules.Finance.Domain.Receipts;
using AutoPartsErp.Modules.Finance.Domain.Receivables;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Finance.Tests;

/// <summary>
/// Matching money and credit against the documents they pay.
/// <para>
/// This is the operation the whole choice of design rests on. A module that only kept a balance
/// would have none of these rules to get right, and none of the questions they answer either.
/// </para>
/// </summary>
public sealed class SettlementTests
{
    private static readonly DateOnly Today = new(2026, 9, 20);

    [Fact]
    public void One_receipt_pays_two_invoices_and_each_records_its_share()
    {
        OpenItem first = Sample.Invoice(300m);
        OpenItem second = Sample.Invoice(250m);
        Receipt receipt = Sample.Receipt(500m);

        Result applied = Settlement.Apply(
            receipt,
            [
                new SettlementLine(first, Sample.Eur(300m)),
                new SettlementLine(second, Sample.Eur(200m)),
            ],
            Today);

        applied.IsSuccess.Should().BeTrue();

        first.Status.Should().Be(OpenItemStatus.Settled);
        first.Outstanding.Amount.Should().Be(0m);

        second.Status.Should().Be(OpenItemStatus.PartiallySettled);
        second.Outstanding.Amount.Should().Be(50m);

        receipt.Status.Should().Be(ReceiptStatus.Allocated);
        receipt.Unallocated.Amount.Should().Be(0m);
        receipt.Allocations.Should().HaveCount(2);

        // The document number is copied onto the allocation, so a statement reads without a join
        // and stays readable if anything upstream is later corrected.
        receipt.Allocations.Select(allocation => allocation.DocumentNumber)
            .Should().BeEquivalentTo([first.DocumentNumber, second.DocumentNumber]);
    }

    [Fact]
    public void Money_nobody_has_matched_yet_stays_on_the_receipt()
    {
        OpenItem invoice = Sample.Invoice(120m);
        Receipt receipt = Sample.Receipt(500m);

        Settlement.Apply(receipt, [new SettlementLine(invoice, Sample.Eur(120m))], Today)
            .IsSuccess.Should().BeTrue();

        // A transfer arrives with a reference nobody can read. It is real money on a real date,
        // and the balance should show it whether or not anybody has worked out what it paid.
        receipt.Status.Should().Be(ReceiptStatus.PartiallyAllocated);
        receipt.Unallocated.Amount.Should().Be(380m);
    }

    [Fact]
    public void A_receipt_cannot_pay_more_than_it_was_for()
    {
        OpenItem invoice = Sample.Invoice(300m);
        Receipt receipt = Sample.Receipt(100m);

        Result applied = Settlement.Apply(
            receipt, [new SettlementLine(invoice, Sample.Eur(300m))], Today);

        applied.Error.Code.Should().Be("finance.settlement.exceeds_unallocated");
    }

    [Fact]
    public void An_invoice_cannot_be_paid_twice()
    {
        OpenItem invoice = Sample.Invoice(100m);

        Settlement.Apply(Sample.Receipt(100m), [new SettlementLine(invoice, Sample.Eur(100m))], Today)
            .IsSuccess.Should().BeTrue();

        Result again = Settlement.Apply(
            Sample.Receipt(100m), [new SettlementLine(invoice, Sample.Eur(100m))], Today);

        again.Error.Code.Should().Be("finance.settlement.nothing_outstanding");
    }

    [Fact]
    public void More_than_is_outstanding_is_refused_and_says_how_much_is_left()
    {
        OpenItem invoice = Sample.Invoice(100m);

        Settlement.Apply(Sample.Receipt(60m), [new SettlementLine(invoice, Sample.Eur(60m))], Today);

        Result tooMuch = Settlement.Apply(
            Sample.Receipt(100m), [new SettlementLine(invoice, Sample.Eur(50m))], Today);

        tooMuch.Error.Code.Should().Be("finance.settlement.exceeds_outstanding");
        tooMuch.Error.Description.Should().Contain("40.00");
    }

    /// <summary>
    /// Nothing is changed until everything has been checked.
    /// <para>
    /// A four-line allocation that failed on the fourth line would otherwise leave three invoices
    /// settled and the receipt part spent, in memory, with no transaction to roll back — because
    /// nothing has reached the database yet.
    /// </para>
    /// </summary>
    [Fact]
    public void A_settlement_that_fails_leaves_nothing_half_applied()
    {
        OpenItem good = Sample.Invoice(100m);
        OpenItem tooSmall = Sample.Invoice(20m);
        Receipt receipt = Sample.Receipt(500m);

        Result applied = Settlement.Apply(
            receipt,
            [
                new SettlementLine(good, Sample.Eur(100m)),
                new SettlementLine(tooSmall, Sample.Eur(80m)),
            ],
            Today);

        applied.IsFailure.Should().BeTrue();

        good.Status.Should().Be(OpenItemStatus.Open, "the first line must not have been applied");
        good.Outstanding.Amount.Should().Be(100m);
        receipt.Status.Should().Be(ReceiptStatus.Unallocated);
        receipt.Allocations.Should().BeEmpty();
    }

    [Fact]
    public void The_same_document_twice_is_a_caller_who_lost_track()
    {
        OpenItem invoice = Sample.Invoice(300m);

        Result applied = Settlement.Apply(
            Sample.Receipt(300m),
            [
                new SettlementLine(invoice, Sample.Eur(100m)),
                new SettlementLine(invoice, Sample.Eur(200m)),
            ],
            Today);

        applied.Error.Code.Should().Be("finance.settlement.duplicate_item");
    }

    [Fact]
    public void A_receipt_cannot_pay_another_customers_invoice()
    {
        OpenItem theirs = Sample.Invoice(100m, customer: new CustomerRef(Guid.NewGuid()));

        Result applied = Settlement.Apply(
            Sample.Receipt(100m), [new SettlementLine(theirs, Sample.Eur(100m))], Today);

        applied.Error.Code.Should().Be("finance.settlement.different_customer");
    }

    [Fact]
    public void Currencies_are_never_converted_quietly()
    {
        OpenItem inSterling = Sample.Invoice(100m, currency: Currency.Gbp);

        Result applied = Settlement.Apply(
            Sample.Receipt(100m), [new SettlementLine(inSterling, Sample.Eur(100m))], Today);

        applied.Error.Code.Should().Be("finance.settlement.currency_mismatch");
    }

    [Fact]
    public void A_receipt_cannot_be_pointed_at_a_credit_note()
    {
        OpenItem note = Sample.CreditNote(40m);

        Result applied = Settlement.Apply(
            Sample.Receipt(40m), [new SettlementLine(note, Sample.Eur(40m))], Today);

        applied.Error.Code.Should().Be("finance.settlement.receipt_against_credit");
    }

    [Fact]
    public void A_credit_note_offsets_an_invoice_and_both_sides_are_consumed()
    {
        OpenItem invoice = Sample.Invoice(100m);
        OpenItem note = Sample.CreditNote(40m);

        Settlement.ApplyCredit(note, [new SettlementLine(invoice, Sample.Eur(40m))])
            .IsSuccess.Should().BeTrue();

        invoice.Outstanding.Amount.Should().Be(60m);
        invoice.Status.Should().Be(OpenItemStatus.PartiallySettled);

        // The credit carries its own record of how much of it is left, so nothing has to add up
        // the other side to find out.
        note.Outstanding.Amount.Should().Be(0m);
        note.Status.Should().Be(OpenItemStatus.Settled);
    }

    [Fact]
    public void A_credit_note_cannot_give_back_more_than_it_was_for()
    {
        OpenItem invoice = Sample.Invoice(100m);
        OpenItem note = Sample.CreditNote(40m);

        Result applied = Settlement.ApplyCredit(
            note, [new SettlementLine(invoice, Sample.Eur(60m))]);

        applied.Error.Code.Should().Be("finance.settlement.exceeds_outstanding");
        invoice.Outstanding.Amount.Should().Be(100m);
    }

    [Fact]
    public void A_credit_note_spread_over_two_invoices_is_used_up_exactly_once()
    {
        OpenItem first = Sample.Invoice(30m);
        OpenItem second = Sample.Invoice(30m);
        OpenItem note = Sample.CreditNote(50m);

        Settlement.ApplyCredit(
            note,
            [
                new SettlementLine(first, Sample.Eur(30m)),
                new SettlementLine(second, Sample.Eur(20m)),
            ]).IsSuccess.Should().BeTrue();

        first.Status.Should().Be(OpenItemStatus.Settled);
        second.Outstanding.Amount.Should().Be(10m);
        note.Outstanding.Amount.Should().Be(0m);

        Result again = Settlement.ApplyCredit(
            note, [new SettlementLine(second, Sample.Eur(10m))]);

        again.Error.Code.Should().Be("finance.settlement.credit_exhausted");
    }

    [Fact]
    public void An_invoice_offered_as_a_credit_is_refused()
    {
        OpenItem invoice = Sample.Invoice(100m);
        OpenItem other = Sample.Invoice(50m);

        Result applied = Settlement.ApplyCredit(
            invoice, [new SettlementLine(other, Sample.Eur(50m))]);

        applied.Error.Code.Should().Be("finance.settlement.not_a_credit_note");
    }

    [Fact]
    public void A_credit_note_cannot_be_allocated_against_itself()
    {
        OpenItem note = Sample.CreditNote(40m);

        Result applied = Settlement.ApplyCredit(
            note, [new SettlementLine(note, Sample.Eur(40m))]);

        applied.Error.Code.Should().Be("finance.settlement.credit_against_itself");
    }

    [Fact]
    public void Allocating_nothing_is_refused_rather_than_quietly_doing_nothing()
    {
        Result applied = Settlement.Apply(Sample.Receipt(100m), [], Today);

        applied.Error.Code.Should().Be("finance.settlement.nothing_to_allocate");
    }
}
