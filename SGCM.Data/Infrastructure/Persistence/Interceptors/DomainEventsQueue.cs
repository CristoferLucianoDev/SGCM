using SGCM.Data.Domain.Common.Interfaces;

namespace SGCM.Data.Infrastructure.Persistence.Interceptors;

// Eventos guardados pero aún no publicados, por petición (scoped).
internal sealed class DomainEventsQueue
{
    private readonly List<IDomainEvent> _pending = [];

    public void Enqueue(IEnumerable<IDomainEvent> domainEvents) => _pending.AddRange(domainEvents);

    public IReadOnlyList<IDomainEvent> DequeueAll()
    {
        var domainEvents = _pending.ToList();
        _pending.Clear();
        return domainEvents;
    }

    public void Clear() => _pending.Clear();
}