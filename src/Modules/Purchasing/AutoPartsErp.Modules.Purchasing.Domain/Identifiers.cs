using System.Globalization;

namespace AutoPartsErp.Modules.Purchasing.Domain;

/// <summary>Identity of a <see cref="Orders.PurchaseOrder"/>.</summary>
/// <param name="Value">The underlying identifier.</param>
public readonly record struct PurchaseOrderId(Guid Value)
{
    /// <summary>The unset identifier.</summary>
    public static readonly PurchaseOrderId Empty = new(Guid.Empty);

    /// <summary>Generates a new identifier.</summary>
    public static PurchaseOrderId New() => new(OrderedGuid.Create());

    /// <summary>True when the identifier has not been set.</summary>
    public bool IsEmpty => Value == Guid.Empty;

    /// <inheritdoc />
    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}

/// <summary>Identity of a <see cref="Orders.PurchaseOrderLine"/>.</summary>
/// <param name="Value">The underlying identifier.</param>
public readonly record struct PurchaseOrderLineId(Guid Value)
{
    /// <summary>The unset identifier.</summary>
    public static readonly PurchaseOrderLineId Empty = new(Guid.Empty);

    /// <summary>Generates a new identifier.</summary>
    public static PurchaseOrderLineId New() => new(OrderedGuid.Create());

    /// <summary>True when the identifier has not been set.</summary>
    public bool IsEmpty => Value == Guid.Empty;

    /// <inheritdoc />
    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}

/// <summary>Identity of a <see cref="Replenishment.ReplenishmentSuggestion"/>.</summary>
/// <param name="Value">The underlying identifier.</param>
public readonly record struct SuggestionId(Guid Value)
{
    /// <summary>The unset identifier.</summary>
    public static readonly SuggestionId Empty = new(Guid.Empty);

    /// <summary>Generates a new identifier.</summary>
    public static SuggestionId New() => new(OrderedGuid.Create());

    /// <summary>True when the identifier has not been set.</summary>
    public bool IsEmpty => Value == Guid.Empty;

    /// <inheritdoc />
    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}

/// <summary>
/// A supplier, as Purchasing knows them.
/// <para>
/// Its own type rather than a reference to <c>Partners.Domain.PartnerId</c>, for the same reason
/// Inventory keeps <c>PartRef</c>: Purchasing holds a supplier by identity and nothing else. It
/// never loads a Partner, never reads their addresses, and cannot be broken by a change to that
/// aggregate. Whether they are actually set up as a supplier is Partners' question to answer,
/// asked once when the order is raised.
/// </para>
/// </summary>
/// <param name="Value">The underlying identifier, matching Partners' PartnerId.</param>
public readonly record struct SupplierRef(Guid Value)
{
    /// <summary>The unset reference.</summary>
    public static readonly SupplierRef Empty = new(Guid.Empty);

    /// <summary>True when no supplier has been set.</summary>
    public bool IsEmpty => Value == Guid.Empty;

    /// <inheritdoc />
    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}

/// <summary>A part, as Purchasing knows it. Matches Catalog's PartId and Inventory's PartRef.</summary>
/// <param name="Value">The underlying identifier.</param>
public readonly record struct PartRef(Guid Value)
{
    /// <summary>The unset reference.</summary>
    public static readonly PartRef Empty = new(Guid.Empty);

    /// <summary>True when no part has been set.</summary>
    public bool IsEmpty => Value == Guid.Empty;

    /// <inheritdoc />
    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}

/// <summary>A warehouse, as Purchasing knows it. Matches Inventory's WarehouseId.</summary>
/// <param name="Value">The underlying identifier.</param>
public readonly record struct WarehouseRef(Guid Value)
{
    /// <summary>The unset reference.</summary>
    public static readonly WarehouseRef Empty = new(Guid.Empty);

    /// <summary>True when no warehouse has been set.</summary>
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
