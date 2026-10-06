using SGCM.Data.Domain.Common.Interfaces;

namespace SGCM.Data.Domain.Common;

/// <summary>
/// Raíz de agregado: la única entidad del agregado que puede modificarse desde fuera
/// y la responsable de mantener sus invariantes. Acumula los eventos de dominio que
/// produce para que la capa de infraestructura los despache después de persistir.
/// </summary>
public abstract class AggregateRoot : Entity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    /// <summary>
    /// Eventos de dominio producidos desde la última llamada a <see cref="ClearDomainEvents"/>.
    /// </summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    /// <summary>
    /// Registra un evento de dominio para despacharlo más adelante.
    /// </summary>
    /// <param name="domainEvent">Evento que describe lo que ocurrió en el agregado.</param>
    protected void Raise(IDomainEvent domainEvent)
    {
        _domainEvents.Add(domainEvent);
    }

    /// <summary>
    /// Descarta los eventos acumulados. Se llama una vez que fueron despachados.
    /// </summary>
    public void ClearDomainEvents()
    {
        _domainEvents.Clear();
    }
}
