using AutoPartsErp.Modules.Sales.Domain;
using AutoPartsErp.Modules.Sales.Domain.Customers;
using AutoPartsErp.Modules.Sales.Domain.Customers.Events;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Sales.Tests;

/// <summary>
/// The credit decision. This is the aggregate that answers the only question the counter really
/// has, and every rule in it exists because getting it wrong costs money rather than tidiness.
/// </summary>
public sealed class CustomerAccountTests
{
    private static CustomerAccount NewAccount(decimal creditLimit = 1000m, int dueInDays = 30) =>
        CustomerAccount.Open(
            new CustomerRef(Guid.NewGuid()),
            "wksp",
            "Workshop Lda",
            Money.Of(creditLimit, Currency.Eur),
            dueInDays)
            .Value;

    [Fact]
    public void A_new_account_is_active_with_its_whole_limit_available()
    {
        CustomerAccount account = NewAccount();

        account.Code.Should().Be("WKSP");
        account.Status.Should().Be(CustomerStatus.Active);
        account.CanTakeOrders.Should().BeTrue();
        account.EnsureCanTrade().IsSuccess.Should().BeTrue();
        account.Committed.IsZero.Should().BeTrue();
        account.AvailableCredit.Amount.Should().Be(1000m);
        account.DomainEvents.OfType<CustomerAccountOpenedDomainEvent>().Should().ContainSingle();
    }

    [Fact]
    public void An_account_needs_a_code_and_a_name()
    {
        CustomerAccount
            .Open(new CustomerRef(Guid.NewGuid()), " ", "Workshop Lda", Money.Of(1m, Currency.Eur), 30)
            .Error.Code.Should().Be("sales.customer.code_required");

        CustomerAccount
            .Open(new CustomerRef(Guid.NewGuid()), "WKSP", " ", Money.Of(1m, Currency.Eur), 30)
            .Error.Code.Should().Be("sales.customer.name_required");
    }

    [Fact]
    public void A_negative_credit_limit_is_refused()
    {
        CustomerAccount
            .Open(new CustomerRef(Guid.NewGuid()), "WKSP", "Workshop Lda", Money.Of(-1m, Currency.Eur), 30)
            .Error.Code.Should().Be("sales.customer.credit_limit_negative");
    }

    [Fact]
    public void No_limit_and_no_payment_days_means_cash_only()
    {
        NewAccount(creditLimit: 0m, dueInDays: 0).IsCashOnly.Should().BeTrue();
        NewAccount(creditLimit: 1000m, dueInDays: 30).IsCashOnly.Should().BeFalse();
    }

    [Fact]
    public void Committing_reduces_what_is_available()
    {
        CustomerAccount account = NewAccount(1000m);

        account.Commit(Money.Of(400m, Currency.Eur)).IsSuccess.Should().BeTrue();

        account.Committed.Amount.Should().Be(400m);
        account.AvailableCredit.Amount.Should().Be(600m);
    }

    [Fact]
    public void An_order_beyond_the_limit_is_refused()
    {
        CustomerAccount account = NewAccount(1000m);
        account.Commit(Money.Of(400m, Currency.Eur));

        Result committed = account.Commit(Money.Of(700m, Currency.Eur));

        committed.IsFailure.Should().BeTrue();
        committed.Error.Code.Should().Be("sales.customer.credit_limit_exceeded");
    }

