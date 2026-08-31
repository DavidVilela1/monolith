using AutoPartsErp.Modules.Partners.Domain.Partners;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Partners.Tests;

/// <summary>
/// The NIF check digit. These are real Portuguese numbers' worth of arithmetic, and the whole
/// point is catching a transposition at the counter rather than at SAF-T submission.
/// </summary>
public sealed class TaxNumberTests
{
    [Theory]
    [InlineData("501442600")]   // Valid: check digit 0
    [InlineData("980405319")]
    [InlineData("123456789")]
    public void A_valid_portuguese_nif_is_accepted(string nif)
    {
        TaxNumber.IsValidPortugueseNif(nif).Should().BeTrue();
    }

    [Theory]
    [InlineData("501442601")]   // Check digit wrong
    [InlineData("123456788")]
    [InlineData("12345678")]    // Too short
    [InlineData("1234567890")]  // Too long
    [InlineData("012345678")]   // Leading zero is not issued
    [InlineData("12345678A")]   // Not all digits
    [InlineData("")]
    public void An_invalid_portuguese_nif_is_rejected(string nif)
    {
        TaxNumber.IsValidPortugueseNif(nif).Should().BeFalse();
    }

    [Fact]
    public void A_transposed_pair_of_digits_is_caught()
    {
        TaxNumber.IsValidPortugueseNif("123456789").Should().BeTrue();

        // The mistake somebody actually makes: two digits swapped.
        TaxNumber.IsValidPortugueseNif("123456879").Should().BeFalse();
    }

    [Theory]
    [InlineData("PT 501 442 600")]
    [InlineData("PT501442600")]
    [InlineData("501442600")]
    [InlineData(" 501442600 ")]
    public void Spacing_and_a_country_prefix_are_stripped(string input)
    {
        Result<TaxNumber> taxNumber = TaxNumber.Create("PT", input);

        taxNumber.IsSuccess.Should().BeTrue();
        taxNumber.Value.Value.Should().Be("501442600");
        taxNumber.Value.Formatted.Should().Be("PT501442600");
    }

    [Fact]
    public void A_portuguese_number_that_fails_its_checksum_is_refused()
    {
        Result<TaxNumber> taxNumber = TaxNumber.Create("PT", "501442601");

        taxNumber.IsFailure.Should().BeTrue();
        taxNumber.Error.Code.Should().Be("partners.partner.tax_number_checksum");
    }

    [Fact]
    public void A_foreign_number_is_accepted_but_marked_unverified()
    {
        Result<TaxNumber> taxNumber = TaxNumber.Create("ES", "B12345678");

        taxNumber.IsSuccess.Should().BeTrue();
        taxNumber.Value.IsVerified.Should().BeFalse();
    }

    [Fact]
    public void A_verified_number_says_so()
    {
        TaxNumber.Create("PT", "501442600").Value.IsVerified.Should().BeTrue();
    }
}

/// <summary>Roles are additive, because the same company is often both customer and supplier.</summary>
public sealed class PartnerRoleTests
{
    [Fact]
    public void A_new_partner_has_no_roles_and_cannot_trade()
    {
        Partner partner = Fixture.NewPartner();

        partner.Roles.Should().Be(PartnerRoles.None);
        partner.CanTakeNewOrders.Should().BeFalse();
        partner.CanPlacePurchaseOrders.Should().BeFalse();
    }

    [Fact]
    public void A_customer_needs_a_billing_address_first()
    {
        Partner partner = Fixture.NewPartner();

        Result granted = partner.GrantCustomerRole(Fixture.AccountTerms());

        granted.IsFailure.Should().BeTrue();
        granted.Error.Code.Should().Be("partners.partner.billing_address_required");
    }

    [Fact]
    public void With_a_billing_address_the_customer_role_is_granted()
    {
        Partner partner = Fixture.NewPartner();
        partner.AddAddress(Fixture.BillingAddress());

        partner.GrantCustomerRole(Fixture.AccountTerms()).IsSuccess.Should().BeTrue();

        partner.IsCustomer.Should().BeTrue();
        partner.CanTakeNewOrders.Should().BeTrue();
    }

    [Fact]
    public void One_partner_can_be_both_customer_and_supplier()
    {
        Partner partner = Fixture.NewPartner();
        partner.AddAddress(Fixture.BillingAddress());

        partner.GrantCustomerRole(Fixture.AccountTerms());
        partner.GrantSupplierRole(Fixture.SupplierTerms());

        partner.IsCustomer.Should().BeTrue();
        partner.IsSupplier.Should().BeTrue();
        partner.Roles.Should().Be(PartnerRoles.Customer | PartnerRoles.Supplier);
    }

    [Fact]
    public void A_supplier_does_not_need_a_billing_address()
    {
        Partner partner = Fixture.NewPartner();

        partner.GrantSupplierRole(Fixture.SupplierTerms()).IsSuccess.Should().BeTrue();
    }
}

/// <summary>Credit terms, and the hold that follows when they are not honoured.</summary>
public sealed class CreditTests
{
    [Fact]
    public void A_credit_limit_needs_a_payment_period()
    {
        Result<CustomerTerms> terms = CustomerTerms.Create(
            Money.Of(5000m, Currency.Eur),
            PaymentTerms.Immediate);

        terms.IsFailure.Should().BeTrue();
        terms.Error.Code.Should().Be("partners.terms.credit_without_period");
    }

