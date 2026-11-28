namespace SGCM.Web.Contracts.Messaging;

/// <summary>Recordatorio recién programado, pendiente de envío.</summary>
/// <param name="Id">Id del recordatorio.</param>
/// <param name="AppointmentId">Cita del recordatorio.</param>
/// <param name="TemplateId">Plantilla de referencia, o <c>null</c> si es un mensaje libre.</param>
/// <param name="Message">Texto final guardado; no cambia aunque la plantilla cambie después.</param>
/// <param name="ScheduledAt">Fecha y hora de envío, en UTC.</param>
public sealed record ScheduledReminderResponse(
    int Id,
    int AppointmentId,
    int? TemplateId,
    string Message,
    DateTimeOffset ScheduledAt);
