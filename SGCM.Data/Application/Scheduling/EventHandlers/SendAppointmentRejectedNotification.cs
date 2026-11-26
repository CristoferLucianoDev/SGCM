using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Notifications;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Scheduling.Notifications;
using SGCM.Data.Domain.Scheduling.Appointments.Events;

namespace SGCM.Data.Application.Scheduling.EventHandlers;

/// <summary>
/// Avisa al paciente de que el médico rechazó su solicitud de cita, con el motivo. Se ejecuta después del commit, así
/// que si el rechazo se revierte no se envía nada; el médico no recibe correo.
/// </summary>
internal sealed class SendAppointmentRejectedNotification(
    IAppointmentNotificationReader reader,
    INotificationService notificationService) : IDomainEventHandler<AppointmentRejectedEvent>
{
    public async Task HandleAsync(AppointmentRejectedEvent domainEvent, CancellationToken cancellationToken)
    {
        var appointment = await reader.GetAsync(domainEvent.Appointment.Id, cancellationToken);

        // Solo falta si la cita se borró entre el commit y la lectura; no hay a quién avisar.
        if (appointment is null) return;

        var notification = new AppointmentRejectedNotification(
            new Recipient(appointment.PatientEmail, appointment.PatientName),
            appointment,
            domainEvent.Appointment.StatusReason);

        await notificationService.SendAsync(notification, cancellationToken);
    }
}
