using System.Globalization;

namespace AutoPartsErp.Modules.Partners.Domain;

/// <summary>Identity of a <see cref="Partners.Partner"/>.</summary>
/// <param name="Value">The underlying identifier.</param>
public readonly record struct PartnerId(Guid Value)
{
    /// <summary>The unset identifier.</summary>
    public static readonly PartnerId Empty = new(Guid.Empty);

    /// <summary>Generates a new identifier.</summary>
    public static PartnerId New() => new(OrderedGuid.Create());

    /// <summary>True when the identifier has not been set.</summary>
    public bool IsEmpty => Value == Guid.Empty;

    /// <summary>Parses an identifier from its string form.</summary>
    public static PartnerId Parse(string value) => new(Guid.Parse(value));

    /// <summary>Attempts to parse an identifier.</summary>
    public static bool TryParse(string? value, out PartnerId id)
    {
        if (Guid.TryParse(value, out Guid parsed))
        {
            id = new PartnerId(parsed);
            return true;
        }

        id = Empty;
        return false;
    }

    /// <inheritdoc />
    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}

/// <summary>Creates time-ordered <see cref="Guid"/> values so inserts stay at the right edge of the index.</summary>
internal static class OrderedGuid
{
    /// <summary>Creates a new time-ordered identifier.</summary>
    public static Guid Create()
    {
        Span<byte> bytes = stackalloc byte[16];
        Guid.NewGuid().TryWriteBytes(bytes, bigEndian: true, out _);

        Span<byte> timestampBytes = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(timestampBytes, DateTime.UtcNow.Ticks);
        timestampBytes[2..8].CopyTo(bytes);

        return new Guid(bytes, bigEndian: true);
    }
}
