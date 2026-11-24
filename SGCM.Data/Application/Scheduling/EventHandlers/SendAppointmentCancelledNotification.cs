using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Notifications;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Scheduling.Notifications;
using SGCM.Data.Domain.Scheduling.Appointments.Events;

namespace SGCM.Data.Application.Scheduling.EventHandlers;

/// <summary>
/// Avisa a la otra parte de la cita cancelada. Se ejecuta después del commit, así que si la cancelación se
/// revierte no se envía nada. El motivo sale de la cita ya guardada; los recordatorios los anula la base
/// (trigger T7).
/// </summary>
internal sealed class SendAppointmentCancelledNotification(
    IAppointmentNotificationReader reader,
    INotificationService notificationService) : IDomainEventHandler<AppointmentCancelledEvent>
{
    public async Task HandleAsync(AppointmentCancelledEvent domainEvent, CancellationToken cancellationToken)
    {
        var appointment = await reader.GetAsync(domainEvent.Appointment.Id, cancellationToken);

        // Solo falta si la cita se borró entre el commit y la lectura; no hay a quién avisar.
        if (appointment is null) return;

        var notification = AppointmentCancelledNotification.Create(
            appointment, domainEvent.CancelledBy, domainEvent.Appointment.StatusReason);

        await notificationService.SendAsync(notification, cancellationToken);
    }
}
