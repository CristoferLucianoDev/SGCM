using SGCM.Data.Application.Abstractions.Messaging;

namespace SGCM.Data.Application.Messaging.Queries.GetReminderPreview;

/// <summary>
/// Vista previa de un recordatorio: la plantilla elegida completada con los datos reales de una cita del médico
/// autenticado, para que la edite antes de programarlo.
/// </summary>
/// <param name="AppointmentId">Cita del recordatorio.</param>
/// <param name="TemplateId">Plantilla elegida: global o del médico, y activa.</param>
public sealed record GetReminderPreviewQuery(int AppointmentId, int TemplateId) : IQuery<ReminderPreviewDto>;
