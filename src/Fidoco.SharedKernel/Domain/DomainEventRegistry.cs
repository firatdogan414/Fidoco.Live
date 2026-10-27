using Microsoft.Extensions.DependencyInjection;

namespace Fidoco.SharedKernel.Domain;

/// <summary>One handler subscribed to one event type. The handler name is stored on outbox rows.</summary>
public sealed class DomainEventHandlerRegistration(
    Type eventType,
    Type handlerType,
    Func<IServiceProvider, IDomainEvent, CancellationToken, Task> invoke)
{
    public Type EventType { get; } = eventType;

    public Type HandlerType { get; } = handlerType;

    public string Name => HandlerType.FullName!;

    public Task InvokeAsync(IServiceProvider services, IDomainEvent domainEvent, CancellationToken cancellationToken) =>
        invoke(services, domainEvent, cancellationToken);
}

/// <summary>All handler registrations, built from DI at startup.</summary>
public sealed class DomainEventRegistry
{
    private readonly Dictionary<Type, List<DomainEventHandlerRegistration>> _byEvent;
    private readonly Dictionary<string, DomainEventHandlerRegistration> _byName;

    public DomainEventRegistry(IEnumerable<DomainEventHandlerRegistration> registrations)
    {
        var list = registrations.ToList();
        _byEvent = list.GroupBy(r => r.EventType).ToDictionary(g => g.Key, g => g.ToList());
        _byName = list.ToDictionary(r => r.Name);
    }

    public IReadOnlyList<DomainEventHandlerRegistration> HandlersFor(Type eventType) =>
        _byEvent.TryGetValue(eventType, out var handlers) ? handlers : [];

    public DomainEventHandlerRegistration? FindHandler(string name) => _byName.GetValueOrDefault(name);
}

public static class DomainEventServiceCollectionExtensions
{
    /// <summary>Subscribes <typeparamref name="THandler"/> to <typeparamref name="TEvent"/> via the outbox.</summary>
    public static IServiceCollection AddDomainEventHandler<TEvent, THandler>(this IServiceCollection services)
        where TEvent : IDomainEvent
        where THandler : class, IDomainEventHandler<TEvent>
    {
        services.AddScoped<THandler>();
        services.AddSingleton(new DomainEventHandlerRegistration(
            typeof(TEvent),
            typeof(THandler),
            (sp, e, ct) => sp.GetRequiredService<THandler>().HandleAsync((TEvent)e, ct)));
        return services;
    }
}
