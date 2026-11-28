using SGCM.Data.Application.Messaging.Commands.ScheduleReminder;
using SGCM.Data.Application.Messaging.Queries.GetReminderPreview;
using SGCM.Web.Contracts.Messaging;

namespace SGCM.Web.Mappers.Messaging;

/// <summary>Convierte los contratos de la API de recordatorios en comandos, y sus resultados en respuestas.</summary>
public static class ReminderMapper
{
    /// <exception cref="ArgumentNullException">Si <paramref name="preview"/> es nulo.</exception>
    public static ReminderPreviewResponse ToResponse(this ReminderPreviewDto preview)
    {
        ArgumentNullException.ThrowIfNull(preview);

        return new ReminderPreviewResponse(preview.TemplateId, preview.Subject, preview.Message);
    }

    /// <exception cref="ArgumentNullException">Si <paramref name="request"/> es nulo.</exception>
    public static ScheduleReminderCommand ToCommand(this ScheduleReminderRequest request, int appointmentId)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new ScheduleReminderCommand(appointmentId, request.TemplateId, request.Message, request.ScheduledAt);
    }

    /// <exception cref="ArgumentNullException">Si <paramref name="reminder"/> es nulo.</exception>
    public static ScheduledReminderResponse ToResponse(this ScheduledReminderDto reminder)
    {
        ArgumentNullException.ThrowIfNull(reminder);

        return new ScheduledReminderResponse(
            reminder.Id, reminder.AppointmentId, reminder.TemplateId, reminder.Message, reminder.ScheduledAt);
    }
}
