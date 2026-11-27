using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Domain.Scheduling.Reports;

namespace SGCM.Data.Application.Scheduling.Queries.GetAppointmentReport;

/// <summary>
/// Consulta una página del reporte de citas del médico autenticado con los filtros y el orden pedidos. Es una
/// consulta y no un comando: filtrar u ordenar no deja una fila nueva en el registro de reportes.
/// </summary>
/// <param name="Criteria">Tipo, rango de fechas, filtros y orden del reporte.</param>
/// <param name="PageNumber">Número de página, desde 1.</param>
/// <param name="PageSize">Cantidad de filas por página.</param>
public sealed record GetAppointmentReportQuery(
    ReportCriteria Criteria,
    int PageNumber = GetAppointmentReportQuery.FirstPage,
    int PageSize = GetAppointmentReportQuery.DefaultPageSize) : IQuery<AppointmentReport>
{
    /// <summary>Primera página.</summary>
    public const int FirstPage = 1;

    /// <summary>Filas por página si el cliente no indica otra cantidad.</summary>
    public const int DefaultPageSize = 20;

    /// <summary>Filas que puede tener una página como máximo.</summary>
    public const int MaximumPageSize = 100;
}
