using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Domain.Scheduling.Reports;

namespace SGCM.Data.Application.Scheduling.Commands.GenerateAppointmentReport;

/// <summary>
/// Genera el reporte de citas del médico autenticado y deja la generación en el registro de reportes. Es un comando y
/// no una consulta porque inserta esa fila. El médico sale de la sesión.
/// </summary>
/// <param name="Criteria">Tipo, rango de fechas, filtros y orden del reporte.</param>
/// <param name="PageSize">Cantidad de filas de la primera página.</param>
public sealed record GenerateAppointmentReportCommand(ReportCriteria Criteria, int PageSize = GenerateAppointmentReportCommand.DefaultPageSize)
    : ICommand<AppointmentReport>
{
    /// <summary>Filas de la primera página si el cliente no indica otra cantidad.</summary>
    public const int DefaultPageSize = 20;

    /// <summary>Filas que puede tener una página como máximo.</summary>
    public const int MaximumPageSize = 100;
}
