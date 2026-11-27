using SGCM.Web.Contracts.People;

namespace SGCM.Web.Contracts.Scheduling;

/// <summary>
/// Detalle de una cita del paciente: los datos del historial, los motivos, lo que el paciente puede hacer con ella y
/// el perfil público del médico.
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
/// <param name="DoctorProfile">
/// Perfil público del médico, el mismo de <c>GET /api/doctors/{id}</c>; <c>null</c> si el médico está desactivado, y
/// entonces solo se muestran su nombre y la especialidad, que vienen en <paramref name="Appointment"/>.
/// </param>
public sealed record PatientAppointmentDetailResponse(
    PatientAppointmentResponse Appointment,
    string? ReasonForVisit,
    string? StatusReason,
    IReadOnlyList<string> AllowedActions,
    DateTimeOffset? ConfirmationDeadline,
    int? MinutesUntilConfirmationDeadline,
    DoctorPublicProfileResponse? DoctorProfile);
