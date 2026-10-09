using SGCM.Data.Domain.Common.Interfaces;

namespace SGCM.Data.Application.Abstractions.Messaging;

// Cada efecto secundario de un evento (enviar un correo, por ejemplo) es una clase que implementa esta interfaz.
public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken);
}