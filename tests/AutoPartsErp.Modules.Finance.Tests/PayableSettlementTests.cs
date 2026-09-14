using AutoPartsErp.Modules.Finance.Domain;
using AutoPartsErp.Modules.Finance.Domain.Payables;
using AutoPartsErp.Modules.Finance.Domain.Payments;
using AutoPartsErp.Modules.Finance.Domain.Payments.Events;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Finance.Tests;

/// <summary>
/// Paying a supplier, and matching the money to the documents it paid.
/// <para>
/// Settling is the one operation in this module that is not about a single aggregate: it changes
/// the payment and it changes every item the payment settled, and both are the same fact. That is
/// why it lives between them, why both halves are internal, and why everything below is checked
/// before anything is changed.
/// </para>
/// </summary>
public sealed class PayableSettlementTests
{
    private static readonly DateOnly Invoiced = new(2026, 9, 1);
    private static readonly DateOnly Paid = new(2026, 10, 1);
    private static readonly SupplierRef Supplier = new(Guid.NewGuid());

    private static Money Eur(decimal amount) => Money.Of(amount, Currency.Eur);

    /// <summary>One transfer against two invoices, which is what a Friday payment run looks like.</summary>
    [Fact]
    public void One_payment_settles_several_documents()
    {
        PayableItem first = Payable(814.75m, "FT 2026/14872");
        PayableItem second = Payable(185.25m, "FT 2026/14905");
        SupplierPayment payment = Payment(1_000.00m);

        PayableSettlement.Apply(
            payment,
            [new PayableSettlementLine(first, Eur(814.75m)),
             new PayableSettlementLine(second, Eur(185.25m))],
            Paid).IsSuccess.Should().BeTrue();

        first.Status.Should().Be(PayableItemStatus.Settled);
        second.Status.Should().Be(PayableItemStatus.Settled);

        payment.Unallocated.Amount.Should().Be(0m);
        payment.Status.Should().Be(PaymentStatus.Allocated);
        payment.Allocations.Should().HaveCount(2);

        payment.DomainEvents.OfType<SupplierPaymentFullyAllocatedDomainEvent>()
            .Should().ContainSingle();
    }

    /// <summary>
    /// A transfer goes out on Friday against a statement; which of eleven invoices it covered is a
    /// question somebody answers on Monday. Forcing the match at the moment the money moves would
    /// mean guessing, or not recording the payment and leaving the bank balance wrong meanwhile.
    /// </summary>
    [Fact]
    public void Money_can_leave_before_anybody_knows_what_it_paid()
    {
        SupplierPayment payment = Payment(1_000.00m);

        payment.Status.Should().Be(PaymentStatus.Unallocated);
        payment.Unallocated.Amount.Should().Be(1_000.00m);
        payment.Allocations.Should().BeEmpty();
    }

    /// <summary>Part of a document paid leaves the rest owed, and both sides say so.</summary>
    [Fact]
    public void Paying_part_of_a_document_leaves_the_rest_owed()
    {
        PayableItem item = Payable(814.75m, "FT 2026/14872");
        SupplierPayment payment = Payment(300.00m);

        PayableSettlement.Apply(
            payment, [new PayableSettlementLine(item, Eur(300.00m))], Paid)
            .IsSuccess.Should().BeTrue();

        item.Outstanding.Amount.Should().Be(514.75m);
        item.Status.Should().Be(PayableItemStatus.PartiallySettled);
        payment.Status.Should().Be(PaymentStatus.Allocated);
    }

    /// <summary>
    /// The two-pass check, and the reason for it. A remittance that failed on its last line would
    /// otherwise leave the earlier ones settled in memory with nothing to roll back — nothing has
    /// touched the database yet, and nothing will until the caller saves.
    /// </summary>
    [Fact]
    public void A_remittance_that_fails_anywhere_changes_nothing_at_all()
    {
        PayableItem first = Payable(100.00m, "FT 2026/14872");
        PayableItem second = Payable(100.00m, "FT 2026/14905");
        SupplierPayment payment = Payment(200.00m);

        // The second line asks for more than that document owes.
        Result applied = PayableSettlement.Apply(
            payment,
            [new PayableSettlementLine(first, Eur(100.00m)),
             new PayableSettlementLine(second, Eur(100.01m))],
            Paid);

        applied.Error.Code.Should().Be("finance.payable.settlement_exceeds_outstanding");

        first.Outstanding.Amount.Should().Be(100.00m);
        first.Status.Should().Be(PayableItemStatus.Open);
        payment.Unallocated.Amount.Should().Be(200.00m);
        payment.Allocations.Should().BeEmpty();
    }

