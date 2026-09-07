using System.Globalization;

namespace AutoPartsErp.Modules.Sales.Domain;

/// <summary>Identity of a <see cref="Orders.SalesOrder"/>.</summary>
/// <param name="Value">The underlying identifier.</param>
public readonly record struct SalesOrderId(Guid Value)
{
    /// <summary>The unset identifier.</summary>
    public static readonly SalesOrderId Empty = new(Guid.Empty);

    /// <summary>Generates a new identifier.</summary>
    public static SalesOrderId New() => new(OrderedGuid.Create());

    /// <summary>True when the identifier has not been set.</summary>
    public bool IsEmpty => Value == Guid.Empty;

    /// <inheritdoc />
    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}

/// <summary>Identity of a <see cref="Orders.SalesOrderLine"/>.</summary>
/// <param name="Value">The underlying identifier.</param>
public readonly record struct SalesOrderLineId(Guid Value)
{
    /// <summary>The unset identifier.</summary>
    public static readonly SalesOrderLineId Empty = new(Guid.Empty);

    /// <summary>Generates a new identifier.</summary>
    public static SalesOrderLineId New() => new(OrderedGuid.Create());

    /// <summary>True when the identifier has not been set.</summary>
    public bool IsEmpty => Value == Guid.Empty;

    /// <inheritdoc />
    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}

/// <summary>
/// A customer, as Sales knows them.
/// <para>
/// The same Guid Partners uses for the partner, and also the identity of Sales' own
/// <see cref="Customers.CustomerAccount"/>. One account per partner, so there is no second
/// identifier to keep in step and no lookup table between the two modules.
/// </para>
/// </summary>
/// <param name="Value">The underlying identifier, matching Partners' PartnerId.</param>
public readonly record struct CustomerRef(Guid Value)
{
    /// <summary>The unset reference.</summary>
    public static readonly CustomerRef Empty = new(Guid.Empty);

    /// <summary>True when no customer has been set.</summary>
    public bool IsEmpty => Value == Guid.Empty;

    /// <inheritdoc />
    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}

/// <summary>A part, as Sales knows it. Matches Catalog's PartId.</summary>
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

/// <summary>A warehouse, as Sales knows it. Matches Inventory's WarehouseId.</summary>
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

/// <summary>
/// A reference to a document in the Invoicing module.
/// <para>
/// A bare identifier and nothing more. Sales does not know what an invoice is, cannot load one,
/// and has no opinion about its number beyond printing it back — it learns both from an
/// integration event and keeps them so an order cannot be billed twice.
/// </para>
/// </summary>
/// <param name="Value">The underlying identifier.</param>
public readonly record struct InvoiceRef(Guid Value)
{
    /// <summary>The unset reference.</summary>
    public static readonly InvoiceRef Empty = new(Guid.Empty);

    /// <summary>True when the reference has not been set.</summary>
    public bool IsEmpty => Value == Guid.Empty;

    /// <inheritdoc />
    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}
