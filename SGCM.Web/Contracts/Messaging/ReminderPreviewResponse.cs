namespace SGCM.Web.Contracts.Messaging;

/// <summary>Plantilla completada con los datos de la cita, lista para que el médico la edite antes de programarla.</summary>
/// <param name="TemplateId">Plantilla usada; se envía al programar el recordatorio.</param>
/// <param name="Subject">Asunto con las variables reemplazadas.</param>
/// <param name="Message">Texto con las variables reemplazadas, sin codificar como HTML.</param>
public sealed record ReminderPreviewResponse(int TemplateId, string Subject, string Message);
