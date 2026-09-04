using System.Globalization;

namespace AutoPartsErp.Modules.Invoicing.Domain;

/// <summary>Identity of a <see cref="Series.DocumentSeries"/>.</summary>
/// <param name="Value">The underlying identifier.</param>
public readonly record struct DocumentSeriesId(Guid Value)
{
    /// <summary>The unset identifier.</summary>
    public static readonly DocumentSeriesId Empty = new(Guid.Empty);

    /// <summary>Generates a new identifier.</summary>
    public static DocumentSeriesId New() => new(OrderedGuid.Create());

    /// <summary>True when the identifier has not been set.</summary>
    public bool IsEmpty => Value == Guid.Empty;

    /// <inheritdoc />
    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}

/// <summary>Identity of an <see cref="Invoices.Invoice"/>.</summary>
/// <param name="Value">The underlying identifier.</param>
public readonly record struct InvoiceId(Guid Value)
{
    /// <summary>The unset identifier.</summary>
    public static readonly InvoiceId Empty = new(Guid.Empty);

    /// <summary>Generates a new identifier.</summary>
    public static InvoiceId New() => new(OrderedGuid.Create());

    /// <summary>True when the identifier has not been set.</summary>
    public bool IsEmpty => Value == Guid.Empty;

    /// <inheritdoc />
    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}

/// <summary>Identity of an <see cref="Invoices.InvoiceLine"/>.</summary>
/// <param name="Value">The underlying identifier.</param>
public readonly record struct InvoiceLineId(Guid Value)
{
    /// <summary>The unset identifier.</summary>
    public static readonly InvoiceLineId Empty = new(Guid.Empty);

    /// <summary>Generates a new identifier.</summary>
    public static InvoiceLineId New() => new(OrderedGuid.Create());

    /// <summary>True when the identifier has not been set.</summary>
    public bool IsEmpty => Value == Guid.Empty;

    /// <inheritdoc />
    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}

/// <summary>A customer, as Invoicing knows them: an identity and nothing else.</summary>
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

/// <summary>A part, as Invoicing knows it.</summary>
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

/// <summary>
/// The sales order an invoice was raised against.
/// <para>
/// Nullable everywhere it is used. Not every document has one — a credit note for a returned part
/// months later, or a counter sale rung up without an order behind it, are both normal.
/// </para>
/// </summary>
/// <param name="Value">The underlying identifier.</param>
public readonly record struct SalesOrderRef(Guid Value)
{
    /// <summary>The unset reference.</summary>
    public static readonly SalesOrderRef Empty = new(Guid.Empty);

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
