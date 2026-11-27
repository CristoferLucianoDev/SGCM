using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Domain.Scheduling.Appointments.Events;

namespace SGCM.Data.Application.Messaging.EventHandlers;

/// <summary>Al vencer una cita sin confirmar se anulan sus recordatorios pendientes.</summary>
internal sealed class CancelRemindersOnAppointmentExpired(AutomaticReminderScheduler scheduler)
    : IDomainEventHandler<AppointmentExpiredEvent>
{
    public Task HandleAsync(AppointmentExpiredEvent domainEvent, CancellationToken cancellationToken) =>
        scheduler.CancelAllAsync(domainEvent.Appointment.Id, cancellationToken);
}
