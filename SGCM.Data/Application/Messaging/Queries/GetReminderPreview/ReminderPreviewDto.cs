namespace SGCM.Data.Application.Messaging.Queries.GetReminderPreview;

/// <summary>Asunto y texto de la plantilla con las variables ya reemplazadas por los datos de la cita.</summary>
/// <param name="TemplateId">Plantilla usada, para enviarla junto con el texto al programar.</param>
/// <param name="Subject">Asunto completado.</param>
/// <param name="Message">Texto completado, sin codificar como HTML.</param>
public sealed record ReminderPreviewDto(int TemplateId, string Subject, string Message);
