using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Common;
using SGCM.Data.Domain.Scheduling.Appointments;

namespace SGCM.Data.Application.Scheduling.Queries.GetPatientAppointmentHistory;

/// <summary>
/// Historial de citas del paciente autenticado, pasadas y futuras, de la más reciente a la más antigua.
/// </summary>
/// <param name="Status">Estado que se filtra; <c>null</c> incluye todos.</param>
/// <param name="From">Fecha local inicial, incluida; <c>null</c> no limita por abajo.</param>
/// <param name="To">Fecha local final, incluida; <c>null</c> no limita por arriba.</param>
/// <param name="PageNumber">Número de página, desde 1.</param>
/// <param name="PageSize">Cantidad de citas por página.</param>
public sealed record GetPatientAppointmentHistoryQuery(
    AppointmentStatus? Status = null,
    DateOnly? From = null,
    DateOnly? To = null,
    int PageNumber = 1,
    int PageSize = 20) : IQuery<PagedResult<PatientAppointmentDto>>;
