namespace SGCM.Data.Application.Messaging.Commands.ScheduleReminder;

/// <summary>Recordatorio recién programado, pendiente de envío.</summary>
/// <param name="Id">Id del recordatorio.</param>
/// <param name="AppointmentId">Cita del recordatorio.</param>
/// <param name="TemplateId">Plantilla de referencia; <c>null</c> si es un mensaje libre.</param>
/// <param name="Message">Texto final guardado.</param>
/// <param name="ScheduledAt">Hora de envío, en UTC.</param>
public sealed record ScheduledReminderDto(int Id, int AppointmentId, int? TemplateId, string Message, DateTimeOffset ScheduledAt);
