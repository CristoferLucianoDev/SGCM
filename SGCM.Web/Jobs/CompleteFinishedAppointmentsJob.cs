using Microsoft.Extensions.Options;
using SGCM.Data.Application.Scheduling.Commands.CompleteFinishedAppointments;
using ILogger = Serilog.ILogger;

namespace SGCM.Web.Jobs;

/// <summary>
/// Cada pocos minutos completa las citas confirmadas que ya terminaron sin que el médico las cerrara, en lotes;
/// cada ejecución toma las que falten.
/// </summary>
public sealed class CompleteFinishedAppointmentsJob(
    IServiceScopeFactory scopeFactory,
    IOptions<WorkerOptions> options,
    ILogger logger) : RecurringJob<CompleteFinishedAppointmentsCommand>(scopeFactory, options, logger)
{
    /// <inheritdoc />
    protected override TimeSpan GetInterval(WorkerIntervalsOptions intervals) => intervals.CompleteFinishedAppointments;
}