    [Fact]
    public void Cash_only_terms_are_valid_with_no_payment_period()
    {
        CustomerTerms terms = CustomerTerms.CashOnly(Currency.Eur);

        terms.HasCreditAccount.Should().BeFalse();
        terms.PaymentTerms.IsPrepaid.Should().BeTrue();
    }

    [Fact]
    public void End_of_month_terms_count_from_the_month_end()
    {
        PaymentTerms terms = PaymentTerms.Create(30, PaymentMethod.BankTransfer, endOfMonth: true).Value;

        // Invoiced 3 March, due 30 days after 31 March.
        terms.DueDateFor(new DateOnly(2026, 3, 3)).Should().Be(new DateOnly(2026, 4, 30));
    }

    [Fact]
    public void Ordinary_terms_count_from_the_invoice_date()
    {
        PaymentTerms terms = PaymentTerms.Create(30, PaymentMethod.BankTransfer).Value;

        terms.DueDateFor(new DateOnly(2026, 3, 3)).Should().Be(new DateOnly(2026, 4, 2));
    }

    [Fact]
    public void A_hold_stops_new_orders_without_ending_the_relationship()
    {
        Partner partner = Fixture.ActiveCustomer();

        partner.PlaceOnHold("Invoice 4471 is 62 days overdue").IsSuccess.Should().BeTrue();

        partner.Status.Should().Be(PartnerStatus.OnHold);
        partner.CanTakeNewOrders.Should().BeFalse();
        partner.IsCustomer.Should().BeTrue();
        partner.HoldReason.Should().Contain("4471");
    }

    [Fact]
    public void A_hold_needs_a_reason()
    {
        Partner partner = Fixture.ActiveCustomer();

        partner.PlaceOnHold("  ").Error.Code.Should().Be("partners.partner.hold_reason_required");
    }

    [Fact]
    public void Releasing_a_hold_restores_trading()
    {
        Partner partner = Fixture.ActiveCustomer();
        partner.PlaceOnHold("Overdue");

        partner.ReleaseHold().IsSuccess.Should().BeTrue();

        partner.CanTakeNewOrders.Should().BeTrue();
        partner.HoldReason.Should().BeNull();
    }

    [Fact]
    public void A_closed_partner_is_frozen()
    {
        Partner partner = Fixture.ActiveCustomer();
        partner.Close();

        partner.Rename("New name", null).Error.Code.Should().Be("partners.partner.closed_readonly");
        partner.PlaceOnHold("anything").Error.Code.Should().Be("partners.partner.closed_readonly");
    }
}

/// <summary>Addresses and contacts.</summary>
public sealed class PartnerAddressTests
{
    [Fact]
    public void A_second_billing_address_replaces_the_first()
    {
        Partner partner = Fixture.NewPartner();

        partner.AddAddress(Fixture.BillingAddress("Rua A"));
        partner.AddAddress(Fixture.BillingAddress("Rua B"));

        partner.Addresses.Count(address => address.Kind == AddressKind.Billing).Should().Be(1);
        partner.BillingAddress!.Line1.Should().Be("Rua B");
    }

    [Fact]
    public void A_partner_may_have_many_delivery_addresses()
    {
        Partner partner = Fixture.NewPartner();

        partner.AddAddress(Fixture.DeliveryAddress("Oficina Norte"));
        partner.AddAddress(Fixture.DeliveryAddress("Oficina Sul"));

        partner.Addresses.Count(address => address.Kind == AddressKind.Delivery).Should().Be(2);
    }

    [Fact]
    public void A_customers_billing_address_cannot_be_removed()
    {
        Partner partner = Fixture.ActiveCustomer();
        Address billing = partner.BillingAddress!;

        partner.RemoveAddress(billing).Error.Code
            .Should().Be("partners.partner.billing_address_required");
    }

    [Fact]
    public void A_contact_needs_a_way_to_reach_them()
    {
        ContactDetail.Create("João Silva").Error.Code
            .Should().Be("partners.contact.no_contact_method");
    }

    [Fact]
    public void Only_one_contact_is_primary()
    {
        Partner partner = Fixture.NewPartner();

        partner.AddContact(ContactDetail.Create("A", phone: "911111111", isPrimary: true).Value);
        partner.AddContact(ContactDetail.Create("B", phone: "922222222", isPrimary: true).Value);

        partner.Contacts.Count(contact => contact.IsPrimary).Should().Be(1);
        partner.Contacts.Single(contact => contact.IsPrimary).Name.Should().Be("B");
    }
}

internal static class Fixture
{
    public static Partner NewPartner() =>
        Partner.Create("C0001", "Oficina Central Lda", TaxNumber.Create("PT", "501442600").Value).Value;

    public static Partner ActiveCustomer()
    {
        Partner partner = NewPartner();
        partner.AddAddress(BillingAddress());
        partner.GrantCustomerRole(AccountTerms());
        return partner;
    }

    public static Address BillingAddress(string line1 = "Rua das Oficinas 12") =>
        Address.Create(AddressKind.Billing, line1, "1000-100", "Lisboa", "PT").Value;

    public static Address DeliveryAddress(string line1) =>
        Address.Create(AddressKind.Delivery, line1, "4000-200", "Porto", "PT").Value;

    public static CustomerTerms AccountTerms() =>
        CustomerTerms.Create(
            Money.Of(5000m, Currency.Eur),
            PaymentTerms.Create(30, PaymentMethod.BankTransfer, endOfMonth: true).Value).Value;

    public static SupplierTerms SupplierTerms() =>
        Domain.Partners.SupplierTerms.Create(
            PaymentTerms.Create(60, PaymentMethod.BankTransfer).Value,
            leadTimeDays: 3).Value;
}
