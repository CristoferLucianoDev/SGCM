using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling.Reports;

namespace SGCM.Data.Application.Scheduling;

/// <summary>
/// Construye el reporte de citas del médico autenticado a partir de unos <see cref="ReportCriteria"/> ya validados.
/// Lo comparten la generación, el filtrado y la exportación, así que los tres muestran las mismas filas en el mismo
/// orden.
/// </summary>
public interface IAppointmentReportBuilder
{
    /// <summary>Construye los totales y una página de filas, para mostrar en pantalla.</summary>
    /// <param name="criteria">Criterios del reporte.</param>
    /// <param name="pageNumber">Número de página, desde 1.</param>
    /// <param name="pageSize">Cantidad de filas por página.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>
    /// El reporte, o <see cref="ReportErrors.DoctorOnly"/> si el usuario no es médico, o
    /// <see cref="SGCM.Data.Domain.People.Doctors.DoctorErrors.NotFound"/> si no tiene perfil de médico activo.
    /// </returns>
    Task<Result<AppointmentReport>> BuildPageAsync(
        ReportCriteria criteria, int pageNumber, int pageSize, CancellationToken cancellationToken);

    /// <summary>Construye los totales y todas las filas en una sola página, para exportar.</summary>
    /// <param name="criteria">Criterios del reporte.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>Lo mismo que <see cref="BuildPageAsync"/>.</returns>
    Task<Result<AppointmentReport>> BuildAllAsync(ReportCriteria criteria, CancellationToken cancellationToken);
}
