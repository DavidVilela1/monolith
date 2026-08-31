using System.Globalization;

namespace AutoPartsErp.Modules.Inventory.Domain;

/// <summary>
/// A part, as Inventory knows it.
/// <para>
/// Deliberately its own type rather than a reference to <c>Catalog.Domain.PartId</c>. Inventory
/// holds a part by identity and nothing else: it never loads a Part, never reads its brand or
/// fitments, and cannot be broken by a change to the Catalog aggregate. The two modules meet
/// only at this Guid and at the integration events they exchange.
/// </para>
/// </summary>
/// <param name="Value">The underlying identifier, matching Catalog's PartId.</param>
public readonly record struct PartRef(Guid Value)
{
    /// <summary>The unset reference.</summary>
    public static readonly PartRef Empty = new(Guid.Empty);

    /// <summary>True when no part has been set.</summary>
    public bool IsEmpty => Value == Guid.Empty;

    /// <inheritdoc />
    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}

/// <summary>Identity of a <see cref="Warehouses.Warehouse"/>.</summary>
/// <param name="Value">The underlying identifier.</param>
public readonly record struct WarehouseId(Guid Value)
{
    /// <summary>The unset identifier.</summary>
    public static readonly WarehouseId Empty = new(Guid.Empty);

    /// <summary>Generates a new identifier.</summary>
    public static WarehouseId New() => new(OrderedGuid.Create());

    /// <summary>True when the identifier has not been set.</summary>
    public bool IsEmpty => Value == Guid.Empty;

    /// <inheritdoc />
    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}

/// <summary>Identity of a <see cref="Warehouses.StorageBin"/>.</summary>
/// <param name="Value">The underlying identifier.</param>
public readonly record struct BinId(Guid Value)
{
    /// <summary>The unset identifier.</summary>
    public static readonly BinId Empty = new(Guid.Empty);

    /// <summary>Generates a new identifier.</summary>
    public static BinId New() => new(OrderedGuid.Create());

    /// <summary>True when the identifier has not been set.</summary>
    public bool IsEmpty => Value == Guid.Empty;

    /// <inheritdoc />
    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}

/// <summary>Identity of a <see cref="Stock.StockItem"/>: one part in one warehouse.</summary>
/// <param name="Value">The underlying identifier.</param>
public readonly record struct StockItemId(Guid Value)
{
    /// <summary>The unset identifier.</summary>
    public static readonly StockItemId Empty = new(Guid.Empty);

    /// <summary>Generates a new identifier.</summary>
    public static StockItemId New() => new(OrderedGuid.Create());

    /// <summary>True when the identifier has not been set.</summary>
    public bool IsEmpty => Value == Guid.Empty;

    /// <inheritdoc />
    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}

/// <summary>Identity of a <see cref="Stock.StockMovement"/>.</summary>
/// <param name="Value">The underlying identifier.</param>
public readonly record struct MovementId(Guid Value)
{
    /// <summary>The unset identifier.</summary>
    public static readonly MovementId Empty = new(Guid.Empty);

    /// <summary>Generates a new identifier.</summary>
    public static MovementId New() => new(OrderedGuid.Create());

    /// <inheritdoc />
    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}

/// <summary>Identity of a <see cref="Stock.StockReservation"/>.</summary>
/// <param name="Value">The underlying identifier.</param>
public readonly record struct ReservationId(Guid Value)
{
    /// <summary>The unset identifier.</summary>
    public static readonly ReservationId Empty = new(Guid.Empty);

    /// <summary>Generates a new identifier.</summary>
    public static ReservationId New() => new(OrderedGuid.Create());

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
