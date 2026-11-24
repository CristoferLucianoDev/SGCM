namespace SGCM.Web.Contracts.Scheduling;

/// <summary>Cancelación de una cita propia del paciente autenticado.</summary>
/// <param name="Reason">Motivo; opcional, hasta 500 caracteres.</param>
public sealed record CancelAppointmentRequest(string? Reason = null);
