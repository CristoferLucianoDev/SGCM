using Microsoft.Extensions.Options;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Notifications;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Scheduling.Notifications;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Domain.Scheduling.Appointments.Events;

namespace SGCM.Data.Application.Scheduling.EventHandlers;

/// <summary>
/// Avisa al paciente y al médico de que la solicitud venció sin confirmarse. Se ejecuta después del commit, así que
/// si el vencimiento se revierte no se envía nada. Los recordatorios pendientes los anula la base al pasar la cita
/// a cancelada.
/// </summary>
internal sealed class SendAppointmentExpiredNotification(
    IAppointmentNotificationReader reader,
    INotificationService notificationService,
    IOptions<SchedulingOptions> schedulingOptions) : IDomainEventHandler<AppointmentExpiredEvent>
{
    public async Task HandleAsync(AppointmentExpiredEvent domainEvent, CancellationToken cancellationToken)
    {
        var appointment = await reader.GetAsync(domainEvent.Appointment.Id, cancellationToken);

        // Solo falta si la cita se borró entre el commit y la lectura; no hay a quién avisar.
        if (appointment is null) return;

        var hours = schedulingOptions.Value.ConfirmationDeadlineHours;

        // El segundo aviso se envía aunque el primero falle: NotificationService ya registra cada fallo.
        await SendAsync(appointment, AppointmentParticipant.Patient, hours, cancellationToken);
        await SendAsync(appointment, AppointmentParticipant.Doctor, hours, cancellationToken);
    }

    private Task SendAsync(
        AppointmentNotificationData appointment,
        AppointmentParticipant recipientRole,
        int hours,
        CancellationToken cancellationToken) =>
        notificationService.SendAsync(
            AppointmentExpiredNotification.Create(appointment, recipientRole, hours), cancellationToken);
}
