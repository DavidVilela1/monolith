using System.Globalization;

namespace AutoPartsErp.Modules.Pricing.Domain;

/// <summary>Identity of a <see cref="PriceLists.PriceList"/>.</summary>
/// <param name="Value">The underlying identifier.</param>
public readonly record struct PriceListId(Guid Value)
{
    /// <summary>The unset identifier.</summary>
    public static readonly PriceListId Empty = new(Guid.Empty);

    /// <summary>Generates a new identifier.</summary>
    public static PriceListId New() => new(OrderedGuid.Create());

    /// <summary>True when the identifier has not been set.</summary>
    public bool IsEmpty => Value == Guid.Empty;

    /// <inheritdoc />
    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}

/// <summary>Identity of a <see cref="PriceLists.PriceListEntry"/>.</summary>
/// <param name="Value">The underlying identifier.</param>
public readonly record struct PriceListEntryId(Guid Value)
{
    /// <summary>The unset identifier.</summary>
    public static readonly PriceListEntryId Empty = new(Guid.Empty);

    /// <summary>Generates a new identifier.</summary>
    public static PriceListEntryId New() => new(OrderedGuid.Create());

    /// <summary>True when the identifier has not been set.</summary>
    public bool IsEmpty => Value == Guid.Empty;

    /// <inheritdoc />
    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}

/// <summary>Identity of a <see cref="Customers.CustomerPricing"/>.</summary>
/// <param name="Value">The underlying identifier.</param>
public readonly record struct CustomerPricingId(Guid Value)
{
    /// <summary>The unset identifier.</summary>
    public static readonly CustomerPricingId Empty = new(Guid.Empty);

    /// <summary>Generates a new identifier.</summary>
    public static CustomerPricingId New() => new(OrderedGuid.Create());

    /// <summary>True when the identifier has not been set.</summary>
    public bool IsEmpty => Value == Guid.Empty;

    /// <inheritdoc />
    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}

/// <summary>
/// A part, as Pricing knows it.
/// <para>
/// Its own type rather than a reference to Catalog's <c>PartId</c>, for the same reason
/// Inventory and Purchasing each keep their own: Pricing holds a part by identity and nothing
/// else. What it is called and how it is counted belong to the catalogue, and a price list that
/// referenced Catalog's types would be a module boundary in name only.
/// </para>
/// </summary>
/// <param name="Value">The underlying identifier.</param>
public readonly record struct PartRef(Guid Value)
{
    /// <summary>The unset reference.</summary>
    public static readonly PartRef Empty = new(Guid.Empty);

    /// <summary>True when the reference has not been set.</summary>
    public bool IsEmpty => Value == Guid.Empty;

    /// <inheritdoc />
    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}

/// <summary>A customer, as Pricing knows them: an identity and nothing else.</summary>
/// <param name="Value">The underlying identifier.</param>
public readonly record struct CustomerRef(Guid Value)
{
    /// <summary>The unset reference.</summary>
    public static readonly CustomerRef Empty = new(Guid.Empty);

    /// <summary>True when the reference has not been set.</summary>
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
