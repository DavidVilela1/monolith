using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Partners.Domain.Partners;

/// <summary>When a partner is expected to pay.</summary>
public enum PaymentMethod
{
    /// <summary>Unspecified. Never persisted.</summary>
    Unknown = 0,

    /// <summary>Paid at the counter before the goods leave.</summary>
    Cash = 1,

    /// <summary>Card, at the counter or over the phone.</summary>
    Card = 2,

    /// <summary>Bank transfer against an invoice.</summary>
    BankTransfer = 3,

    /// <summary>Direct debit on the due date.</summary>
    DirectDebit = 4,

    /// <summary>Cheque. Still used, still slow.</summary>
    Cheque = 5,
}

/// <summary>
/// How long a partner has to pay, and by what means.
/// <para>
/// <see cref="DueInDays"/> of zero means payment on delivery — the difference between a cash
/// customer and an account customer, and the single most important commercial fact about a
/// trade counter relationship.
/// </para>
/// </summary>
public sealed class PaymentTerms : ValueObject
{
    private PaymentTerms(int dueInDays, PaymentMethod method, bool endOfMonth)
    {
        DueInDays = dueInDays;
        Method = method;
        EndOfMonth = endOfMonth;
    }

    /// <summary>Required by EF Core materialization.</summary>
    private PaymentTerms()
    {
    }

    /// <summary>Payment on delivery, by cash or card.</summary>
    public static PaymentTerms Immediate { get; } = new(0, PaymentMethod.Cash, endOfMonth: false);

    /// <summary>Days from the invoice date, or from month end when <see cref="EndOfMonth"/> is set.</summary>
    public int DueInDays { get; }

    /// <summary>How payment is expected.</summary>
    public PaymentMethod Method { get; } = PaymentMethod.Cash;

    /// <summary>
    /// True for terms counted from the end of the invoice month rather than the invoice date —
    /// "30 days end of month", the common arrangement with workshops.
    /// </summary>
    public bool EndOfMonth { get; }

    /// <summary>True when the partner pays before the goods leave.</summary>
    public bool IsPrepaid => DueInDays == 0;

    /// <summary>Creates payment terms.</summary>
    /// <param name="dueInDays">Days to pay. Zero means on delivery.</param>
    /// <param name="method">How payment is expected.</param>
    /// <param name="endOfMonth">True to count from month end.</param>
    public static Result<PaymentTerms> Create(
        int dueInDays,
        PaymentMethod method,
        bool endOfMonth = false)
    {
        if (dueInDays is < 0 or > 365)
        {
            return PartnerErrors.Terms.DueDaysOutOfRange;
        }

        if (method == PaymentMethod.Unknown)
        {
            return PartnerErrors.Terms.PaymentMethodRequired;
        }

        return new PaymentTerms(dueInDays, method, endOfMonth);
    }

    /// <summary>Works out when an invoice dated <paramref name="invoiceDate"/> falls due.</summary>
    public DateOnly DueDateFor(DateOnly invoiceDate)
    {
        if (!EndOfMonth)
        {
            return invoiceDate.AddDays(DueInDays);
        }

        var monthEnd = new DateOnly(
            invoiceDate.Year,
            invoiceDate.Month,
            DateTime.DaysInMonth(invoiceDate.Year, invoiceDate.Month));

        return monthEnd.AddDays(DueInDays);
    }

    /// <inheritdoc />
    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return DueInDays;
        yield return Method;
        yield return EndOfMonth;
    }

    /// <inheritdoc />
    public override string ToString() =>
        DueInDays == 0 ? "On delivery" : EndOfMonth ? $"{DueInDays} days EOM" : $"{DueInDays} days";
}

/// <summary>
/// The commercial arrangement with a customer.
/// <para>
/// Credit is the part that hurts when it is wrong. A distributor that lets a workshop run past
/// its limit is lending money it did not agree to lend, and parts businesses fail on unpaid
/// receivables far more often than on thin margins.
/// </para>
/// </summary>
public sealed class CustomerTerms : ValueObject
{
    private CustomerTerms(Money creditLimit, PaymentTerms paymentTerms, string? priceListCode)
    {
        CreditLimit = creditLimit;
        PaymentTerms = paymentTerms;
        PriceListCode = priceListCode;
    }

