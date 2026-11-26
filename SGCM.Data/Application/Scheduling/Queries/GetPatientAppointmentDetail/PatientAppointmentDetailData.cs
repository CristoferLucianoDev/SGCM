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
public sealed record PatientAppointmentDetailData(
    PatientAppointmentDto Appointment,
    string? ReasonForVisit,
    string? StatusReason);
