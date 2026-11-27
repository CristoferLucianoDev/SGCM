using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Reporting;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling;
using SGCM.Data.Domain.Scheduling.Reports;

namespace SGCM.Data.Application.Scheduling.Commands.ExportAppointmentReport;

/// <summary>
/// Reconstruye el reporte completo con <see cref="IAppointmentReportBuilder"/>, el mismo de la pantalla, así que el
/// archivo tiene las mismas filas en el mismo orden. Rechaza un reporte vacío, elige el exportador por su formato y
/// registra la exportación. El decorador de transacción guarda el registro.
/// </summary>
internal sealed class ExportAppointmentReportCommandHandler(
    IAppointmentReportBuilder builder,
    IEnumerable<IReportExporter> exporters,
    IAppointmentReportLogRepository logRepository,
    IDateTimeProvider clock,
    BusinessCalendar calendar) : ICommandHandler<ExportAppointmentReportCommand, ExportedFile>
{
    public async Task<Result<ExportedFile>> HandleAsync(
        ExportAppointmentReportCommand command, CancellationToken cancellationToken)
    {
        var report = await builder.BuildAllAsync(command.Criteria, cancellationToken);
        if (report.IsFailure) return report.Error;
        if (report.Value.Rows.TotalCount == 0) return ReportErrors.EmptyReport;

        var now = clock.UtcNow;
        var document = AppointmentReportDocumentFactory.Create(report.Value, command.Criteria, calendar.ToLocal(now));
        // Hay un solo exportador por formato; que falte uno es un error de configuración, no del usuario.
        var file = exporters.Single(exporter => exporter.Format == command.Format).Export(document);

        logRepository.Add(AppointmentReportLog.ForExport(
            report.Value.DoctorId, command.Criteria, ToLogFormat(command.Format), report.Value.Rows.TotalCount, now));

        return file;
    }

    // El registro guarda el formato como su extensión en minúsculas: pdf, xlsx o docx.
    private static string ToLogFormat(ReportFormat format) => format.ToString().ToLowerInvariant();
}
