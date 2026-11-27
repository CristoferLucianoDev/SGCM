using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling.Reports;

namespace SGCM.Data.Application.Scheduling.Commands.GenerateAppointmentReport;

/// <summary>
/// Construye el reporte con <see cref="IAppointmentReportBuilder"/> y registra la generación con la cantidad total de
/// filas, no solo las de la primera página. El decorador de transacción guarda el registro.
/// </summary>
internal sealed class GenerateAppointmentReportCommandHandler(
    IAppointmentReportBuilder builder,
    IAppointmentReportLogRepository logRepository,
    IDateTimeProvider clock) : ICommandHandler<GenerateAppointmentReportCommand, AppointmentReport>
{
    private const int FirstPage = 1;

    public async Task<Result<AppointmentReport>> HandleAsync(
        GenerateAppointmentReportCommand command, CancellationToken cancellationToken)
    {
        var report = await builder.BuildPageAsync(command.Criteria, FirstPage, command.PageSize, cancellationToken);
        if (report.IsFailure) return report.Error;

        logRepository.Add(AppointmentReportLog.ForGeneration(
            report.Value.DoctorId, command.Criteria, report.Value.Rows.TotalCount, clock.UtcNow));

        return report.Value;
    }
}
