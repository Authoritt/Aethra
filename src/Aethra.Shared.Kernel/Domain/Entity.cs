namespace Aethra.Shared.Kernel.Domain;

/// <summary>
/// Identidad por <typeparamref name="TId"/>. Igualdad por identidad, no por contenido.
/// </summary>
public abstract class Entity<TId> : IEquatable<Entity<TId>>
    where TId : notnull
{
    public TId Id { get; protected set; } = default!;

    protected Entity(TId id)
    {
        Id = id;
    }

    protected Entity() { }

    public bool Equals(Entity<TId>? other)
        => other is not null && EqualityComparer<TId>.Default.Equals(Id, other.Id);

    public override bool Equals(object? obj)
        => obj is Entity<TId> other && Equals(other);

    public override int GetHashCode() => Id.GetHashCode();

    public static bool operator ==(Entity<TId>? a, Entity<TId>? b)
        => a is null ? b is null : a.Equals(b);

    public static bool operator !=(Entity<TId>? a, Entity<TId>? b) => !(a == b);
}
