namespace SGCM.Web.Contracts.Scheduling;

/// <summary>
/// Detalle de una cita del paciente: los datos del historial más los motivos.
/// </summary>
/// <param name="Appointment">Datos de la cita, los mismos que muestra el historial.</param>
/// <param name="ReasonForVisit">Motivo de consulta; <c>null</c> si el paciente no escribió ninguno.</param>
/// <param name="StatusReason">
/// Motivo de cancelación o rechazo, solo en citas canceladas o rechazadas; <c>null</c> en los demás estados o si
/// no se indicó.
/// </param>
public sealed record PatientAppointmentDetailResponse(
    PatientAppointmentResponse Appointment,
    string? ReasonForVisit,
    string? StatusReason);
