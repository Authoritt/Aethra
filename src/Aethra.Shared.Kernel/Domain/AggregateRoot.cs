namespace Aethra.Shared.Kernel.Domain;

/// <summary>
/// Raíz de un agregado. Acumula eventos de dominio que se publican al hacer SaveChanges.
/// </summary>
public abstract class AggregateRoot<TId> : Entity<TId>
    where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected AggregateRoot(TId id) : base(id) { }
    protected AggregateRoot() { }

    protected void Raise(IDomainEvent @event) => _domainEvents.Add(@event);

    public void ClearDomainEvents() => _domainEvents.Clear();
}
