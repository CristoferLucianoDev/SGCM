using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Domain.Common.Interfaces;
using Serilog;

namespace SGCM.Data.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Publica los eventos de dominio a sus manejadores después de confirmar la transacción que los originó. Cada manejador
/// se aísla: si uno falla, el fallo se registra en el log, los cambios que dejó pendientes se descartan para que el
/// siguiente no los guarde, y los demás siguen. El cambio que originó el evento ya está confirmado y no se revierte.
/// </summary>
internal sealed class DomainEventDispatcher(IServiceProvider services, ILogger logger)
{
    private readonly ILogger _logger = logger.ForContext<DomainEventDispatcher>();

    public async Task DispatchAsync(IReadOnlyList<IDomainEvent> domainEvents)
    {
        foreach (var domainEvent in domainEvents)
        {
            var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(domainEvent.GetType());
            var handleMethod = handlerType.GetMethod(nameof(IDomainEventHandler<IDomainEvent>.HandleAsync))!;

            foreach (var handler in services.GetServices(handlerType))
            {
                try
                {
                    await (Task)handleMethod.Invoke(handler, [domainEvent, CancellationToken.None])!;
                }
                catch (Exception exception)
                {
                    // Los datos ya están confirmados: un efecto secundario fallido se registra y no se propaga. Si el
                    // manejador falla antes de su primer await, la reflexión envuelve la excepción; se registra la real.
                    _logger.Error(
                        exception is TargetInvocationException { InnerException: { } inner } ? inner : exception,
                        "Falló {Handler} al procesar {Event}",
                        handler!.GetType().Name,
                        domainEvent.GetType().Name);
                    DiscardPendingChanges();
                }
            }
        }
    }

    // El contexto es el mismo del caso de uso (scoped). Tras el commit no queda nada pendiente salvo lo que dejó el
    // manejador que falló; se descarta para que el siguiente manejador que guarde no lo confirme por error. Se resuelve
    // aquí y no en el constructor porque el contexto depende del interceptor que usa este despachador.
    private void DiscardPendingChanges()
    {
        var context = services.GetService<AppDbContext>();
        if (context is null) return;

        foreach (var entry in context.ChangeTracker.Entries().ToList())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.State = EntityState.Detached;
                    break;
                case EntityState.Modified:
                case EntityState.Deleted:
                    entry.CurrentValues.SetValues(entry.OriginalValues);
                    entry.State = EntityState.Unchanged;
                    break;
            }
        }
    }
}
