using System.Text.RegularExpressions;
using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Catalog.Domain.Parts;

/// <summary>
/// The distributor's own stock keeping unit: the code printed on the bin label and typed
/// into the counter terminal all day. Always stored uppercase and trimmed, so
/// <c>bp-1234</c> and <c>BP-1234 </c> can never become two different rows.
/// </summary>
public sealed partial class Sku : ValueObject
{
    /// <summary>Longest permitted SKU.</summary>
    public const int MaxLength = 40;

    private Sku(string value)
    {
        Value = value;
    }

    /// <summary>The normalized code.</summary>
    public string Value { get; }

    /// <summary>
    /// Creates a SKU, normalizing case and whitespace.
    /// Letters, digits, hyphen, dot, slash and underscore are allowed; nothing else, because
    /// stray characters break barcode printing and EDI files downstream.
    /// </summary>
    public static Result<Sku> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return CatalogErrors.Part.SkuRequired;
        }

        string normalized = value.Trim().ToUpperInvariant();

        if (normalized.Length > MaxLength)
        {
            return CatalogErrors.Part.SkuTooLong;
        }

        return AllowedPattern().IsMatch(normalized)
            ? new Sku(normalized)
            : CatalogErrors.Part.SkuInvalidCharacters;
    }

    /// <summary>Rehydrates a SKU already known to be valid, for use by the persistence layer.</summary>
    public static Sku FromStorage(string value) => new(value);

    /// <inheritdoc />
    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    /// <inheritdoc />
    public override string ToString() => Value;

    [GeneratedRegex(@"^[A-Z0-9][A-Z0-9\-\._/]*$", RegexOptions.CultureInvariant)]
    private static partial Regex AllowedPattern();
}
