using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Finance.Domain.Customers;

/// <summary>
/// When a customer's documents fall due, and the little else Finance needs to know about them.
/// <para>
/// Built from the event Partners publishes when a customer account is opened, the same way Sales
/// builds its own copy. Three modules therefore each hold a slice of the same customer, which
/// looks like duplication and is the opposite: none of them can be broken by a change to another,
/// and each holds only the fields it can defend. Finance needs a due date and a currency; it does
/// not need an address, and having one would only be a second address to go stale.
/// </para>
/// <para>
/// Keyed by the customer rather than by an identifier of its own. There is exactly one set of
/// terms per customer per tenant, and giving the row a surrogate key would create the possibility
/// of two.
/// </para>
/// </summary>
public sealed class CustomerTerms : AggregateRoot<CustomerRef>, ITenantScoped, IAuditable
{
    /// <summary>Longest customer code.</summary>
    public const int MaxCodeLength = 20;

    /// <summary>Longest legal name.</summary>
    public const int MaxNameLength = 200;

    /// <summary>
    /// The longest credit period accepted, in days.
    /// <para>
    /// Not a legal limit, a sanity one. Portuguese commercial practice runs to ninety days and
    /// occasionally a hundred and twenty; a four-digit number in this field is a typo, and letting
    /// it through produces invoices that quietly never become overdue.
    /// </para>
    /// </summary>
    public const int MaxPaymentDueInDays = 365;

    private CustomerTerms(
        CustomerRef customerId,
        string code,
        string legalName,
        Currency currency,
        int paymentDueInDays,
        bool paymentEndOfMonth)
        : base(customerId)
    {
        Code = code;
        LegalName = legalName;
        Currency = currency;
        PaymentDueInDays = paymentDueInDays;
        PaymentEndOfMonth = paymentEndOfMonth;
    }

    /// <summary>Required by EF Core materialization.</summary>
#pragma warning disable CS8618
    private CustomerTerms()
    {
    }
#pragma warning restore CS8618

    /// <summary>The customer's code, as Partners issued it.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>Their legal name, for the statement heading.</summary>
    public string LegalName { get; private set; } = string.Empty;

    /// <summary>The currency they trade in.</summary>
    public Currency Currency { get; private set; } = Currency.Default;

    /// <summary>How many days after a document they have to pay it. Zero means on receipt.</summary>
    public int PaymentDueInDays { get; private set; }

    /// <summary>
    /// True when the credit period runs to the end of the month it lands in.
    /// <para>
    /// "Thirty days end of month" is the common Portuguese arrangement and it is not thirty days:
    /// an invoice on the 2nd and one on the 28th both fall due on the last day of the following
    /// month. Getting this wrong makes every ageing report wrong by up to a month.
    /// </para>
    /// </summary>
    public bool PaymentEndOfMonth { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <inheritdoc />
    public string CreatedBy { get; set; } = string.Empty;

    /// <inheritdoc />
    public DateTimeOffset? ModifiedAtUtc { get; set; }

    /// <inheritdoc />
    public string? ModifiedBy { get; set; }

    /// <summary>True when they pay on receipt and nothing is ever on credit.</summary>
    public bool IsCashOnly => PaymentDueInDays == 0 && !PaymentEndOfMonth;

    /// <summary>Records a customer's terms, from the event Partners published.</summary>
    /// <param name="customerId">The customer.</param>
    /// <param name="code">Their code.</param>
    /// <param name="legalName">Their legal name.</param>
    /// <param name="currency">The currency they trade in.</param>
    /// <param name="paymentDueInDays">Their credit period in days.</param>
    /// <param name="paymentEndOfMonth">Whether that period runs to month end.</param>
    public static Result<CustomerTerms> Open(
        CustomerRef customerId,
        string? code,
        string? legalName,
        Currency currency,
        int paymentDueInDays,
        bool paymentEndOfMonth)
    {
        ArgumentNullException.ThrowIfNull(currency);

        if (customerId.IsEmpty)
        {
            return FinanceErrors.Terms.CustomerRequired;
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return FinanceErrors.Terms.CodeRequired;
        }

        if (paymentDueInDays is < 0 or > MaxPaymentDueInDays)
        {
            return FinanceErrors.Terms.PaymentDaysOutOfRange;
        }

        return new CustomerTerms(
            customerId,
            Clip(code, MaxCodeLength).ToUpperInvariant(),
            Clip(legalName, MaxNameLength),
            currency,
            paymentDueInDays,
            paymentEndOfMonth);
    }

    /// <summary>
    /// Changes the terms.
    /// <para>
    /// Documents already raised keep the due date they were given. A customer moved from thirty
    /// to sixty days has not been given another month on invoices they already have, and
    /// rewriting history is how an ageing report stops matching the letters that were sent.
    /// </para>
    /// </summary>
    /// <param name="legalName">Their legal name.</param>
    /// <param name="currency">The currency they trade in.</param>
    /// <param name="paymentDueInDays">Their credit period in days.</param>
    /// <param name="paymentEndOfMonth">Whether that period runs to month end.</param>
    public Result Change(
        string? legalName,
        Currency currency,
        int paymentDueInDays,
        bool paymentEndOfMonth)
    {
        ArgumentNullException.ThrowIfNull(currency);

        if (paymentDueInDays is < 0 or > MaxPaymentDueInDays)
        {
            return FinanceErrors.Terms.PaymentDaysOutOfRange;
        }

        if (!string.IsNullOrWhiteSpace(legalName))
        {
            LegalName = Clip(legalName, MaxNameLength);
        }

        Currency = currency;
        PaymentDueInDays = paymentDueInDays;
        PaymentEndOfMonth = paymentEndOfMonth;

        return Result.Success();
    }

    /// <summary>
    /// When a document dated <paramref name="documentDate"/> falls due under these terms.
    /// </summary>
    /// <param name="documentDate">The date on the document.</param>
    public DateOnly DueDateFor(DateOnly documentDate)
    {
        DateOnly due = documentDate.AddDays(PaymentDueInDays);

        if (!PaymentEndOfMonth)
        {
            return due;
        }

        return new DateOnly(
            due.Year, due.Month, DateTime.DaysInMonth(due.Year, due.Month));
    }

    private static string Clip(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string trimmed = value.Trim();

        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