    [Fact]
    public void Exposure_is_counted_at_confirmation_not_at_invoicing()
    {
        // Four unshipped orders of 300 against a 1,000 limit. Nothing has been invoiced, and the
        // fourth still has to be refused - that is the entire point of tracking commitment.
        CustomerAccount account = NewAccount(1000m);

        account.Commit(Money.Of(300m, Currency.Eur)).IsSuccess.Should().BeTrue();
        account.Commit(Money.Of(300m, Currency.Eur)).IsSuccess.Should().BeTrue();
        account.Commit(Money.Of(300m, Currency.Eur)).IsSuccess.Should().BeTrue();
        account.Commit(Money.Of(300m, Currency.Eur)).IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Crossing_nine_tenths_of_the_limit_raises_a_warning()
    {
        CustomerAccount account = NewAccount(1000m);
        account.Commit(Money.Of(400m, Currency.Eur));
        account.ClearDomainEvents();

        account.DomainEvents.Should().BeEmpty();

        account.Commit(Money.Of(500m, Currency.Eur));

        account.DomainEvents.OfType<CustomerCreditNearlyExhaustedDomainEvent>().Should().ContainSingle();
    }

    [Fact]
    public void Releasing_gives_the_credit_back()
    {
        CustomerAccount account = NewAccount(1000m);
        account.Commit(Money.Of(900m, Currency.Eur));

        account.ReleaseCommitment(Money.Of(400m, Currency.Eur)).IsSuccess.Should().BeTrue();

        account.AvailableCredit.Amount.Should().Be(500m);
    }

    [Fact]
    public void Releasing_more_than_was_committed_is_refused()
    {
        CustomerAccount account = NewAccount(1000m);
        account.Commit(Money.Of(100m, Currency.Eur));

        account.ReleaseCommitment(Money.Of(200m, Currency.Eur))
            .Error.Code.Should().Be("sales.customer.release_exceeds_commitment");
    }

    [Fact]
    public void Money_in_another_currency_is_refused_in_both_directions()
    {
        CustomerAccount account = NewAccount();

        account.Commit(Money.Of(10m, Currency.Usd))
            .Error.Code.Should().Be("sales.customer.currency_mismatch");
        account.ReleaseCommitment(Money.Of(10m, Currency.Usd))
            .Error.Code.Should().Be("sales.customer.currency_mismatch");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void A_hold_needs_a_reason(string? reason)
    {
        NewAccount().PlaceOnHold(reason).Error.Code.Should().Be("sales.customer.hold_reason_required");
    }

    [Fact]
    public void A_held_account_cannot_trade_or_commit()
    {
        CustomerAccount account = NewAccount();

        account.PlaceOnHold("Invoice 4471 is 40 days overdue.").IsSuccess.Should().BeTrue();

        account.CanTakeOrders.Should().BeFalse();
        account.EnsureCanTrade().Error.Code.Should().Be("sales.customer.on_hold");
        account.Commit(Money.Of(1m, Currency.Eur)).Error.Code.Should().Be("sales.customer.on_hold");
        account.HoldReason.Should().Be("Invoice 4471 is 40 days overdue.");
    }

    [Fact]
    public void Releasing_a_hold_lets_them_buy_again()
    {
        CustomerAccount account = NewAccount();
        account.PlaceOnHold("Overdue.");

        account.ReleaseHold().IsSuccess.Should().BeTrue();

        account.CanTakeOrders.Should().BeTrue();
        account.HoldReason.Should().BeNull();
    }

    [Fact]
    public void A_closed_account_cannot_trade_and_cannot_be_released()
    {
        CustomerAccount account = NewAccount();

        account.Close().IsSuccess.Should().BeTrue();

        account.EnsureCanTrade().Error.Code.Should().Be("sales.customer.closed");
        account.ReleaseHold().Error.Code.Should().Be("sales.customer.closed");
    }

    [Fact]
    public void A_limit_may_drop_below_what_is_already_committed()
    {
        // The orders already out were already promised. The new figure binds the next one.
        CustomerAccount account = NewAccount(1000m);
        account.Commit(Money.Of(800m, Currency.Eur));

        account.ChangeCreditLimit(Money.Of(100m, Currency.Eur)).IsSuccess.Should().BeTrue();

        account.AvailableCredit.Amount.Should().Be(0m);
        account.Commit(Money.Of(1m, Currency.Eur)).IsFailure.Should().BeTrue();
    }

    [Fact]
    public void A_negative_limit_is_refused_on_change_too()
    {
        NewAccount()
            .ChangeCreditLimit(Money.Of(-1m, Currency.Eur))
            .Error.Code.Should().Be("sales.customer.credit_limit_negative");
    }

    [Fact]
    public void Re_applying_the_same_terms_is_safe()
    {
        // The projection is fed by an at-least-once stream, so this happens rather than might.
        CustomerAccount account = NewAccount(1000m);
        account.Commit(Money.Of(200m, Currency.Eur));

        account.ApplyTerms("WKSP", "Workshop Lda", Money.Of(1000m, Currency.Eur), 30, false, "TRADE")
            .IsSuccess.Should().BeTrue();

        account.Committed.Amount.Should().Be(200m);
        account.PriceListCode.Should().Be("TRADE");
    }

    [Fact]
    public void Granting_the_role_again_reopens_a_closed_account()
    {
        CustomerAccount account = NewAccount();
        account.Close();

        account.ApplyTerms("WKSP", "Workshop Lda", Money.Of(2000m, Currency.Eur), 30, false, null)
            .IsSuccess.Should().BeTrue();

        account.Status.Should().Be(CustomerStatus.Active);
        account.CreditLimit.Amount.Should().Be(2000m);
    }

    [Fact]
    public void Applying_terms_does_not_lift_a_hold()
    {
        // A hold is Sales' answer to an overdue account, and a routine terms refresh from
        // Partners must not quietly clear it.
        CustomerAccount account = NewAccount();
        account.PlaceOnHold("Overdue.");

        account.ApplyTerms("WKSP", "Workshop Lda", Money.Of(1000m, Currency.Eur), 30, false, null);

        account.Status.Should().Be(CustomerStatus.OnHold);
    }
}
