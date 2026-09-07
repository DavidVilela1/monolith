using System.Globalization;

namespace AutoPartsErp.Modules.Finance.Domain;

/// <summary>Identity of an <see cref="Receivables.OpenItem"/>.</summary>
/// <param name="Value">The underlying identifier.</param>
public readonly record struct OpenItemId(Guid Value)
{
    /// <summary>The unset identifier.</summary>
    public static readonly OpenItemId Empty = new(Guid.Empty);

    /// <summary>Generates a new identifier.</summary>
    public static OpenItemId New() => new(OrderedGuid.Create());

    /// <summary>True when the identifier has not been set.</summary>
    public bool IsEmpty => Value == Guid.Empty;

    /// <inheritdoc />
    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}

/// <summary>Identity of a <see cref="Receipts.Receipt"/>.</summary>
/// <param name="Value">The underlying identifier.</param>
public readonly record struct ReceiptId(Guid Value)
{
    /// <summary>The unset identifier.</summary>
    public static readonly ReceiptId Empty = new(Guid.Empty);

    /// <summary>Generates a new identifier.</summary>
    public static ReceiptId New() => new(OrderedGuid.Create());

    /// <summary>True when the identifier has not been set.</summary>
    public bool IsEmpty => Value == Guid.Empty;

    /// <inheritdoc />
    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}

/// <summary>Identity of a <see cref="Receipts.ReceiptAllocation"/>.</summary>
/// <param name="Value">The underlying identifier.</param>
public readonly record struct ReceiptAllocationId(Guid Value)
{
    /// <summary>The unset identifier.</summary>
    public static readonly ReceiptAllocationId Empty = new(Guid.Empty);

    /// <summary>Generates a new identifier.</summary>
    public static ReceiptAllocationId New() => new(OrderedGuid.Create());

    /// <inheritdoc />
    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}

/// <summary>
/// A customer, as Finance refers to them.
/// <para>
/// The same Guid Partners issued and Sales and Invoicing already use. A reference rather than a
/// foreign key: the customer's name and address belong to Partners, and Finance holds only what
/// it needs to compute a due date and print a statement.
/// </para>
/// </summary>
/// <param name="Value">The partner's identifier.</param>
public readonly record struct CustomerRef(Guid Value)
{
    /// <summary>The unset reference.</summary>
    public static readonly CustomerRef Empty = new(Guid.Empty);

    /// <summary>True when the reference has not been set.</summary>
    public bool IsEmpty => Value == Guid.Empty;

    /// <inheritdoc />
    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}

/// <summary>
/// The document an open item was raised from, in Invoicing.
/// <para>
/// Finance never loads the document. It carries the identifier so that a statement line can be
/// traced back, and the number so that the statement reads the way the customer's copy does.
/// </para>
/// </summary>
/// <param name="Value">The document's identifier in Invoicing.</param>
public readonly record struct DocumentRef(Guid Value)
{
    /// <summary>The unset reference.</summary>
    public static readonly DocumentRef Empty = new(Guid.Empty);

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
