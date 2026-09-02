using AutoPartsErp.Modules.Sales.Domain.Customers.Events;
using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Sales.Domain.Customers;

/// <summary>
/// What Sales knows about a customer: whether they may buy, and for how much.
/// <para>
/// This is a projection, not a second copy of <c>Partner</c>. Partners owns who the company is —
/// their name, tax number, addresses, contacts. Sales owns the answer to one question that has
/// to be right in the seconds before somebody walks out with the goods: can this account take
/// this order? Keeping that answer local is what lets the counter work without a call across a
/// module boundary on every keystroke.
/// </para>
/// <para>
/// It is built from facts Partners publishes and never edited by hand — except for the credit
/// <i>exposure</i>, which is Sales' own number and nobody else's. Partners says what the limit
/// is; Sales knows how much of it is currently spoken for.
/// </para>
/// </summary>
public sealed class CustomerAccount : AggregateRoot<CustomerRef>, IAuditable, ITenantScoped
{
    /// <summary>Longest permitted customer code.</summary>
    public const int MaxCodeLength = 20;

    /// <summary>Longest permitted name.</summary>
    public const int MaxNameLength = 200;

    /// <summary>Longest permitted hold reason.</summary>
    public const int MaxReasonLength = 300;

    /// <summary>The share of a limit at which somebody should be told, before the refusal happens.</summary>
    private const decimal WarningThreshold = 0.9m;

    private CustomerAccount(
        CustomerRef id,
        string code,
        string legalName,
        Money creditLimit,
        int paymentDueInDays,
        bool paymentEndOfMonth,
        string? priceListCode)
        : base(id)
    {
        Code = code;
        LegalName = legalName;
        CreditLimit = creditLimit;
        Committed = Money.Zero(creditLimit.Currency);
        PaymentDueInDays = paymentDueInDays;
        PaymentEndOfMonth = paymentEndOfMonth;
        PriceListCode = priceListCode;
        Status = CustomerStatus.Active;
        CreatedBy = string.Empty;
    }

    /// <summary>Required by EF Core materialization.</summary>
#pragma warning disable CS8618
    private CustomerAccount()
    {
    }
#pragma warning restore CS8618

    /// <summary>Their short code, as typed at the counter.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>Their registered name, as it goes on an invoice.</summary>
    public string LegalName { get; private set; } = string.Empty;

    /// <summary>How much they may owe at once. Zero means cash only.</summary>
    public Money CreditLimit { get; private set; } = null!;

    /// <summary>
    /// The value of confirmed orders that have not yet gone out.
    /// <para>
    /// Sales' own figure, not Finance's. It is deliberately not the invoice ledger balance: the
    /// point is to stop the fourth order of the day going out to someone who has already had
    /// three, hours before any of them are invoiced.
    /// </para>
    /// </summary>
    public Money Committed { get; private set; } = null!;

    /// <summary>Days to pay. Zero means on delivery.</summary>
    public int PaymentDueInDays { get; private set; }

    /// <summary>True when the days run from the end of the invoice month.</summary>
    public bool PaymentEndOfMonth { get; private set; }

    /// <summary>Which price list applies.</summary>
    public string? PriceListCode { get; private set; }

    /// <summary>Whether they may buy.</summary>
    public CustomerStatus Status { get; private set; }

    /// <summary>Why they are on hold, when they are.</summary>
    public string? HoldReason { get; private set; }

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

    /// <summary>The currency this account trades in.</summary>
    public Currency Currency => CreditLimit.Currency;

    /// <summary>True when they may place new orders.</summary>
    public bool CanTakeOrders => Status == CustomerStatus.Active;

    /// <summary>True when they pay before the goods leave, so no credit is at risk.</summary>
    public bool IsCashOnly => PaymentDueInDays == 0 || CreditLimit.IsZero;

    /// <summary>How much of their limit is left.</summary>
    public Money AvailableCredit
    {
        get
        {
            Money remaining = CreditLimit - Committed;

            return remaining.IsNegative ? Money.Zero(Currency) : remaining;
        }
    }

    /// <summary>Opens the account from what Partners published.</summary>
    /// <param name="customerId">The partner, whose identity this account shares.</param>
    /// <param name="code">Their short code.</param>
    /// <param name="legalName">Their registered name.</param>
    /// <param name="creditLimit">How much they may owe at once.</param>
    /// <param name="paymentDueInDays">Days to pay.</param>
    /// <param name="paymentEndOfMonth">True to count from month end.</param>
    /// <param name="priceListCode">Which price list applies.</param>
    public static Result<CustomerAccount> Open(
        CustomerRef customerId,
        string? code,
        string? legalName,
        Money creditLimit,
        int paymentDueInDays,
        bool paymentEndOfMonth = false,
        string? priceListCode = null)
    {
        ArgumentNullException.ThrowIfNull(creditLimit);

        if (customerId.IsEmpty)
        {
            return SalesErrors.Order.CustomerRequired;
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return SalesErrors.Customer.CodeRequired;
        }

        if (string.IsNullOrWhiteSpace(legalName))
        {
            return SalesErrors.Customer.NameRequired;
        }

        if (creditLimit.IsNegative)
        {
            return SalesErrors.Customer.CreditLimitNegative;
        }

        var account = new CustomerAccount(
            customerId,
            Clip(code, MaxCodeLength).ToUpperInvariant(),
            Clip(legalName, MaxNameLength),
            creditLimit,
            paymentDueInDays,
            paymentEndOfMonth,
            Clean(priceListCode, MaxCodeLength));

        account.Raise(new CustomerAccountOpenedDomainEvent(customerId, account.Code));

        return account;
    }

