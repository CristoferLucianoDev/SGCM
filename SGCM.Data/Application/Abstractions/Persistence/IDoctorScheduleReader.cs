using SGCM.Data.Application.Common;
using SGCM.Data.Application.Scheduling.Queries.GetDoctorAppointmentDetail;
using SGCM.Data.Application.Scheduling.Queries.GetDoctorSchedule;

namespace SGCM.Data.Application.Abstractions.Persistence;

/// <summary>Lee la agenda del médico desde la vista <c>scheduling.vw_doctor_schedule</c>: citas pendientes y confirmadas.</summary>
public interface IDoctorScheduleReader
{
    /// <summary>Una página de la agenda con los filtros combinados y el orden pedido.</summary>
    Task<PagedResult<DoctorScheduleItemDto>> ListAsync(
        int doctorId, DoctorScheduleFilter filter, int pageNumber, int pageSize, CancellationToken cancellationToken);

    /// <summary>Todas las citas entre dos fechas locales, ambas incluidas, en orden cronológico.</summary>
    Task<IReadOnlyList<DoctorScheduleItemDto>> ListRangeAsync(
        int doctorId, DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary>
    /// Detalle de una cita de la agenda con el contacto del paciente; <c>null</c> si no existe, es de otro médico o ya
    /// no está activa.
    /// </summary>
    Task<DoctorAppointmentDetailDto?> GetDetailAsync(int doctorId, int appointmentId, CancellationToken cancellationToken);
}
