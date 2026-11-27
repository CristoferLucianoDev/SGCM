using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Notifications;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Application.Messaging.Notifications;
using SGCM.Data.Application.Scheduling.Notifications;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Messaging.Reminders;
using SGCM.Data.Domain.Messaging.ReminderTemplates;

namespace SGCM.Data.Application.Messaging.Commands.SendDueReminders;

/// <summary>
/// Toma los recordatorios pendientes cuya hora llegó, los envía como <see cref="ReminderNotification"/> al paciente y
/// los marca como enviados o fallidos; un fallo no corta el lote ni se reintenta. Es idempotente por el estado: solo
/// toma pendientes, así que una segunda ejecución no los reenvía. El asunto sale de la plantilla con los datos de la
/// cita, o es uno fijo si el médico escribió un mensaje libre.
/// </summary>
internal sealed class SendDueRemindersCommandHandler(
    IReminderRepository reminders,
    IReminderTemplateRepository templates,
    IAppointmentNotificationReader appointmentReader,
    ReminderComposer composer,
    INotificationService notificationService,
    IDateTimeProvider clock) : ICommandHandler<SendDueRemindersCommand, int>
{
    // Acota la transacción; los que queden los toma la siguiente ejecución.
    private const int BatchSize = 100;

    public async Task<Result<int>> HandleAsync(SendDueRemindersCommand command, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var due = await reminders.ListDueAsync(now, BatchSize, cancellationToken);
        var templateCache = new Dictionary<int, ReminderTemplate?>();

        var processed = 0;
        foreach (var reminder in due.Where(reminder => reminder.IsDue(now)))
        {
            var sent = await SendAsync(reminder, templateCache, cancellationToken);
            var marked = sent ? reminder.MarkSent(now) : reminder.MarkFailed();
            if (marked.IsSuccess) processed++;
        }

        // El decorador guarda los estados y confirma la transacción.
        return processed;
    }

    private async Task<bool> SendAsync(
        Reminder reminder, Dictionary<int, ReminderTemplate?> templateCache, CancellationToken cancellationToken)
    {
        // Solo falta si la cita se borró; no hay a quién enviarlo y queda registrado como fallido.
        var appointment = await appointmentReader.GetAsync(reminder.AppointmentId, cancellationToken);
        if (appointment is null) return false;

        var subject = await SubjectAsync(reminder, appointment, templateCache, cancellationToken);
        var notification = new ReminderNotification(
            new Recipient(appointment.PatientEmail, appointment.PatientName), subject, reminder.MessageBody);

        return (await notificationService.SendAsync(notification, cancellationToken)).IsSuccess;
    }

    private async Task<string> SubjectAsync(
        Reminder reminder,
        AppointmentNotificationData appointment,
        Dictionary<int, ReminderTemplate?> templateCache,
        CancellationToken cancellationToken)
    {
        if (reminder.TemplateId is { } templateId)
        {
            if (!templateCache.TryGetValue(templateId, out var template))
            {
                template = await templates.GetByIdAsync(templateId, cancellationToken);
                templateCache[templateId] = template;
            }

            if (template is not null) return composer.Compose(template, appointment).Subject;
        }

        return DefaultSubject(appointment);
    }

    private static string DefaultSubject(AppointmentNotificationData appointment) =>
        $"Recordatorio de su cita de {appointment.SpecialtyName} del " +
        AppointmentNotificationFormats.Date(appointment.AppointmentDate);
}
