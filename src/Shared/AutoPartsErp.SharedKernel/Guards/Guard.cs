using System.Runtime.CompilerServices;

namespace AutoPartsErp.SharedKernel.Guards;

/// <summary>
/// Argument checks for invariants that must never be violated by correct code.
/// Guards protect against programmer error and therefore throw; expected business
/// failures return <see cref="Results.Result"/> instead.
/// </summary>
public static class Guard
{
    /// <summary>Throws when the value is null.</summary>
    public static T NotNull<T>(T? value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(value, paramName);
        return value;
    }

    /// <summary>Throws when the string is null, empty or whitespace, and trims the result.</summary>
    public static string NotNullOrWhiteSpace(
        string? value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);
        return value.Trim();
    }

    /// <summary>Throws when the trimmed string exceeds <paramref name="maxLength"/>.</summary>
    public static string MaxLength(
        string value,
        int maxLength,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);
        string trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentException(
                $"Value exceeds the maximum length of {maxLength} characters.", paramName);
        }

        return trimmed;
    }

    /// <summary>Throws when the value is negative.</summary>
    public static decimal NotNegative(
        decimal value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value, paramName);
        return value;
    }

    /// <summary>Throws when the value is zero or negative.</summary>
    public static int Positive(int value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value, paramName);
        return value;
    }

    /// <summary>Throws when the identifier is the default (empty) value.</summary>
    public static Guid NotEmpty(Guid value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Identifier must not be empty.", paramName);
        }

        return value;
    }
}
