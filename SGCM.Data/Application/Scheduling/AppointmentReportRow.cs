using SGCM.Data.Domain.Scheduling.Appointments;

namespace SGCM.Data.Application.Scheduling;

/// <summary>Fila de un reporte de citas como la ve el médico, en hora local y sin identificadores internos.</summary>
/// <param name="Date">Fecha local de la cita.</param>
/// <param name="StartTime">Hora local de inicio.</param>
/// <param name="PatientName">Nombre completo del paciente.</param>
/// <param name="SpecialtyName">Especialidad por la que se atiende la cita.</param>
/// <param name="Status">Estado de la cita.</param>
/// <param name="ReasonForVisit">Motivo de consulta, si el paciente lo indicó.</param>
public sealed record AppointmentReportRow(
    DateOnly Date,
    TimeOnly StartTime,
    string PatientName,
    string SpecialtyName,
    AppointmentStatus Status,
    string? ReasonForVisit);
