using Microsoft.Extensions.Options;
using SGCM.Data.Application.Scheduling.Commands.GenerateAvailabilityBlocks;
using ILogger = Serilog.ILogger;

namespace SGCM.Web.Jobs;

public sealed class GenerateAvailabilityBlocksJob(
    IServiceScopeFactory scopeFactory,
    IOptions<WorkerOptions> options,
    ILogger logger) : RecurringJob<GenerateAvailabilityBlocksCommand>(scopeFactory, options, logger)
{
    protected override TimeSpan GetInterval(WorkerIntervalsOptions intervals) => intervals.GenerateAvailabilityBlocks;
}
