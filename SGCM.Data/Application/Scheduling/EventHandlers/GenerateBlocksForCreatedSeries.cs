using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Domain.Scheduling.Availability.Events;

namespace SGCM.Data.Application.Scheduling.EventHandlers;

/// <summary>
/// Genera los bloques de la serie recién creada. Corre después de confirmar la creación, así que la serie
/// ya tiene su Id; si falla, la serie queda creada y el job diario completa sus bloques.
/// </summary>
internal sealed class GenerateBlocksForCreatedSeries(AvailabilityBlockGenerator generator, IUnitOfWork unitOfWork)
    : IDomainEventHandler<DoctorAvailabilitySeriesCreatedEvent>
{
    public async Task HandleAsync(DoctorAvailabilitySeriesCreatedEvent domainEvent, CancellationToken cancellationToken)
    {
        var generated = await generator.GenerateAsync(domainEvent.Series, cancellationToken);
        if (generated > 0) await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
