using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Domain.Scheduling.Appointments.Events;

namespace SGCM.Data.Application.Messaging.EventHandlers;

/// <summary>Al confirmarse una cita se programa su recordatorio automático.</summary>
internal sealed class ScheduleReminderOnAppointmentConfirmed(AutomaticReminderScheduler scheduler)
    : IDomainEventHandler<AppointmentConfirmedEvent>
{
    public Task HandleAsync(AppointmentConfirmedEvent domainEvent, CancellationToken cancellationToken) =>
        scheduler.ScheduleAsync(domainEvent.Appointment, cancellationToken);
}
