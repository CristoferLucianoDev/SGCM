using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Notifications;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Scheduling.Notifications;
using SGCM.Data.Domain.Scheduling;
using SGCM.Data.Domain.Scheduling.Appointments.Events;

namespace SGCM.Data.Application.Scheduling.EventHandlers;

/// <summary>
/// Avisa a la otra parte de la cita reprogramada con la nueva fecha. Se ejecuta después del commit, así que si la
/// reprogramación se revierte no se envía nada. Los datos se leen ya con el nuevo horario; el anterior sale del
/// evento y se convierte a la hora local de la agenda. Solo avisa a la parte que no reprogramó, y la reprogramación
/// del médico lo reutiliza.
/// </summary>
internal sealed class SendAppointmentRescheduledNotification(
    IAppointmentNotificationReader reader,
    INotificationService notificationService,
    BusinessCalendar calendar) : IDomainEventHandler<AppointmentRescheduledEvent>
{
    public async Task HandleAsync(AppointmentRescheduledEvent domainEvent, CancellationToken cancellationToken)
    {
        var appointment = await reader.GetAsync(domainEvent.Appointment.Id, cancellationToken);

        // Solo falta si la cita se borró entre el commit y la lectura; no hay a quién avisar.
        if (appointment is null) return;

        var previous = calendar.ToLocal(domainEvent.PreviousStartsAt).DateTime;
        var notification = AppointmentRescheduledNotification.Create(
            appointment, domainEvent.RescheduledBy, DateOnly.FromDateTime(previous), TimeOnly.FromDateTime(previous));

        await notificationService.SendAsync(notification, cancellationToken);
    }
}
