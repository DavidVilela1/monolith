using System.Globalization;
using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.SharedKernel.ValueObjects;

/// <summary>
/// An amount of something together with the unit it is measured in.
/// Carrying the unit alongside the number is what stops "5" litres of oil from being
/// received into stock as "5" drums.
/// </summary>
public sealed class Quantity : ValueObject, IComparable<Quantity>
{
    private Quantity(decimal value, UnitOfMeasure unit)
    {
        Unit = unit;
        Value = Math.Round(value, unit.DecimalPlaces, MidpointRounding.ToEven);
    }

    /// <summary>The numeric amount, rounded to the unit's precision.</summary>
    public decimal Value { get; }

    /// <summary>The unit the amount is expressed in.</summary>
    public UnitOfMeasure Unit { get; }

    /// <summary>True when the quantity is exactly zero.</summary>
    public bool IsZero => Value == 0m;

    /// <summary>True when the quantity is below zero.</summary>
    public bool IsNegative => Value < 0m;

    /// <summary>Creates a quantity, rejecting fractions for units that do not allow them.</summary>
    /// <exception cref="ArgumentException">A fraction was supplied for a discrete unit.</exception>
    public static Quantity Of(decimal value, UnitOfMeasure unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (!unit.AllowsFractions && value != Math.Truncate(value))
        {
            throw new ArgumentException(
                $"Unit '{unit.Code}' is counted in whole units; {value} is not valid.", nameof(value));
        }

        return new Quantity(value, unit);
    }

    /// <summary>
    /// Creates a quantity, reporting an invalid fraction as a failure rather than an exception.
    /// Use this wherever the value came from outside the process; <see cref="Of(decimal, UnitOfMeasure)"/>
    /// is for values the code itself already knows are valid.
    /// </summary>
    public static Result<Quantity> Create(decimal value, UnitOfMeasure unit)
    {
        ArgumentNullException.ThrowIfNull(unit);

        if (!unit.AllowsFractions && value != Math.Truncate(value))
        {
            return Error.Validation(
                "quantity.whole_units_only",
                $"'{unit.Name}' is counted in whole units, so {value} is not a valid quantity.");
        }

        return new Quantity(value, unit);
    }

    /// <summary>Creates a quantity in whole units of <see cref="UnitOfMeasure.Each"/>.</summary>
    public static Quantity Each(int value) => Of(value, UnitOfMeasure.Each);

    /// <summary>Zero in the given unit.</summary>
    public static Quantity Zero(UnitOfMeasure unit) => Of(0m, unit);

    /// <summary>Adds a quantity of the same unit.</summary>
    public Quantity Add(Quantity other)
    {
        EnsureSameUnit(other);
        return new Quantity(Value + other.Value, Unit);
    }

    /// <summary>Subtracts a quantity of the same unit.</summary>
    public Quantity Subtract(Quantity other)
    {
        EnsureSameUnit(other);
        return new Quantity(Value - other.Value, Unit);
    }

    /// <summary>Scales the quantity by a factor.</summary>
    public Quantity Multiply(decimal factor) => new(Value * factor, Unit);

    /// <summary>Adds two quantities.</summary>
    public static Quantity operator +(Quantity left, Quantity right) => Guarded(left).Add(Guarded(right));

    /// <summary>Subtracts two quantities.</summary>
    public static Quantity operator -(Quantity left, Quantity right) => Guarded(left).Subtract(Guarded(right));

    /// <summary>Compares two quantities of the same unit.</summary>
    public static bool operator >(Quantity left, Quantity right) => Guarded(left).CompareTo(right) > 0;

    /// <summary>Compares two quantities of the same unit.</summary>
    public static bool operator <(Quantity left, Quantity right) => Guarded(left).CompareTo(right) < 0;

    /// <summary>Compares two quantities of the same unit.</summary>
    public static bool operator >=(Quantity left, Quantity right) => Guarded(left).CompareTo(right) >= 0;

    /// <summary>Compares two quantities of the same unit.</summary>
    public static bool operator <=(Quantity left, Quantity right) => Guarded(left).CompareTo(right) <= 0;

    /// <inheritdoc />
    public int CompareTo(Quantity? other)
    {
        if (other is null)
        {
            return 1;
        }

        EnsureSameUnit(other);
        return Value.CompareTo(other.Value);
    }

    /// <inheritdoc />
    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
        yield return Unit;
    }

    /// <inheritdoc />
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Value} {Unit.Code}");

    private static Quantity Guarded(Quantity quantity)
    {
        ArgumentNullException.ThrowIfNull(quantity);
        return quantity;
    }

    private void EnsureSameUnit(Quantity other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (Unit != other.Unit)
        {
            throw new InvalidOperationException(
                $"Cannot combine quantities in {Unit.Code} and {other.Unit.Code}. Convert to a common unit first.");
        }
    }
}