    /// <summary>Matching more than left the bank is how a purchase ledger stops reconciling to it.</summary>
    [Fact]
    public void A_payment_cannot_settle_more_than_left_the_bank()
    {
        PayableItem item = Payable(500.00m, "FT 2026/14872");
        SupplierPayment payment = Payment(300.00m);

        PayableSettlement.Apply(
            payment, [new PayableSettlementLine(item, Eur(500.00m))], Paid)
            .Error.Code.Should().Be("finance.payment.exceeds_unallocated");

        payment.Allocations.Should().BeEmpty();
    }

    /// <summary>
    /// A credit note is money the supplier owes back. Paying one with cash is not a transaction
    /// that exists — it is matched against an invoice.
    /// </summary>
    [Fact]
    public void Cash_is_never_matched_against_a_credit_note()
    {
        PayableItem credit = Payable(120.00m, "NC 2026/91", PayableItemKind.CreditNote);
        SupplierPayment payment = Payment(120.00m);

        PayableSettlement.Apply(
            payment, [new PayableSettlementLine(credit, Eur(120.00m))], Paid)
            .Error.Code.Should().Be("finance.payment.against_credit");
    }

    /// <summary>Somebody clicked twice, and both amounts would have been applied.</summary>
    [Fact]
    public void The_same_document_cannot_appear_twice_on_one_remittance()
    {
        PayableItem item = Payable(200.00m, "FT 2026/14872");
        SupplierPayment payment = Payment(200.00m);

        PayableSettlement.Apply(
            payment,
            [new PayableSettlementLine(item, Eur(100.00m)),
             new PayableSettlementLine(item, Eur(100.00m))],
            Paid).Error.Code.Should().Be("finance.payment.duplicate_document");
    }

    /// <summary>A payment to one supplier does not settle another one's document.</summary>
    [Fact]
    public void A_document_on_another_suppliers_account_is_refused()
    {
        PayableItem other = PayableItem.Raise(
            new SupplierRef(Guid.NewGuid()),
            "VALEO",
            new SupplierInvoiceRef(Guid.NewGuid()),
            "FT 2026/77",
            PayableItemKind.Invoice,
            Eur(100.00m),
            Invoiced,
            Invoiced.AddDays(30)).Value;

        SupplierPayment payment = Payment(100.00m);

        PayableSettlement.Apply(
            payment, [new PayableSettlementLine(other, Eur(100.00m))], Paid)
            .Error.Code.Should().Be("finance.payment.wrong_supplier");
    }

    /// <summary>A document nobody owes anything on has nothing to settle.</summary>
    [Fact]
    public void A_document_already_settled_is_refused()
    {
        PayableItem item = Payable(100.00m, "FT 2026/14872");
        SupplierPayment first = Payment(100.00m);

        PayableSettlement.Apply(
            first, [new PayableSettlementLine(item, Eur(100.00m))], Paid)
            .IsSuccess.Should().BeTrue();

        SupplierPayment second = Payment(100.00m, "PAY-2026-00002");

        PayableSettlement.Apply(
            second, [new PayableSettlementLine(item, Eur(100.00m))], Paid)
            .Error.Code.Should().Be("finance.payment.nothing_owed");
    }

    /// <summary>A remittance with no lines settles nothing, and saying so is not an allocation.</summary>
    [Fact]
    public void A_remittance_has_to_say_what_it_paid()
    {
        PayableSettlement.Apply(Payment(100.00m), [], Paid)
            .Error.Code.Should().Be("finance.payment.nothing_to_allocate");
    }

    /// <summary>
    /// A bank reconciliation that cannot tell a transfer from a cheque is one nobody can finish.
    /// </summary>
    [Fact]
    public void A_payment_needs_a_number_an_amount_and_a_method()
    {
        Recording(number: " ").Error.Code.Should().Be("finance.payment.number_required");
        Recording(amount: 0m).Error.Code.Should().Be("finance.payment.amount_not_positive");
        Recording(method: PaymentMethod.Unknown)
            .Error.Code.Should().Be("finance.payment.method_required");
    }

    private static Result<SupplierPayment> Recording(
        string? number = "PAY-2026-00001",
        decimal amount = 100.00m,
        PaymentMethod method = PaymentMethod.BankTransfer) =>
        SupplierPayment.Record(number, Supplier, "BOSCH", Eur(amount), Paid, method);

    private static SupplierPayment Payment(decimal amount, string number = "PAY-2026-00001") =>
        SupplierPayment.Record(
            number, Supplier, "BOSCH", Eur(amount), Paid, PaymentMethod.BankTransfer).Value;

    private static PayableItem Payable(
        decimal amount, string number, PayableItemKind kind = PayableItemKind.Invoice) =>
        PayableItem.Raise(
            Supplier,
            "BOSCH",
            new SupplierInvoiceRef(Guid.NewGuid()),
            number,
            kind,
            Eur(amount),
            Invoiced,
            Invoiced.AddDays(30)).Value;
}
