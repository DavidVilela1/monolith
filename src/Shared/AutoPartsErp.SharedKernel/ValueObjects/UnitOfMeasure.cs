using AutoPartsErp.SharedKernel.Primitives;

namespace AutoPartsErp.SharedKernel.ValueObjects;

/// <summary>
/// How a part is counted, stocked and sold. Parts distribution mixes discrete units
/// (a brake disc), pairs and sets (brake pads, wiper blades), and continuous measures
/// (bulk oil, brake line by the metre), so the unit is part of every quantity.
/// </summary>
public sealed class UnitOfMeasure : ValueObject
{
    private UnitOfMeasure(string code, string name, bool allowsFractions, int decimalPlaces)
    {
        Code = code;
        Name = name;
        AllowsFractions = allowsFractions;
        DecimalPlaces = decimalPlaces;
    }

    /// <summary>A single item.</summary>
    public static readonly UnitOfMeasure Each = new("EA", "Each", allowsFractions: false, decimalPlaces: 0);

    /// <summary>Two items sold together, such as a pair of shock absorbers.</summary>
    public static readonly UnitOfMeasure Pair = new("PR", "Pair", allowsFractions: false, decimalPlaces: 0);

    /// <summary>A kit sold as one sellable unit, such as an axle set of brake pads.</summary>
    public static readonly UnitOfMeasure Set = new("SET", "Set", allowsFractions: false, decimalPlaces: 0);

    /// <summary>A packed box of items.</summary>
    public static readonly UnitOfMeasure Box = new("BOX", "Box", allowsFractions: false, decimalPlaces: 0);

    /// <summary>Litres, for oils and fluids.</summary>
    public static readonly UnitOfMeasure Litre = new("L", "Litre", allowsFractions: true, decimalPlaces: 3);

    /// <summary>Kilograms, for bulk and weight-priced goods.</summary>
    public static readonly UnitOfMeasure Kilogram = new("KG", "Kilogram", allowsFractions: true, decimalPlaces: 3);

    /// <summary>Metres, for hose, cable and brake line.</summary>
    public static readonly UnitOfMeasure Metre = new("M", "Metre", allowsFractions: true, decimalPlaces: 2);

    /// <summary>Hours, used by service and labour lines.</summary>
    public static readonly UnitOfMeasure Hour = new("HR", "Hour", allowsFractions: true, decimalPlaces: 2);

    /// <summary>Every unit the system understands.</summary>
    public static readonly IReadOnlyCollection<UnitOfMeasure> All =
        [Each, Pair, Set, Box, Litre, Kilogram, Metre, Hour];

    /// <summary>Short code stored in the database and shown on documents.</summary>
    public string Code { get; }

    /// <summary>Display name.</summary>
    public string Name { get; }

    /// <summary>Whether a partial quantity is meaningful for this unit.</summary>
    public bool AllowsFractions { get; }

    /// <summary>How many decimal places quantities are rounded to.</summary>
    public int DecimalPlaces { get; }

    /// <summary>Looks up a unit by code, case-insensitively.</summary>
    /// <exception cref="ArgumentException">The code is not a registered unit.</exception>
    public static UnitOfMeasure FromCode(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        return TryFromCode(code, out UnitOfMeasure? unit)
            ? unit
            : throw new ArgumentException($"'{code}' is not a supported unit of measure.", nameof(code));
    }

    /// <summary>Attempts to look up a unit by code.</summary>
    public static bool TryFromCode(string? code, out UnitOfMeasure unit)
    {
        foreach (UnitOfMeasure candidate in All)
        {
            if (string.Equals(candidate.Code, code?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                unit = candidate;
                return true;
            }
        }

        unit = Each;
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
