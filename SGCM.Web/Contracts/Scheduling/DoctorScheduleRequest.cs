using SGCM.Data.Application.Scheduling.Queries.GetDoctorSchedule;
using SGCM.Data.Domain.Scheduling.Appointments;

namespace SGCM.Web.Contracts.Scheduling;

/// <summary>Filtros combinables, orden y página de la agenda del médico; un filtro omitido no se aplica.</summary>
/// <param name="Status">Estado: <c>pending</c> o <c>confirmed</c>.</param>
/// <param name="From">Primer día local, incluido.</param>
/// <param name="To">Último día local, incluido.</param>
/// <param name="PatientName">Parte del nombre del paciente, sin distinguir mayúsculas ni tildes.</param>
/// <param name="Sort">Orden por fecha: <c>ascending</c> (por defecto) o <c>descending</c>.</param>
/// <param name="PageNumber">Página, desde 1.</param>
/// <param name="PageSize">Tamaño de página, de 1 a 100.</param>
public sealed record DoctorScheduleRequest(
    AppointmentStatus? Status = null,
    DateOnly? From = null,
    DateOnly? To = null,
    string? PatientName = null,
    DoctorScheduleSort Sort = DoctorScheduleSort.Ascending,
    int PageNumber = 1,
    int PageSize = 20);
