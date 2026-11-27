using SGCM.Data.Domain.Scheduling.Appointments;

namespace SGCM.Data.Application.Scheduling;

/// <summary>Fila de un reporte tal como se lee, con el inicio en UTC y sin identificadores.</summary>
/// <param name="StartsAt">Inicio de la cita en UTC.</param>
/// <param name="PatientFirstName">Nombre del paciente.</param>
/// <param name="PatientLastName">Apellido del paciente.</param>
/// <param name="SpecialtyName">Especialidad por la que se atiende la cita.</param>
/// <param name="Status">Estado de la cita.</param>
/// <param name="ReasonForVisit">Motivo de consulta, si el paciente lo indicó.</param>
public sealed record AppointmentReportRowData(
    DateTimeOffset StartsAt,
    string PatientFirstName,
    string PatientLastName,
    string SpecialtyName,
    AppointmentStatus Status,
    string? ReasonForVisit);
