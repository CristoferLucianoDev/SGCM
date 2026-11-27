using SGCM.Data.Application.Scheduling;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Domain.Scheduling.Reports;

namespace SGCM.Data.Application.Abstractions.Persistence;

/// <summary>
/// Lee las citas de un reporte sin cargar agregados. El conteo y las filas aplican exactamente el mismo filtro.
/// </summary>
public interface IAppointmentReportReader
{
    /// <summary>Cuenta las citas que cumplen el filtro, agrupadas por estado.</summary>
    /// <param name="filter">Filtro del reporte.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>La cantidad por estado; los estados sin citas no aparecen.</returns>
    Task<IReadOnlyDictionary<AppointmentStatus, int>> CountByStatusAsync(
        AppointmentReportFilter filter, CancellationToken cancellationToken);

    /// <summary>
    /// Lista las citas que cumplen el filtro en el orden pedido. A igual valor de la columna, se ordenan por inicio y
    /// luego por id en el mismo sentido, para que la pantalla y la exportación coincidan siempre.
    /// </summary>
    /// <param name="filter">Filtro del reporte.</param>
    /// <param name="sortBy">Columna por la que se ordena.</param>
    /// <param name="sortDirection">Sentido del orden.</param>
    /// <param name="offset">Cantidad de filas que se saltan.</param>
    /// <param name="limit">Cantidad máxima de filas, o <see langword="null"/> para leer todas.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    Task<IReadOnlyList<AppointmentReportRowData>> ListAsync(
        AppointmentReportFilter filter,
        ReportSortField sortBy,
        ReportSortDirection sortDirection,
        int offset,
        int? limit,
        CancellationToken cancellationToken);
}
