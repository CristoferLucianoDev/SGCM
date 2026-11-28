namespace SGCM.Web.Contracts.Messaging;

/// <summary>Datos de una plantilla propia del médico, para crearla o editarla.</summary>
/// <param name="Name">Nombre, hasta 100 caracteres.</param>
/// <param name="Subject">Asunto, hasta 200 caracteres.</param>
/// <param name="BodyTemplate">
/// Cuerpo, hasta 2000 caracteres. El asunto y el cuerpo solo admiten {PatientName}, {DoctorName}, {AppointmentDate},
/// {StartTime} y {SpecialtyName}.
/// </param>
public sealed record ReminderTemplateRequest(string? Name = null, string? Subject = null, string? BodyTemplate = null);
