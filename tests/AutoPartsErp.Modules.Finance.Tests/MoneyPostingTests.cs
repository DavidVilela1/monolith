using AutoPartsErp.Modules.Finance.Application.EventHandlers;
using AutoPartsErp.Modules.Finance.Domain;
using AutoPartsErp.Modules.Finance.Domain.Ledger;
using AutoPartsErp.Modules.Finance.Domain.Payments;
using AutoPartsErp.Modules.Finance.Domain.Payments.Events;
using AutoPartsErp.Modules.Finance.Domain.Receipts;
using AutoPartsErp.Modules.Finance.Domain.Receipts.Events;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Finance.Tests;

/// <summary>
/// What reaches the ledger when money moves.
/// <para>
/// The last two facts, and the two that a bank statement is reconciled against. Both post when the
/// money moves rather than when somebody has worked out which invoices it settles: a ledger that
/// waited for the allocation would disagree with the bank for as long as the matching took, which
/// is the state this module exists to make visible rather than to reproduce.
/// </para>
/// </summary>
public sealed class MoneyPostingTests
{
    private static readonly DateOnly InMarch = new(2026, 3, 18);

    /// <summary>Money arriving from a customer reaches the ledger the day it arrives.</summary>
    [Fact]
    public async Task A_receipt_posts_what_arrived()
    {
        var poster = new RecordingPoster();

        await new PostReceiptOnMoneyReceived(poster).HandleAsync(
            new ReceiptRecordedDomainEvent(
                ReceiptId.New(),
                "RC-2026-00042",
                new CustomerRef(Guid.NewGuid()),
                Money.Of(1240.50m, Currency.Eur),
                InMarch,
                ReceiptMethod.BankTransfer));

        HandedFact posted = poster.Facts.Single();
        posted.FactType.Should().Be(PostingFacts.CustomerReceipt);
        posted.Reference.Should().Be("RC-2026-00042");
        posted.OccurredOn.Should().Be(InMarch);
        posted.Amounts[PostingFacts.Gross].Amount.Should().Be(1240.50m);
        posted.Description.Should().Contain("Transfer");
    }

    /// <summary>Money leaving for a supplier does the same, the other way.</summary>
    [Fact]
    public async Task A_payment_posts_what_left()
    {
        var poster = new RecordingPoster();

        await new PostPaymentOnMoneyPaid(poster).HandleAsync(Paid(814.75m));

        HandedFact posted = poster.Facts.Single();
        posted.FactType.Should().Be(PostingFacts.SupplierPayment);
        posted.Reference.Should().Be("PM-2026-00017");
        posted.Amounts[PostingFacts.Gross].Amount.Should().Be(814.75m);
        posted.Description.Should().Contain("BP");
    }

    /// <summary>
    /// Both run inside the save that caused them, so neither commits. A handler that saved would
    /// re-enter the save it is running inside, and the receipt and its entry would stop being one
    /// transaction.
    /// </summary>
    [Fact]
    public async Task Neither_commits_the_transaction_it_runs_inside()
    {
        var poster = new RecordingPoster();

        await new PostPaymentOnMoneyPaid(poster).HandleAsync(Paid(814.75m));
        await new PostReceiptOnMoneyReceived(poster).HandleAsync(
            new ReceiptRecordedDomainEvent(
                ReceiptId.New(),
                "RC-2026-00042",
                new CustomerRef(Guid.NewGuid()),
                Money.Of(10m, Currency.Eur),
                InMarch,
                ReceiptMethod.Cash));

        poster.Facts.Should().HaveCount(2);
        poster.Facts.Should().OnlyContain(fact => !fact.Saved);
    }

    /// <summary>A payment of nothing is not a payment.</summary>
    [Fact]
    public async Task Money_worth_nothing_posts_nothing()
    {
        var poster = new RecordingPoster();

        await new PostPaymentOnMoneyPaid(poster).HandleAsync(Paid(0m));

        poster.Facts.Should().BeEmpty();
    }

    private static SupplierPaymentRecordedDomainEvent Paid(decimal amount) =>
        new(
            SupplierPaymentId.New(),
            "PM-2026-00017",
            new SupplierRef(Guid.NewGuid()),
            "BP",
            amount,
            "EUR",
            InMarch,
            PaymentMethod.BankTransfer);
}
