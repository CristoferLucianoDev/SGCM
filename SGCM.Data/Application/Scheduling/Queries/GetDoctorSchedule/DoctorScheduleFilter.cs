using SGCM.Data.Domain.Scheduling.Appointments;

namespace SGCM.Data.Application.Scheduling.Queries.GetDoctorSchedule;

/// <summary>Filtros combinables de la agenda; un filtro nulo no se aplica.</summary>
/// <param name="Status">Estado de la cita: pendiente o confirmada.</param>
/// <param name="From">Primer día local, incluido.</param>
/// <param name="To">Último día local, incluido.</param>
/// <param name="PatientName">Parte del nombre del paciente, sin distinguir mayúsculas ni tildes.</param>
/// <param name="Sort">Orden por fecha de inicio.</param>
public sealed record DoctorScheduleFilter(
    AppointmentStatus? Status,
    DateOnly? From,
    DateOnly? To,
    string? PatientName,
    DoctorScheduleSort Sort);
