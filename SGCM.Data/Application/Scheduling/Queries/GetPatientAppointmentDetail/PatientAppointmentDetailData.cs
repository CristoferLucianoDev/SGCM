using SGCM.Data.Application.People.Queries.GetDoctorProfile;
using SGCM.Data.Application.Scheduling.Queries.GetPatientAppointmentHistory;

namespace SGCM.Data.Application.Scheduling.Queries.GetPatientAppointmentDetail;

/// <summary>
/// Lo que lee el lector de una cita del paciente: sus datos y sus motivos, todavía sin las acciones permitidas,
/// que el handler calcula con el agregado. Existe para que no circule un <see cref="PatientAppointmentDetailDto"/>
/// a medio construir.
/// </summary>
/// <param name="Appointment">Datos de la cita, los mismos que muestra el historial.</param>
/// <param name="ReasonForVisit">Motivo de consulta; <c>null</c> si el paciente no escribió ninguno.</param>
/// <param name="StatusReason">Motivo de cancelación o rechazo, solo en citas canceladas o rechazadas.</param>
/// <param name="DoctorProfile">
/// Perfil público del médico; <c>null</c> si el médico está desactivado, y entonces solo se muestran su nombre y la
/// especialidad de la cita, que vienen en <paramref name="Appointment"/>.
/// </param>
public sealed record PatientAppointmentDetailData(
    PatientAppointmentDto Appointment,
    string? ReasonForVisit,
    string? StatusReason,
    DoctorPublicProfileDto? DoctorProfile);
