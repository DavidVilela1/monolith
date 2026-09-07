using AutoPartsErp.Modules.Finance.Domain;
using AutoPartsErp.Modules.Finance.Domain.Receivables;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Finance.Tests;

/// <summary>One line of a customer's account, and what may be done to it.</summary>
public sealed class OpenItemTests
{
    [Fact]
    public void An_invoice_is_owed_by_the_customer_and_starts_fully_outstanding()
    {
        OpenItem item = Sample.Invoice(100m);

        item.IsDebit.Should().BeTrue();
        item.IsCredit.Should().BeFalse();
        item.Status.Should().Be(OpenItemStatus.Open);
        item.Outstanding.Amount.Should().Be(100m);
        item.SettledAmount.Amount.Should().Be(0m);
        item.SignedOutstanding.Amount.Should().Be(100m);
    }

    /// <summary>
    /// A credit note is stored positive and counted negative.
    /// <para>
    /// Storing it as minus a hundred would make every sum in the module right and every row
    /// unreadable. Which way an item points is what kind of document it is, and the arithmetic
    /// asks for a sign only where it needs one.
    /// </para>
    /// </summary>
    [Fact]
    public void A_credit_note_is_held_positive_and_counts_against_the_balance()
    {
        OpenItem note = Sample.CreditNote(40m);

        note.IsCredit.Should().BeTrue();
        note.OriginalAmount.Amount.Should().Be(40m);
        note.SignedOutstanding.Amount.Should().Be(-40m);
    }

    [Fact]
    public void A_document_for_nothing_is_refused()
    {
        Result<OpenItem> zero = OpenItem.Raise(
            Sample.Customer,
            new DocumentRef(Guid.NewGuid()),
            "FT 2026/1",
            OpenItemKind.Invoice,
            Sample.Eur(0m),
            Sample.Raised,
            Sample.Due);

        zero.Error.Code.Should().Be("finance.open_item.amount_not_positive");
    }

    [Fact]
    public void A_document_with_no_number_is_refused_because_the_customer_will_quote_it_back()
    {
        Result<OpenItem> unnumbered = OpenItem.Raise(
            Sample.Customer,
            new DocumentRef(Guid.NewGuid()),
            "  ",
            OpenItemKind.Invoice,
            Sample.Eur(10m),
            Sample.Raised,
            Sample.Due);

        unnumbered.Error.Code.Should().Be("finance.open_item.document_number_required");
    }

    [Fact]
    public void A_due_date_before_the_document_is_pulled_forward_rather_than_refused()
    {
        // Odd, and not worth refusing: the document has already gone to the customer, and a sales
        // ledger that cannot record it is worse than one holding a strange date.
        OpenItem item = OpenItem.Raise(
            Sample.Customer,
            new DocumentRef(Guid.NewGuid()),
            "FT 2026/9",
            OpenItemKind.Invoice,
            Sample.Eur(10m),
            new DateOnly(2026, 9, 7),
            new DateOnly(2026, 9, 1)).Value;

        item.DueDate.Should().Be(new DateOnly(2026, 9, 7));
    }

    [Fact]
    public void Overdue_is_counted_from_the_day_after_the_due_date()
    {
        OpenItem item = Sample.Invoice(100m, due: new DateOnly(2026, 10, 7));

        item.IsOverdueOn(new DateOnly(2026, 10, 7)).Should().BeFalse("the due date is not late");
        item.DaysOverdueOn(new DateOnly(2026, 10, 7)).Should().Be(0);

        item.IsOverdueOn(new DateOnly(2026, 10, 8)).Should().BeTrue();
        item.DaysOverdueOn(new DateOnly(2026, 11, 6)).Should().Be(30);
    }

    [Fact]
    public void A_settled_item_is_never_overdue_however_late_it_was_paid()
    {
        OpenItem item = Sample.Invoice(100m, due: new DateOnly(2026, 10, 7));

        Settlement.Apply(
            Sample.Receipt(100m),
            [new SettlementLine(item, Sample.Eur(100m))],
            new DateOnly(2027, 1, 1)).IsSuccess.Should().BeTrue();

        item.Status.Should().Be(OpenItemStatus.Settled);
        item.IsOverdueOn(new DateOnly(2027, 6, 1)).Should().BeFalse();
    }

    [Fact]
    public void A_voided_document_comes_off_the_account()
    {
        OpenItem item = Sample.Invoice(100m);

        item.Cancel("Raised against the wrong customer").IsSuccess.Should().BeTrue();

        item.Status.Should().Be(OpenItemStatus.Cancelled);
        item.IsOutstanding.Should().BeFalse();
        item.CancellationReason.Should().Be("Raised against the wrong customer");
    }

    [Fact]
    public void Cancelling_twice_is_not_an_error()
    {
        OpenItem item = Sample.Invoice(100m);

        item.Cancel("Wrong customer").IsSuccess.Should().BeTrue();

        // The outbox delivers at least once, so the void handler will see the same message twice
        // sooner or later. Refusing the second one would turn redelivery into a failure.
        item.Cancel("Wrong customer").IsSuccess.Should().BeTrue();
    }

    /// <summary>A document somebody has already paid cannot be quietly taken off the account.</summary>
    [Fact]
    public void A_voided_document_that_was_already_paid_is_refused()
    {
        OpenItem item = Sample.Invoice(100m);

        Settlement.Apply(
            Sample.Receipt(60m),
            [new SettlementLine(item, Sample.Eur(60m))],
            new DateOnly(2026, 9, 20)).IsSuccess.Should().BeTrue();

        Result cancelled = item.Cancel("Voided in error");

        cancelled.Error.Code.Should().Be("finance.open_item.cancelled_after_settlement");
        cancelled.Error.Description.Should().Contain("60.00");

        // Still on the account and still owed, because the money and the receipt both still exist.
        item.Status.Should().Be(OpenItemStatus.PartiallySettled);
        item.Outstanding.Amount.Should().Be(40m);
    }

    [Fact]
    public void Nothing_can_be_matched_against_a_cancelled_item()
    {
        OpenItem item = Sample.Invoice(100m);
        item.Cancel("Wrong customer");

        Result settled = Settlement.Apply(
            Sample.Receipt(100m),
            [new SettlementLine(item, Sample.Eur(100m))],
            new DateOnly(2026, 9, 20));

        settled.IsFailure.Should().BeTrue();
    }
}
