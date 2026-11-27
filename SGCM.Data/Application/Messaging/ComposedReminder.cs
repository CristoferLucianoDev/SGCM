namespace SGCM.Data.Application.Messaging;

/// <summary>Asunto y texto de un recordatorio con las variables ya reemplazadas por los datos de la cita.</summary>
public sealed record ComposedReminder(string Subject, string Message);
