namespace SGCM.Web.Contracts.Messaging;

/// <summary>Plantilla de recordatorio tal como la ve el médico.</summary>
/// <param name="Id">Id de la plantilla.</param>
/// <param name="Name">Nombre.</param>
/// <param name="Subject">Asunto, con variables.</param>
/// <param name="BodyTemplate">Cuerpo, con variables.</param>
/// <param name="IsActive">Si está activa.</param>
/// <param name="IsReadOnly"><c>true</c> si es global: se puede usar, pero no editar ni desactivar.</param>
public sealed record ReminderTemplateResponse(
    int Id,
    string Name,
    string Subject,
    string BodyTemplate,
    bool IsActive,
    bool IsReadOnly);
