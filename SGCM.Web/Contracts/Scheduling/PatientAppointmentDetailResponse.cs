namespace SGCM.Web.Contracts.Scheduling;

/// <summary>
/// Detalle de una cita del paciente: los datos del historial, los motivos y lo que el paciente puede hacer con ella.
/// </summary>
/// <param name="Appointment">Datos de la cita, los mismos que muestra el historial.</param>
/// <param name="ReasonForVisit">Motivo de consulta; <c>null</c> si el paciente no escribió ninguno.</param>
/// <param name="StatusReason">
/// Motivo de cancelación o rechazo, solo en citas canceladas o rechazadas; <c>null</c> en los demás estados o si
/// no se indicó.
/// </param>
/// <param name="AllowedActions">
/// Acciones disponibles para el paciente, en camelCase: cancel, reschedule, changeDoctor, confirm, reject, extend o
/// complete.
/// </param>
/// <param name="ConfirmationDeadline">Límite para que el médico confirme; <c>null</c> si la cita no está pendiente.</param>
/// <param name="MinutesUntilConfirmationDeadline">
/// Minutos completos que faltan para ese límite, sin bajar de cero; <c>null</c> si la cita no está pendiente.
/// </param>
public sealed record PatientAppointmentDetailResponse(
    PatientAppointmentResponse Appointment,
    string? ReasonForVisit,
    string? StatusReason,
    IReadOnlyList<string> AllowedActions,
    DateTimeOffset? ConfirmationDeadline,
    int? MinutesUntilConfirmationDeadline);
