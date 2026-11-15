using Microsoft.Extensions.Options;
using SGCM.Data.Application.Identity.Commands.PurgeExpiredCredentials;
using ILogger = Serilog.ILogger;

namespace SGCM.Web.Jobs;

/// <summary>
/// Borra una vez al día los tokens y las sesiones que terminaron hace más del período de
/// retención, para que las tablas no crezcan sin límite.
/// </summary>
public sealed class PurgeExpiredCredentialsJob(
    IServiceScopeFactory scopeFactory,
    IOptions<WorkerOptions> options,
    ILogger logger) : RecurringJob<PurgeExpiredCredentialsCommand>(scopeFactory, options, logger)
{
    /// <inheritdoc />
    protected override TimeSpan GetInterval(WorkerIntervalsOptions intervals) => intervals.PurgeExpiredCredentials;
}
