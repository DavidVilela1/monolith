using System.Text;
using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Catalog.Domain.Parts;

/// <summary>
/// A manufacturer's part number, kept in two forms at once.
/// <para>
/// Parts numbers are written inconsistently everywhere they appear: Bosch print
/// <c>0 986 424 815</c>, the supplier's price file says <c>0986424815</c>, and the customer on
/// the phone reads <c>0986-424-815</c> off an old box. <see cref="Display"/> keeps what the
/// manufacturer actually prints, for documents and labels; <see cref="Normalized"/> strips
/// everything that is not a letter or digit and uppercases the rest, and that is what every
/// lookup and uniqueness constraint uses. Searching on the normalized form is the single
/// difference between a counter system people trust and one they work around.
/// </para>
/// </summary>
public sealed class PartNumber : ValueObject
{
    /// <summary>Longest permitted part number in display form.</summary>
    public const int MaxLength = 60;

    private PartNumber(string display, string normalized)
    {
        Display = display;
        Normalized = normalized;
    }

    /// <summary>
    /// Required by EF Core, which maps this value object as an owned type and writes the
    /// backing fields directly. Domain code always goes through <see cref="Create"/>.
    /// </summary>
#pragma warning disable CS8618
    private PartNumber()
    {
    }
#pragma warning restore CS8618

    /// <summary>The number as the manufacturer prints it, including spaces and separators.</summary>
    public string Display { get; } = string.Empty;

    /// <summary>Uppercase, letters and digits only. Used for all matching and indexing.</summary>
    public string Normalized { get; } = string.Empty;

    /// <summary>Creates a part number from whatever the user or import file supplied.</summary>
    public static Result<PartNumber> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return CatalogErrors.Part.PartNumberRequired;
        }

        string display = value.Trim();

        if (display.Length > MaxLength)
        {
            return CatalogErrors.Part.PartNumberTooLong;
        }

        string normalized = Normalize(display);

        return normalized.Length == 0
            ? CatalogErrors.Part.PartNumberInvalid
            : new PartNumber(display, normalized);
    }

    /// <summary>Rehydrates a part number already known to be valid.</summary>
    public static PartNumber FromStorage(string display, string normalized) => new(display, normalized);

    /// <summary>
    /// Reduces a part number to its comparable form: uppercase, letters and digits only.
    /// Call this on user input before searching so the counter finds the part however it was typed.
    /// </summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);

        foreach (char character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToUpperInvariant(character));
            }
        }

        return builder.ToString();
    }

    /// <inheritdoc />
    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Normalized;
    }

    /// <inheritdoc />
    public override string ToString() => Display;
}
