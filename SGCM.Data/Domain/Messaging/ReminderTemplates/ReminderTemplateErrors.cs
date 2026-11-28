using SGCM.Data.Domain.Common;

namespace SGCM.Data.Domain.Messaging.ReminderTemplates;

public static class ReminderTemplateErrors
{
    /// <summary>La plantilla no existe, está inactiva o es privada de otro médico; los tres casos responden igual.</summary>
    public static readonly Error NotFound = Error.NotFound(
        "ReminderTemplate.NotFound", "La plantilla de recordatorio no existe.");
}
