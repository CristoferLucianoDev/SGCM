using SGCM.Data.Domain.Messaging.ReminderTemplates;

namespace SGCM.Data.Application.Messaging.ReminderTemplates;

/// <summary>Plantilla de recordatorio tal como la ve el médico.</summary>
/// <param name="Id">Id de la plantilla.</param>
/// <param name="Name">Nombre.</param>
/// <param name="Subject">Asunto, con variables.</param>
/// <param name="BodyTemplate">Cuerpo, con variables.</param>
/// <param name="IsActive">Si está activa.</param>
/// <param name="IsReadOnly"><c>true</c> si es global: el médico la usa, pero no la edita ni la desactiva.</param>
public sealed record ReminderTemplateDto(
    int Id,
    string Name,
    string Subject,
    string BodyTemplate,
    bool IsActive,
    bool IsReadOnly)
{
    /// <exception cref="ArgumentNullException">Si <paramref name="template"/> es nulo.</exception>
    public static ReminderTemplateDto From(ReminderTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);

        return new ReminderTemplateDto(
            template.Id, template.Name, template.Subject, template.BodyTemplate, template.IsActive, template.IsGlobal);
    }
}
