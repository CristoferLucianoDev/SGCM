using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SGCM.Data.Domain.Common;

namespace SGCM.Data.Infrastructure.Persistence.Interceptors;

// Recoge los eventos de los agregados después de guardar. Sin transacción los publica de inmediato;
// con transacción los deja en la cola para que se publiquen al confirmar.
internal sealed class DomainEventsInterceptor(DomainEventsQueue queue, DomainEventDispatcher dispatcher)
    : SaveChangesInterceptor
{
    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        var context = eventData.Context;
        if (context is not null)
        {
            var aggregates = context.ChangeTracker.Entries<AggregateRoot>()
                .Select(entry => entry.Entity)
                .Where(aggregate => aggregate.DomainEvents.Count > 0)
                .ToList();

            foreach (var aggregate in aggregates)
            {
                queue.Enqueue(aggregate.DomainEvents.ToList());
                aggregate.ClearDomainEvents();
            }

            if (context.Database.CurrentTransaction is null)
            {
                await dispatcher.DispatchAsync(queue.DequeueAll());
            }
        }

        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }
}