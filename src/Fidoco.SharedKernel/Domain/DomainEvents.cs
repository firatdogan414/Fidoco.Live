namespace Fidoco.SharedKernel.Domain;

/// <summary>A fact that happened in a module. Serialized to the outbox, so keep it a plain record.</summary>
public interface IDomainEvent;

public interface IHasDomainEvents
{
    IReadOnlyList<IDomainEvent> DomainEvents { get; }

    void ClearDomainEvents();
}

/// <summary>Base class for entities that raise domain events.</summary>
public abstract class AggregateRoot : IHasDomainEvents
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents;

    public void ClearDomainEvents() => _domainEvents.Clear();

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
}

/// <summary>
/// Handles a domain event, possibly from another module. Delivery is at-least-once, so handlers must be
/// idempotent.
/// </summary>
public interface IDomainEventHandler<in TEvent>
    where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken);
}
