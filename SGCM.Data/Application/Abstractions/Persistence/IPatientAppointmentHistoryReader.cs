using SGCM.Data.Application.Common;
using SGCM.Data.Application.Scheduling.Queries.GetPatientAppointmentHistory;
using SGCM.Data.Domain.Scheduling.Appointments;

namespace SGCM.Data.Application.Abstractions.Persistence;

/// <summary>
/// Lee el historial paginado de citas de un paciente sin cargar el agregado.
/// </summary>
public interface IPatientAppointmentHistoryReader
{
    /// <summary>
    /// Lista una página de citas del paciente, en cualquier estado, ordenadas por inicio descendente y, a igual
    /// inicio, por id descendente.
    /// </summary>
    /// <param name="patientId">Id del paciente, no el de su usuario.</param>
    /// <param name="status">Estado que se filtra; <c>null</c> incluye todos.</param>
    /// <param name="from">Fecha local inicial, incluida; se compara con la fecha local de inicio de la cita. <c>null</c> no limita.</param>
    /// <param name="to">Fecha local final, incluida; se compara con la fecha local de inicio de la cita. <c>null</c> no limita.</param>
    /// <param name="pageNumber">Número de página, desde 1.</param>
    /// <param name="pageSize">Cantidad de citas por página.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>
    /// La página pedida, con el total de citas que cumplen el filtro contado antes de paginar y con la fecha y las
    /// horas de cada cita en la zona local de la agenda.
    /// </returns>
    Task<PagedResult<PatientAppointmentDto>> ListAsync(
        int patientId,
        AppointmentStatus? status,
        DateOnly? from,
        DateOnly? to,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken);
}
