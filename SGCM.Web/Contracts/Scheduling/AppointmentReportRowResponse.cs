namespace SGCM.Web.Contracts.Scheduling;

/// <summary>Fila del reporte de citas, en hora local y sin identificadores internos.</summary>
/// <param name="Date">Fecha local de la cita.</param>
/// <param name="StartTime">Hora local de inicio.</param>
/// <param name="PatientName">Nombre completo del paciente.</param>
/// <param name="SpecialtyName">Especialidad de la cita.</param>
/// <param name="Status">Estado en minúsculas.</param>
/// <param name="ReasonForVisit">Motivo de consulta, o <c>null</c> si no se indicó.</param>
public sealed record AppointmentReportRowResponse(
    DateOnly Date,
    TimeOnly StartTime,
    string PatientName,
    string SpecialtyName,
    string Status,
    string? ReasonForVisit);
