namespace SGCM.Web.Contracts.Messaging;

/// <summary>Recordatorio que el médico programa para una cita propia confirmada o completada.</summary>
/// <param name="Message">Texto final que recibe el paciente, hasta 2000 caracteres.</param>
/// <param name="ScheduledAt">Fecha y hora de envío con su desfase; debe ser futura.</param>
/// <param name="TemplateId">Plantilla de la vista previa, como referencia; se omite si es un mensaje libre.</param>
public sealed record ScheduleReminderRequest(
    string? Message = null,
    DateTimeOffset? ScheduledAt = null,
    int? TemplateId = null);
