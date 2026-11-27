using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Domain.Scheduling.Appointments.Events;

namespace SGCM.Data.Application.Messaging.EventHandlers;

/// <summary>
/// Al reprogramarse una cita se anula su recordatorio automático pendiente, que tenía la hora y el texto anteriores.
/// Si queda confirmada, porque la reprogramó el médico, se programa uno nuevo; si vuelve a pendiente, se programa de
/// nuevo cuando el médico la confirme.
/// </summary>
internal sealed class RecalculateReminderOnAppointmentRescheduled(AutomaticReminderScheduler scheduler)
    : IDomainEventHandler<AppointmentRescheduledEvent>
{
    public async Task HandleAsync(AppointmentRescheduledEvent domainEvent, CancellationToken cancellationToken)
    {
        await scheduler.CancelAutomaticAsync(domainEvent.Appointment.Id, cancellationToken);

        if (domainEvent.Appointment.Status == AppointmentStatus.Confirmed)
            await scheduler.ScheduleAsync(domainEvent.Appointment, cancellationToken);
    }
}
