using AutoPartsErp.Modules.Finance.Domain;
using AutoPartsErp.Modules.Finance.Domain.Payables;
using AutoPartsErp.Modules.Finance.Domain.Payables.Events;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Finance.Tests;

/// <summary>
/// The purchase ledger: what the company owes a supplier, and when.
/// <para>
/// The mirror of the sales ledger, and deliberately a separate aggregate rather than a flag on it.
/// Merging them was tempting and the reason not to is a query that forgets the filter: one
/// afternoon it would net what a customer owes against what the company owes a supplier and report
/// a balance that is wrong in a way nobody would question.
/// </para>
/// </summary>
public sealed class PayableItemTests
{
    private static readonly DateOnly Invoiced = new(2026, 9, 1);

    private static Money Eur(decimal amount) => Money.Of(amount, Currency.Eur);

    /// <summary>
    /// The whole chain ends here: a purchase order, a counted pallet, an accepted invoice, and
    /// finally a figure somebody is going to pay.
    /// </summary>
    [Fact]
    public void An_accepted_document_lands_on_the_ledger_owing_its_full_amount()
    {
        PayableItem item = Raise(814.75m);

        item.SupplierCode.Should().Be("BOSCH");
        item.DocumentNumber.Should().Be("FT 2026/14872");
        item.OriginalAmount.Amount.Should().Be(814.75m);
        item.Outstanding.Amount.Should().Be(814.75m);
        item.Status.Should().Be(PayableItemStatus.Open);
        item.IsDebit.Should().BeTrue();

        PayableItemRaisedDomainEvent raised =
            item.DomainEvents.OfType<PayableItemRaisedDomainEvent>().Should().ContainSingle().Subject;

        raised.DueDate.Should().Be(Invoiced.AddDays(30));
        raised.Amount.Should().Be(814.75m);
    }

    /// <summary>
    /// Positive whichever way the item points. A negative <c>Money</c> in the database is a number
    /// nobody can read at a glance, so the direction is the kind and the sign is asked for.
    /// </summary>
    [Fact]
    public void A_credit_note_is_owed_the_other_way_and_still_stored_positive()
    {
        PayableItem credit = Raise(120.00m, PayableItemKind.CreditNote);

        credit.OriginalAmount.Amount.Should().Be(120.00m);
        credit.IsCredit.Should().BeTrue();
        credit.SignedOutstanding.Amount.Should().Be(-120.00m);
    }

    [Fact]
    public void An_untouched_item_can_be_withdrawn_with_a_reason()
    {
        PayableItem item = Raise(100.00m);

        item.Cancel(null).Error.Code.Should().Be("finance.payable.cancel_reason_required");

        item.Cancel("Duplicate of FT 2026/14871").IsSuccess.Should().BeTrue();

        item.Status.Should().Be(PayableItemStatus.Cancelled);
        item.IsOutstanding.Should().BeFalse();
        item.CancellationReason.Should().Contain("14871");

        item.Cancel("Again").Error.Code.Should().Be("finance.payable.cancelled");
    }

    /// <summary>What the payment run reads, and what a supplier rings up about.</summary>
    [Fact]
    public void It_knows_how_late_it_is()
    {
        PayableItem item = Raise(100.00m);
        DateOnly due = Invoiced.AddDays(30);

        item.IsOverdueOn(due).Should().BeFalse();
        item.DaysOverdueOn(due).Should().Be(0);

        item.IsOverdueOn(due.AddDays(14)).Should().BeTrue();
        item.DaysOverdueOn(due.AddDays(14)).Should().Be(14);

        // An item nobody owes any more is not late, however long it sat there.
        item.Cancel("Credited in full by NC 2026/91");
        item.IsOverdueOn(due.AddDays(14)).Should().BeFalse();
    }

    /// <summary>
    /// A supplier who dates their terms from the delivery rather than the invoice produces this
    /// legitimately, and refusing it would mean a document the company has already accepted cannot
    /// be recorded.
    /// </summary>
    [Fact]
    public void A_due_date_before_the_document_is_pulled_forward_rather_than_refused()
    {
        PayableItem item = PayableItem.Raise(
            new SupplierRef(Guid.NewGuid()),
            "BOSCH",
            new SupplierInvoiceRef(Guid.NewGuid()),
            "FT 2026/14872",
            PayableItemKind.Invoice,
            Eur(100.00m),
            Invoiced,
            Invoiced.AddDays(-10)).Value;

        item.DueDate.Should().Be(Invoiced);
    }

    /// <summary>
    /// A document for nothing is not something a purchase ledger should carry, and if Purchasing
    /// ever settles one that is worth finding out here rather than as a row nobody can pay off.
    /// </summary>
    [Fact]
    public void A_document_needs_a_supplier_a_number_and_an_amount()
    {
        Raising(supplier: SupplierRef.Empty)
            .Error.Code.Should().Be("finance.payable.supplier_required");

        Raising(number: " ")
            .Error.Code.Should().Be("finance.payable.document_number_required");

        Raising(amount: 0m)
            .Error.Code.Should().Be("finance.payable.amount_not_positive");

        Raising(document: SupplierInvoiceRef.Empty)
            .Error.Code.Should().Be("finance.payable.document_required");
    }

    private static Result<PayableItem> Raising(
        SupplierRef? supplier = null,
        SupplierInvoiceRef? document = null,
        string? number = "FT 2026/14872",
        decimal amount = 100.00m) =>
        PayableItem.Raise(
            supplier ?? new SupplierRef(Guid.NewGuid()),
            "BOSCH",
            document ?? new SupplierInvoiceRef(Guid.NewGuid()),
            number,
            PayableItemKind.Invoice,
            Eur(amount),
            Invoiced,
            Invoiced.AddDays(30));

    private static PayableItem Raise(
        decimal amount, PayableItemKind kind = PayableItemKind.Invoice) =>
        PayableItem.Raise(
            new SupplierRef(Guid.NewGuid()),
            "BOSCH",
            new SupplierInvoiceRef(Guid.NewGuid()),
            "FT 2026/14872",
            kind,
            Eur(amount),
            Invoiced,
            Invoiced.AddDays(30)).Value;
}