    /// <summary>Required by EF Core materialization.</summary>
#pragma warning disable CS8618
    private CustomerTerms()
    {
    }
#pragma warning restore CS8618

    /// <summary>How much the customer may owe at once. Zero means cash only.</summary>
    public Money CreditLimit { get; } = null!;

    /// <summary>When they are expected to pay.</summary>
    public PaymentTerms PaymentTerms { get; } = null!;

    /// <summary>
    /// Which price list applies. Pricing owns the list itself; this is only the key, so the
    /// two modules stay independent.
    /// </summary>
    public string? PriceListCode { get; }

    /// <summary>True when this customer buys on account rather than paying at the counter.</summary>
    public bool HasCreditAccount => !CreditLimit.IsZero;

    /// <summary>Creates customer terms.</summary>
    public static Result<CustomerTerms> Create(
        Money creditLimit,
        PaymentTerms paymentTerms,
        string? priceListCode = null)
    {
        ArgumentNullException.ThrowIfNull(creditLimit);
        ArgumentNullException.ThrowIfNull(paymentTerms);

        if (creditLimit.IsNegative)
        {
            return PartnerErrors.Terms.CreditLimitNegative;
        }

        // Terms that let someone owe money without a deadline are not terms.
        if (!creditLimit.IsZero && paymentTerms.IsPrepaid)
        {
            return PartnerErrors.Terms.CreditWithoutPaymentPeriod;
        }

        return new CustomerTerms(
            creditLimit,
            paymentTerms,
            string.IsNullOrWhiteSpace(priceListCode) ? null : priceListCode.Trim().ToUpperInvariant());
    }

    /// <summary>Cash-only terms in the given currency: no credit, paid on delivery.</summary>
    public static CustomerTerms CashOnly(Currency currency) =>
        new(Money.Zero(currency), PaymentTerms.Immediate, null);

    /// <inheritdoc />
    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return CreditLimit;
        yield return PaymentTerms;
        yield return PriceListCode;
    }
}

/// <summary>The arrangement with a supplier.</summary>
public sealed class SupplierTerms : ValueObject
{
    private SupplierTerms(
        PaymentTerms paymentTerms,
        int leadTimeDays,
        Money? minimumOrderValue,
        string? ourAccountNumber)
    {
        PaymentTerms = paymentTerms;
        LeadTimeDays = leadTimeDays;
        MinimumOrderValue = minimumOrderValue;
        OurAccountNumber = ourAccountNumber;
    }

    /// <summary>Required by EF Core materialization.</summary>
#pragma warning disable CS8618
    private SupplierTerms()
    {
    }
#pragma warning restore CS8618

    /// <summary>When we are expected to pay them.</summary>
    public PaymentTerms PaymentTerms { get; } = null!;

    /// <summary>
    /// Typical days from order to delivery. Feeds the reorder point: a supplier who takes two
    /// weeks needs a higher trigger level than one who delivers overnight.
    /// </summary>
    public int LeadTimeDays { get; }

    /// <summary>The order value below which they will not ship, or charge carriage.</summary>
    public Money? MinimumOrderValue { get; }

    /// <summary>Our account number with them, as it appears on their paperwork.</summary>
    public string? OurAccountNumber { get; }

    /// <summary>Creates supplier terms.</summary>
    public static Result<SupplierTerms> Create(
        PaymentTerms paymentTerms,
        int leadTimeDays,
        Money? minimumOrderValue = null,
        string? ourAccountNumber = null)
    {
        ArgumentNullException.ThrowIfNull(paymentTerms);

        if (leadTimeDays is < 0 or > 365)
        {
            return PartnerErrors.Terms.LeadTimeOutOfRange;
        }

        if (minimumOrderValue is { IsNegative: true })
        {
            return PartnerErrors.Terms.MinimumOrderNegative;
        }

        return new SupplierTerms(
            paymentTerms,
            leadTimeDays,
            minimumOrderValue,
            string.IsNullOrWhiteSpace(ourAccountNumber) ? null : ourAccountNumber.Trim());
    }

    /// <inheritdoc />
    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return PaymentTerms;
        yield return LeadTimeDays;
        yield return MinimumOrderValue;
        yield return OurAccountNumber;
    }
}
