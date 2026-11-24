namespace SGCM.Web.Contracts.Scheduling;

/// <summary>Cancelación de una cita propia del médico autenticado.</summary>
/// <param name="Reason">Motivo obligatorio, hasta 500 caracteres; con poco aviso exige un mínimo de palabras.</param>
public sealed record CancelAppointmentByDoctorRequest(string? Reason = null);
