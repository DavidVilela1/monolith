namespace AutoPartsErp.SharedKernel.Primitives;

/// <summary>
/// Base class for every persistent object that has a stable identity.
/// Two entities are equal when they are of the same type and carry the same identifier,
/// regardless of the state of their other properties.
/// </summary>
/// <typeparam name="TId">The identifier type. Use a strongly typed id, never a bare <see cref="Guid"/>.</typeparam>
public abstract class Entity<TId> : IEquatable<Entity<TId>>
    where TId : notnull
{
    /// <summary>Initializes a new entity with the supplied identity.</summary>
    protected Entity(TId id)
    {
        Id = id;
    }

    /// <summary>Required by EF Core materialization. Never call this from domain code.</summary>
#pragma warning disable CS8618 // EF Core sets Id during materialization.
    protected Entity()
    {
    }
#pragma warning restore CS8618

    /// <summary>The entity's identity. Immutable for the lifetime of the object.</summary>
    public TId Id { get; protected init; }

    /// <inheritdoc />
    public bool Equals(Entity<TId>? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return GetType() == other.GetType() && EqualityComparer<TId>.Default.Equals(Id, other.Id);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Entity<TId> other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(GetType(), Id);

    /// <summary>Identity equality.</summary>
    public static bool operator ==(Entity<TId>? left, Entity<TId>? right) =>
        left?.Equals(right) ?? right is null;

    /// <summary>Identity inequality.</summary>
    public static bool operator !=(Entity<TId>? left, Entity<TId>? right) => !(left == right);
}
