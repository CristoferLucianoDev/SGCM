namespace SGCM.Web.Contracts.Scheduling;

/// <summary>
/// Parámetros de consulta del historial de citas del paciente.
/// </summary>
/// <param name="Status">Estado que se filtra; <c>null</c> incluye todos.</param>
/// <param name="From">Fecha local inicial, incluida, en formato yyyy-MM-dd; <c>null</c> no limita.</param>
/// <param name="To">Fecha local final, incluida, en formato yyyy-MM-dd; <c>null</c> no limita.</param>
/// <param name="PageNumber">Número de página, desde 1.</param>
/// <param name="PageSize">Cantidad de citas por página.</param>
public sealed record ListPatientAppointmentsRequest(
    AppointmentStatusFilter? Status = null,
    DateOnly? From = null,
    DateOnly? To = null,
    int PageNumber = 1,
    int PageSize = 20);
