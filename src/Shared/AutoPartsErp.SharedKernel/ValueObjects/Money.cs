using System.Globalization;
using AutoPartsErp.SharedKernel.Primitives;

namespace AutoPartsErp.SharedKernel.ValueObjects;

/// <summary>
/// An amount in a specific currency. Always <see cref="decimal"/>, never <see cref="double"/>:
/// binary floating point cannot represent 0.10 exactly, and an ERP that loses a cent per line
/// loses trust. Arithmetic between different currencies is rejected rather than guessed at.
/// </summary>
public sealed class Money : ValueObject, IComparable<Money>
{
    private Money(decimal amount, Currency currency)
    {
        Currency = currency;
        Amount = Math.Round(amount, currency.DecimalPlaces, MidpointRounding.ToEven);
    }

    /// <summary>
    /// Required by object-relational mappers that materialize this type as an owned value and
    /// write the backing fields directly. Domain code always goes through <see cref="Of(decimal, Currency)"/>.
    /// </summary>
#pragma warning disable CS8618
    private Money()
    {
    }
#pragma warning restore CS8618

    /// <summary>The rounded amount.</summary>
    public decimal Amount { get; }

    /// <summary>The currency the amount is expressed in.</summary>
    public Currency Currency { get; } = Currency.Default;

    /// <summary>True when the amount is exactly zero.</summary>
    public bool IsZero => Amount == 0m;

    /// <summary>True when the amount is below zero (a credit, refund or negative adjustment).</summary>
    public bool IsNegative => Amount < 0m;

    /// <summary>Creates an amount in the given currency.</summary>
    public static Money Of(decimal amount, Currency currency)
    {
        ArgumentNullException.ThrowIfNull(currency);
        return new Money(amount, currency);
    }

    /// <summary>Creates an amount from an ISO currency code.</summary>
    public static Money Of(decimal amount, string currencyCode) =>
        new(amount, Currency.FromCode(currencyCode));

    /// <summary>Zero in the given currency.</summary>
    public static Money Zero(Currency currency) => Of(0m, currency);

    /// <summary>Zero in the system default currency.</summary>
    public static Money ZeroDefault => Of(0m, Currency.Default);

    /// <summary>Adds two amounts of the same currency.</summary>
    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount + other.Amount, Currency);
    }

    /// <summary>Subtracts an amount of the same currency.</summary>
    public Money Subtract(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount - other.Amount, Currency);
    }

    /// <summary>Multiplies by a scalar, for example a line quantity.</summary>
    public Money Multiply(decimal factor) => new(Amount * factor, Currency);

    /// <summary>Divides by a scalar.</summary>
    /// <exception cref="DivideByZeroException">The divisor is zero.</exception>
    public Money Divide(decimal divisor)
    {
        if (divisor == 0m)
        {
            throw new DivideByZeroException("Cannot divide a monetary amount by zero.");
        }

        return new Money(Amount / divisor, Currency);
    }

    /// <summary>Applies a percentage, for example a 12.5% discount or a 23% VAT rate.</summary>
    public Money Percentage(decimal percent) => new(Amount * percent / 100m, Currency);

    /// <summary>Returns the amount with the opposite sign.</summary>
    public Money Negate() => new(-Amount, Currency);

    /// <summary>Adds two amounts.</summary>
    public static Money operator +(Money left, Money right) => Guarded(left).Add(Guarded(right));

    /// <summary>Subtracts two amounts.</summary>
    public static Money operator -(Money left, Money right) => Guarded(left).Subtract(Guarded(right));

    /// <summary>Multiplies by a scalar.</summary>
    public static Money operator *(Money money, decimal factor) => Guarded(money).Multiply(factor);

    /// <summary>Divides by a scalar.</summary>
    public static Money operator /(Money money, decimal divisor) => Guarded(money).Divide(divisor);

    /// <summary>Negates an amount.</summary>
    public static Money operator -(Money money) => Guarded(money).Negate();

    /// <summary>Compares two amounts of the same currency.</summary>
    public static bool operator >(Money left, Money right) => Guarded(left).CompareTo(right) > 0;

    /// <summary>Compares two amounts of the same currency.</summary>
    public static bool operator <(Money left, Money right) => Guarded(left).CompareTo(right) < 0;

    /// <summary>Compares two amounts of the same currency.</summary>
    public static bool operator >=(Money left, Money right) => Guarded(left).CompareTo(right) >= 0;

    /// <summary>Compares two amounts of the same currency.</summary>
    public static bool operator <=(Money left, Money right) => Guarded(left).CompareTo(right) <= 0;

    /// <inheritdoc />
    public int CompareTo(Money? other)
    {
        if (other is null)
        {
            return 1;
        }

        EnsureSameCurrency(other);
        return Amount.CompareTo(other.Amount);
    }

    /// <inheritdoc />
    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }

    /// <inheritdoc />
    public override string ToString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{Amount.ToString($"F{Currency.DecimalPlaces}", CultureInfo.InvariantCulture)} {Currency.Code}");

    private static Money Guarded(Money money)
    {
        ArgumentNullException.ThrowIfNull(money);
        return money;
    }

    private void EnsureSameCurrency(Money other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (Currency != other.Currency)
        {
            throw new InvalidOperationException(
                $"Cannot combine {Currency.Code} with {other.Currency.Code}. Convert to a common currency first.");
        }
    }
}
