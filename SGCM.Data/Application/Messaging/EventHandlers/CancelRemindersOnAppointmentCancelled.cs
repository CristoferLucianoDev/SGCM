using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Domain.Scheduling.Appointments.Events;

namespace SGCM.Data.Application.Messaging.EventHandlers;

/// <summary>
/// Al cancelarse una cita se anulan sus recordatorios pendientes. La base hace lo mismo (trigger T7); esto deja la
/// regla también en la aplicación y no cambia nada si el trigger ya los anuló.
/// </summary>
internal sealed class CancelRemindersOnAppointmentCancelled(AutomaticReminderScheduler scheduler)
    : IDomainEventHandler<AppointmentCancelledEvent>
{
    public Task HandleAsync(AppointmentCancelledEvent domainEvent, CancellationToken cancellationToken) =>
        scheduler.CancelAllAsync(domainEvent.Appointment.Id, cancellationToken);
}