    /// <summary>
    /// Re-applies the terms Partners published.
    /// <para>
    /// Safe to call with the same values repeatedly, because it will be: the account is fed by
    /// an at-least-once event stream, and a redelivered "customer opened" is an update rather
    /// than an error.
    /// </para>
    /// </summary>
    public Result ApplyTerms(
        string? code,
        string? legalName,
        Money creditLimit,
        int paymentDueInDays,
        bool paymentEndOfMonth,
        string? priceListCode)
    {
        ArgumentNullException.ThrowIfNull(creditLimit);

        if (creditLimit.IsNegative)
        {
            return SalesErrors.Customer.CreditLimitNegative;
        }

        if (!string.IsNullOrWhiteSpace(code))
        {
            Code = Clip(code, MaxCodeLength).ToUpperInvariant();
        }

        if (!string.IsNullOrWhiteSpace(legalName))
        {
            LegalName = Clip(legalName, MaxNameLength);
        }

        CreditLimit = creditLimit;
        PaymentDueInDays = paymentDueInDays;
        PaymentEndOfMonth = paymentEndOfMonth;
        PriceListCode = Clean(priceListCode, MaxCodeLength);

        // Reopen an account that was closed and then granted the role again. Partners is the
        // authority on the relationship existing; Sales does not get a veto.
        if (Status == CustomerStatus.Closed)
        {
            Status = CustomerStatus.Active;
            HoldReason = null;
        }

        return Result.Success();
    }

    /// <summary>Changes the limit, leaving what is already committed alone.</summary>
    public Result ChangeCreditLimit(Money creditLimit)
    {
        ArgumentNullException.ThrowIfNull(creditLimit);

        if (creditLimit.IsNegative)
        {
            return SalesErrors.Customer.CreditLimitNegative;
        }

        if (creditLimit.Currency != Currency)
        {
            return SalesErrors.Customer.CurrencyMismatch;
        }

        // Deliberately not checked against Committed. Dropping a limit below what is already
        // out is a real thing that happens after a bad payment run, and the existing orders are
        // already promised - the new limit binds the next one.
        CreditLimit = creditLimit;

        return Result.Success();
    }

    /// <summary>Stops new orders.</summary>
    /// <param name="reason">Why, in words somebody can repeat to them.</param>
    public Result PlaceOnHold(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return SalesErrors.Customer.HoldReasonRequired;
        }

        Status = CustomerStatus.OnHold;
        HoldReason = Clip(reason, MaxReasonLength);

        return Result.Success();
    }

    /// <summary>Lets them buy again.</summary>
    public Result ReleaseHold()
    {
        if (Status == CustomerStatus.Closed)
        {
            return SalesErrors.Customer.Closed;
        }

        Status = CustomerStatus.Active;
        HoldReason = null;

        return Result.Success();
    }

    /// <summary>Ends the relationship. Kept so historical orders still resolve.</summary>
    public Result Close()
    {
        Status = CustomerStatus.Closed;
        HoldReason = null;

        return Result.Success();
    }

    /// <summary>The check the counter is really asking: may this account order at all?</summary>
    public Result EnsureCanTrade() => Status switch
    {
        CustomerStatus.Active => Result.Success(),
        CustomerStatus.OnHold => SalesErrors.Customer.OnHold(HoldReason ?? "no reason recorded"),
        _ => SalesErrors.Customer.Closed,
    };

    /// <summary>
    /// Sets aside credit for an order that has just been confirmed.
    /// <para>
    /// Committing at confirmation rather than at invoicing is the whole point. An account with a
    /// 5,000 limit and four unshipped 2,000 orders is over its limit in every way that matters,
    /// and a system that only counts invoices will cheerfully take the fifth.
    /// </para>
    /// </summary>
    /// <param name="amount">The order's gross value.</param>
    public Result Commit(Money amount)
    {
        ArgumentNullException.ThrowIfNull(amount);

        if (amount.Currency != Currency)
        {
            return SalesErrors.Customer.CurrencyMismatch;
        }

        Result canTrade = EnsureCanTrade();
        if (canTrade.IsFailure)
        {
            return canTrade;
        }

        if (amount > AvailableCredit)
        {
            return SalesErrors.Customer.CreditLimitExceeded(
                AvailableCredit.Amount, amount.Amount, Currency.Code);
        }

        Committed += amount;

        if (!CreditLimit.IsZero && Committed >= CreditLimit * WarningThreshold)
        {
            Raise(new CustomerCreditNearlyExhaustedDomainEvent(
                Id, Code, Committed.Amount, CreditLimit.Amount));
        }

        return Result.Success();
    }

    /// <summary>Gives credit back when an order ships or is cancelled.</summary>
    /// <param name="amount">The value being released.</param>
    public Result ReleaseCommitment(Money amount)
    {
        ArgumentNullException.ThrowIfNull(amount);

        if (amount.Currency != Currency)
        {
            return SalesErrors.Customer.CurrencyMismatch;
        }

        if (amount > Committed)
        {
            return SalesErrors.Customer.ReleaseExceedsCommitment;
        }

        Committed -= amount;

        return Result.Success();
    }

    private static string Clip(string value, int maxLength)
    {
        string trimmed = value.Trim();

        return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }

    private static string? Clean(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : Clip(value, maxLength);
}

/// <summary>Whether a customer may buy.</summary>
public enum CustomerStatus
{
    /// <summary>Unspecified. Never persisted.</summary>
    Unknown = 0,

    /// <summary>Trading normally.</summary>
    Active = 1,

    /// <summary>No new orders, usually because of an overdue account. Reversible.</summary>
    OnHold = 2,

    /// <summary>The relationship has ended.</summary>
    Closed = 3,
}
