using System.Globalization;

namespace AutoPartsErp.Modules.Catalog.Domain;

/// <summary>
/// Identity of a <see cref="Parts.Part"/>.
/// <para>
/// Strongly typed rather than a bare <see cref="Guid"/> so that passing a brand id where a part id
/// belongs is a compile error instead of a support ticket. The wrapper is a readonly record struct,
/// so it costs nothing at runtime compared to the raw Guid.
/// </para>
/// </summary>
/// <param name="Value">The underlying identifier.</param>
public readonly record struct PartId(Guid Value)
{
    /// <summary>The unset identifier.</summary>
    public static readonly PartId Empty = new(Guid.Empty);

    /// <summary>Generates a new identifier. Uses a v7-style ordered Guid for index locality.</summary>
    public static PartId New() => new(OrderedGuid.Create());

    /// <summary>True when the identifier has not been set.</summary>
    public bool IsEmpty => Value == Guid.Empty;

    /// <summary>Parses an identifier from its string form.</summary>
    public static PartId Parse(string value) => new(Guid.Parse(value));

    /// <summary>Attempts to parse an identifier.</summary>
    public static bool TryParse(string? value, out PartId id)
    {
        if (Guid.TryParse(value, out Guid parsed))
        {
            id = new PartId(parsed);
            return true;
        }

        id = Empty;
        return false;
    }

    /// <inheritdoc />
    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}

/// <summary>Identity of a <see cref="Brands.Brand"/>.</summary>
/// <param name="Value">The underlying identifier.</param>
public readonly record struct BrandId(Guid Value)
{
    /// <summary>The unset identifier.</summary>
    public static readonly BrandId Empty = new(Guid.Empty);

    /// <summary>Generates a new identifier.</summary>
    public static BrandId New() => new(OrderedGuid.Create());

    /// <summary>True when the identifier has not been set.</summary>
    public bool IsEmpty => Value == Guid.Empty;

    /// <summary>Parses an identifier from its string form.</summary>
    public static BrandId Parse(string value) => new(Guid.Parse(value));

    /// <summary>Attempts to parse an identifier.</summary>
    public static bool TryParse(string? value, out BrandId id)
    {
        if (Guid.TryParse(value, out Guid parsed))
        {
            id = new BrandId(parsed);
            return true;
        }

        id = Empty;
        return false;
    }

    /// <inheritdoc />
    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}

/// <summary>Identity of a <see cref="Categories.PartCategory"/>.</summary>
/// <param name="Value">The underlying identifier.</param>
public readonly record struct CategoryId(Guid Value)
{
    /// <summary>The unset identifier.</summary>
    public static readonly CategoryId Empty = new(Guid.Empty);

    /// <summary>Generates a new identifier.</summary>
    public static CategoryId New() => new(OrderedGuid.Create());

    /// <summary>True when the identifier has not been set.</summary>
    public bool IsEmpty => Value == Guid.Empty;

    /// <summary>Parses an identifier from its string form.</summary>
    public static CategoryId Parse(string value) => new(Guid.Parse(value));

    /// <summary>Attempts to parse an identifier.</summary>
    public static bool TryParse(string? value, out CategoryId id)
    {
        if (Guid.TryParse(value, out Guid parsed))
        {
            id = new CategoryId(parsed);
            return true;
        }

        id = Empty;
        return false;
    }

    /// <inheritdoc />
    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}

/// <summary>
/// Creates time-ordered <see cref="Guid"/> values.
/// <para>
/// Random Guids as primary keys scatter inserts across a B-tree and fragment the index; in a
/// catalogue that grows by tens of thousands of rows a month that shows up as slow writes.
/// These values sort by creation time, so inserts stay at the right-hand edge of the index.
/// </para>
/// </summary>
internal static class OrderedGuid
{
    /// <summary>Creates a new time-ordered identifier.</summary>
    public static Guid Create()
    {
        Span<byte> bytes = stackalloc byte[16];

        // Fill with randomness first, then stamp the clock over the leading bytes.
        Guid.NewGuid().TryWriteBytes(bytes, bigEndian: true, out _);

        Span<byte> timestampBytes = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(timestampBytes, DateTime.UtcNow.Ticks);

        // PostgreSQL compares uuid values as unsigned bytes from left to right,
        // so putting the clock in the leading six bytes makes new keys sort last.
        timestampBytes[2..8].CopyTo(bytes);

        return new Guid(bytes, bigEndian: true);
    }
}
