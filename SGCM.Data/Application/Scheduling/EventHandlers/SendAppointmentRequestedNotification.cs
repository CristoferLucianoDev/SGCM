using Microsoft.Extensions.Options;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Notifications;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Scheduling.Notifications;
using SGCM.Data.Domain.Scheduling.Appointments.Events;

namespace SGCM.Data.Application.Scheduling.EventHandlers;

/// <summary>
/// Envía al médico el correo de la nueva solicitud. Se ejecuta después del commit, cuando la cita ya tiene su Id,
/// así que si la solicitud se revierte no se envía nada. Al paciente no se le envía correo.
/// </summary>
internal sealed class SendAppointmentRequestedNotification(
    IAppointmentNotificationReader reader,
    INotificationService notificationService,
    IOptions<SchedulingOptions> schedulingOptions) : IDomainEventHandler<AppointmentRequestedEvent>
{
    public async Task HandleAsync(AppointmentRequestedEvent domainEvent, CancellationToken cancellationToken)
    {
        var appointment = await reader.GetAsync(domainEvent.Appointment.Id, cancellationToken);

        // Solo falta si la cita se borró entre el commit y la lectura; no hay a quién avisar.
        if (appointment is null) return;

        var notification = new AppointmentRequestedNotification(
            new Recipient(appointment.DoctorEmail, appointment.DoctorName),
            appointment,
            schedulingOptions.Value.ConfirmationDeadlineHours);

        await notificationService.SendAsync(notification, cancellationToken);
    }
}
