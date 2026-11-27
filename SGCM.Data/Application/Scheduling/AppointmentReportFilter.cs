using SGCM.Data.Domain.Scheduling.Appointments;

namespace SGCM.Data.Application.Scheduling;

/// <summary>
/// Filtro ya resuelto de un reporte de citas, listo para la lectura: el médico, los estados efectivos y el rango
/// en UTC.
/// </summary>
/// <param name="DoctorId">Médico dueño de las citas.</param>
/// <param name="Statuses">Estados que se incluyen; sin estados el reporte queda vacío.</param>
/// <param name="FromUtc">Inicio del rango en UTC, incluido; se compara con el inicio de la cita.</param>
/// <param name="ToUtcExclusive">Fin del rango en UTC, excluido; se compara con el inicio de la cita.</param>
/// <param name="PatientName">Texto a buscar en el nombre completo del paciente, o <see langword="null"/> para no filtrar.</param>
/// <param name="SpecialtyId">Especialidad de la cita, o <see langword="null"/> para no filtrar.</param>
public sealed record AppointmentReportFilter(
    int DoctorId,
    IReadOnlyCollection<AppointmentStatus> Statuses,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtcExclusive,
    string? PatientName,
    int? SpecialtyId);
