using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Notifications;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Scheduling.Notifications;
using SGCM.Data.Domain.Scheduling.Appointments.Events;

namespace SGCM.Data.Application.Scheduling.EventHandlers;

/// <summary>
/// Avisa al paciente de que el médico confirmó su cita. Se ejecuta después del commit, así que si la confirmación
/// se revierte no se envía nada; el médico no recibe correo.
/// </summary>
internal sealed class SendAppointmentConfirmedNotification(
    IAppointmentNotificationReader reader,
    INotificationService notificationService) : IDomainEventHandler<AppointmentConfirmedEvent>
{
    public async Task HandleAsync(AppointmentConfirmedEvent domainEvent, CancellationToken cancellationToken)
    {
        var appointment = await reader.GetAsync(domainEvent.Appointment.Id, cancellationToken);

        // Solo falta si la cita se borró entre el commit y la lectura; no hay a quién avisar.
        if (appointment is null) return;

        var notification = new AppointmentConfirmedNotification(
            new Recipient(appointment.PatientEmail, appointment.PatientName), appointment);

        await notificationService.SendAsync(notification, cancellationToken);
    }
}
