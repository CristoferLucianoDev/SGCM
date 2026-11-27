using Microsoft.Extensions.Options;
using SGCM.Data.Application.Messaging.Commands.SendDueReminders;
using ILogger = Serilog.ILogger;

namespace SGCM.Web.Jobs;

/// <summary>Envía cada minuto (Worker:Intervals:SendDueReminders) los recordatorios cuya hora llegó.</summary>
public sealed class SendDueRemindersJob(
    IServiceScopeFactory scopeFactory,
    IOptions<WorkerOptions> options,
    ILogger logger) : RecurringJob<SendDueRemindersCommand>(scopeFactory, options, logger)
{
    protected override TimeSpan GetInterval(WorkerIntervalsOptions intervals) => intervals.SendDueReminders;
}
