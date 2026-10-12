using Microsoft.Extensions.Options;
using SGCM.Data.Application.Identity.Commands.PurgeUnconfirmedAccounts;
using ILogger = Serilog.ILogger;

namespace SGCM.Web.Jobs;

/// <summary>
/// Borra periódicamente los registros pendientes cuyo último enlace de confirmación venció,
/// para que su correo quede libre y pueda registrarse de nuevo.
/// </summary>
public sealed class PurgeUnconfirmedAccountsJob(
    IServiceScopeFactory scopeFactory,
    IOptions<WorkerOptions> options,
    ILogger logger) : RecurringJob<PurgeUnconfirmedAccountsCommand>(scopeFactory, options, logger)
{
    /// <inheritdoc />
    protected override TimeSpan GetInterval(WorkerIntervalsOptions intervals) => intervals.PurgeUnconfirmedAccounts;
}
