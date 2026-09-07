using AutoPartsErp.Modules.Finance.Domain;
using AutoPartsErp.Modules.Finance.Domain.Customers;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Finance.Tests;

/// <summary>
/// When a document falls due.
/// <para>
/// The whole module hangs off this one calculation. Every ageing bucket, every overdue flag and
/// every reminder letter is a comparison against a date this produces, so an error of a day here
/// is an error of a day in all of them at once — and an error of a month, which end-of-month
/// terms make easy, is a customer chased for something that is not yet late.
/// </para>
/// </summary>
public sealed class CustomerTermsTests
{
    private static readonly CustomerRef Customer = new(Guid.NewGuid());

    [Fact]
    public void Cash_terms_fall_due_on_the_day_of_the_document()
    {
        CustomerTerms terms = Open(paymentDueInDays: 0, endOfMonth: false);

        terms.DueDateFor(new DateOnly(2026, 9, 7)).Should().Be(new DateOnly(2026, 9, 7));
        terms.IsCashOnly.Should().BeTrue();
    }

    [Fact]
    public void Thirty_days_is_thirty_days_after_the_document()
    {
        CustomerTerms terms = Open(paymentDueInDays: 30, endOfMonth: false);

        terms.DueDateFor(new DateOnly(2026, 9, 7)).Should().Be(new DateOnly(2026, 10, 7));
    }

    /// <summary>
    /// "Thirty days end of month" is not thirty days, and this is the case that proves it.
    /// </summary>
    [Fact]
    public void End_of_month_terms_push_both_documents_to_the_same_day()
    {
        CustomerTerms terms = Open(paymentDueInDays: 30, endOfMonth: true);

        // The 2nd plus thirty days lands on 2 October; the 28th plus thirty lands on 28 October.
        // Under end-of-month terms both are owed on the 31st, which is the arrangement the
        // customer believes they have.
        terms.DueDateFor(new DateOnly(2026, 9, 2)).Should().Be(new DateOnly(2026, 10, 31));
        terms.DueDateFor(new DateOnly(2026, 9, 28)).Should().Be(new DateOnly(2026, 10, 31));
    }

    [Fact]
    public void End_of_month_lands_on_the_real_last_day_of_a_short_month()
    {
        CustomerTerms terms = Open(paymentDueInDays: 30, endOfMonth: true);

        // 30 January plus thirty days is 1 March in a non-leap year, so the answer is 31 March
        // rather than anything in February.
        terms.DueDateFor(new DateOnly(2026, 1, 30)).Should().Be(new DateOnly(2026, 3, 31));

        // Landing inside February gets February's own last day, and 2028 is a leap year.
        terms.DueDateFor(new DateOnly(2028, 1, 20)).Should().Be(new DateOnly(2028, 2, 29));
    }

    [Fact]
    public void Terms_cross_the_year_end_without_help()
    {
        CustomerTerms terms = Open(paymentDueInDays: 60, endOfMonth: false);

        terms.DueDateFor(new DateOnly(2026, 12, 15)).Should().Be(new DateOnly(2027, 2, 13));
    }

    [Fact]
    public void A_credit_period_nobody_meant_is_refused()
    {
        CustomerTerms.Open(Customer, "OFIC01", "Oficina, Lda.", Currency.Eur, -1, false)
            .IsFailure.Should().BeTrue();

        CustomerTerms.Open(Customer, "OFIC01", "Oficina, Lda.", Currency.Eur, 3000, false)
            .Error.Code.Should().Be("finance.terms.payment_days_out_of_range");
    }

    [Fact]
    public void Changing_terms_does_not_reach_back_into_documents_already_raised()
    {
        CustomerTerms terms = Open(paymentDueInDays: 30, endOfMonth: false);

        var raisedOn = new DateOnly(2026, 9, 7);
        DateOnly dueUnderOldTerms = terms.DueDateFor(raisedOn);

        terms.Change("Oficina, Lda.", Currency.Eur, 60, false).IsSuccess.Should().BeTrue();

        // The aggregate answers with the new terms from now on...
        terms.DueDateFor(raisedOn).Should().Be(new DateOnly(2026, 11, 6));

        // ...but the date already stamped on an item is a different value, held by the item. This
        // is the reason OpenItem stores DueDate rather than recomputing it: a customer moved to
        // sixty days has not been given another month on invoices they already have.
        dueUnderOldTerms.Should().Be(new DateOnly(2026, 10, 7));
    }

    private static CustomerTerms Open(int paymentDueInDays, bool endOfMonth) =>
        CustomerTerms.Open(
            Customer, "OFIC01", "Oficina Central, Lda.", Currency.Eur, paymentDueInDays, endOfMonth)
            .Value;
}
