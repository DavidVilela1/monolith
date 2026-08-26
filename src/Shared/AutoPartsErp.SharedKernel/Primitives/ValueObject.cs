namespace AutoPartsErp.SharedKernel.Primitives;

/// <summary>
/// Base class for objects defined purely by their attributes: a price, a quantity, a SKU.
/// Value objects are immutable and compared component by component.
/// </summary>
public abstract class ValueObject : IEquatable<ValueObject>
{
    /// <summary>Yields the components that make up this value's identity, in a stable order.</summary>
    protected abstract IEnumerable<object?> GetEqualityComponents();

    /// <inheritdoc />
    public bool Equals(ValueObject? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return GetType() == other.GetType()
            && GetEqualityComponents().SequenceEqual(other.GetEqualityComponents());
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ValueObject other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = default(HashCode);
        foreach (object? component in GetEqualityComponents())
        {
            hash.Add(component);
        }

        return hash.ToHashCode();
    }

    /// <summary>Structural equality.</summary>
    public static bool operator ==(ValueObject? left, ValueObject? right) =>
        left?.Equals(right) ?? right is null;

    /// <summary>Structural inequality.</summary>
    public static bool operator !=(ValueObject? left, ValueObject? right) => !(left == right);
}
