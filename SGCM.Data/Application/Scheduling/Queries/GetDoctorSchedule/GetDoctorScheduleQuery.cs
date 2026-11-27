using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Common;
using SGCM.Data.Domain.Scheduling.Appointments;

namespace SGCM.Data.Application.Scheduling.Queries.GetDoctorSchedule;

/// <summary>
/// Agenda paginada del médico autenticado: sus citas pendientes y confirmadas, con filtros combinables por estado,
/// rango de fechas locales y nombre del paciente, ordenadas por fecha en el sentido pedido.
/// </summary>
public sealed record GetDoctorScheduleQuery(
    AppointmentStatus? Status = null,
    DateOnly? From = null,
    DateOnly? To = null,
    string? PatientName = null,
    DoctorScheduleSort Sort = DoctorScheduleSort.Ascending,
    int PageNumber = 1,
    int PageSize = 20) : IQuery<PagedResult<DoctorScheduleItemDto>>
{
    /// <summary>Longitud máxima del texto para buscar por nombre del paciente.</summary>
    public const int PatientNameMaxLength = 100;
}
