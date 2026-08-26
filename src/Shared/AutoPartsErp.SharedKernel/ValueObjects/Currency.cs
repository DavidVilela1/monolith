using AutoPartsErp.SharedKernel.Primitives;

namespace AutoPartsErp.SharedKernel.ValueObjects;

/// <summary>
/// An ISO 4217 currency, including the number of minor units it is rounded to.
/// Only currencies registered here can be used, which stops typos ("EURO", "eur")
/// from reaching the ledger.
/// </summary>
public sealed class Currency : ValueObject
{
    private Currency(string code, string symbol, int decimalPlaces)
    {
        Code = code;
        Symbol = symbol;
        DecimalPlaces = decimalPlaces;
    }

    /// <summary>Euro.</summary>
    public static readonly Currency Eur = new("EUR", "€", 2);

    /// <summary>United States dollar.</summary>
    public static readonly Currency Usd = new("USD", "$", 2);

    /// <summary>Pound sterling.</summary>
    public static readonly Currency Gbp = new("GBP", "£", 2);

    /// <summary>Brazilian real.</summary>
    public static readonly Currency Brl = new("BRL", "R$", 2);

    /// <summary>Swiss franc.</summary>
    public static readonly Currency Chf = new("CHF", "CHF", 2);

    /// <summary>Japanese yen (no minor units).</summary>
    public static readonly Currency Jpy = new("JPY", "¥", 0);

    /// <summary>Every currency the system understands.</summary>
    public static readonly IReadOnlyCollection<Currency> All = [Eur, Usd, Gbp, Brl, Chf, Jpy];

    /// <summary>The currency used when none is specified. Change this per deployment.</summary>
    public static Currency Default => Eur;

    /// <summary>The three-letter ISO 4217 code, uppercase.</summary>
    public string Code { get; }

    /// <summary>The display symbol.</summary>
    public string Symbol { get; }

    /// <summary>How many decimal places amounts are rounded to.</summary>
    public int DecimalPlaces { get; }

    /// <summary>Looks up a currency by ISO code, case-insensitively.</summary>
    /// <exception cref="ArgumentException">The code is not a registered currency.</exception>
    public static Currency FromCode(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        return TryFromCode(code, out Currency? currency)
            ? currency
            : throw new ArgumentException($"'{code}' is not a supported currency code.", nameof(code));
    }

    /// <summary>Attempts to look up a currency by ISO code.</summary>
    public static bool TryFromCode(string? code, out Currency currency)
    {
        foreach (Currency candidate in All)
        {
            if (string.Equals(candidate.Code, code?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                currency = candidate;
                return true;
            }
        }

        currency = Default;
        return false;
    }

    /// <inheritdoc />
    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Code;
    }

    /// <inheritdoc />
    public override string ToString() => Code;
}
