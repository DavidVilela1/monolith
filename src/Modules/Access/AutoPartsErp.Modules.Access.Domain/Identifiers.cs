using System.Globalization;

namespace AutoPartsErp.Modules.Access.Domain;

/// <summary>Identity of a <see cref="Users.User"/>.</summary>
/// <param name="Value">The underlying identifier.</param>
public readonly record struct UserId(Guid Value)
{
    /// <summary>The unset identifier.</summary>
    public static readonly UserId Empty = new(Guid.Empty);

    /// <summary>Generates a new identifier.</summary>
    public static UserId New() => new(OrderedGuid.Create());

    /// <summary>True when the identifier has not been set.</summary>
    public bool IsEmpty => Value == Guid.Empty;

    /// <inheritdoc />
    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}

/// <summary>Identity of a <see cref="Roles.Role"/>.</summary>
/// <param name="Value">The underlying identifier.</param>
public readonly record struct RoleId(Guid Value)
{
    /// <summary>The unset identifier.</summary>
    public static readonly RoleId Empty = new(Guid.Empty);

    /// <summary>Generates a new identifier.</summary>
    public static RoleId New() => new(OrderedGuid.Create());

    /// <summary>True when the identifier has not been set.</summary>
    public bool IsEmpty => Value == Guid.Empty;

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
