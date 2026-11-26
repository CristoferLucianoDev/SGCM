using Microsoft.Extensions.Options;
using SGCM.Data.Application.Scheduling.Commands.ExpirePendingAppointments;
using ILogger = Serilog.ILogger;

namespace SGCM.Web.Jobs;

/// <summary>
/// Cada pocos minutos vence las citas pendientes que llegaron al plazo de confirmación sin confirmarse, en lotes;
/// cada ejecución toma las que falten.
/// </summary>
public sealed class ExpirePendingAppointmentsJob(
    IServiceScopeFactory scopeFactory,
    IOptions<WorkerOptions> options,
    ILogger logger) : RecurringJob<ExpirePendingAppointmentsCommand>(scopeFactory, options, logger)
{
    /// <inheritdoc />
    protected override TimeSpan GetInterval(WorkerIntervalsOptions intervals) => intervals.ExpirePendingAppointments;
}
