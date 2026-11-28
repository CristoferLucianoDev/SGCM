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
}
