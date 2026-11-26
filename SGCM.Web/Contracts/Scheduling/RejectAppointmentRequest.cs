namespace SGCM.Web.Contracts.Scheduling;

/// <summary>Rechazo de una cita propia pendiente del médico autenticado.</summary>
/// <param name="Reason">Motivo obligatorio, hasta 500 caracteres.</param>
public sealed record RejectAppointmentRequest(string? Reason = null);
