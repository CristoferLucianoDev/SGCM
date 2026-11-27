using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Reporting;
using SGCM.Data.Domain.Scheduling.Reports;

namespace SGCM.Data.Application.Scheduling.Commands.ExportAppointmentReport;

/// <summary>
/// Exporta a un archivo el reporte de citas del médico autenticado, con los filtros y el orden de la pantalla, y deja
/// la exportación en el registro de reportes. El médico sale de la sesión.
/// </summary>
/// <param name="Criteria">Tipo, rango de fechas, filtros y orden del reporte.</param>
/// <param name="Format">Formato del archivo.</param>
public sealed record ExportAppointmentReportCommand(ReportCriteria Criteria, ReportFormat Format) : ICommand<ExportedFile>;
