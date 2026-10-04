using Microsoft.Extensions.Options;
using SGCM.Data.Application.Common;

namespace SGCM.Web.Jobs;

public sealed class WorkerOptions
{
    public const string SectionName = "Worker";

    public bool Enabled { get; set; }
    public WorkerIntervalsOptions Intervals { get; set; } = new();
}

public sealed class WorkerIntervalsOptions
{
    public TimeSpan GenerateAvailabilityBlocks { get; set; }
    public TimeSpan ExpirePendingAppointments { get; set; }
    public TimeSpan CompleteFinishedAppointments { get; set; }
    public TimeSpan SendDueReminders { get; set; }
    public TimeSpan PurgeUnconfirmedAccounts { get; set; }
    public TimeSpan PurgeExpiredCredentials { get; set; }
}

public sealed class WorkerOptionsValidator : IValidateOptions<WorkerOptions>
{
    public ValidateOptionsResult Validate(string? name, WorkerOptions options)
    {
        var errors = new List<string>();
        var intervals = options.Intervals;

        OptionsRules.Positive(errors, "Worker:Intervals:GenerateAvailabilityBlocks", intervals.GenerateAvailabilityBlocks);
        OptionsRules.Positive(errors, "Worker:Intervals:ExpirePendingAppointments", intervals.ExpirePendingAppointments);
        OptionsRules.Positive(errors, "Worker:Intervals:CompleteFinishedAppointments", intervals.CompleteFinishedAppointments);
        OptionsRules.Positive(errors, "Worker:Intervals:SendDueReminders", intervals.SendDueReminders);
        OptionsRules.Positive(errors, "Worker:Intervals:PurgeUnconfirmedAccounts", intervals.PurgeUnconfirmedAccounts);
        OptionsRules.Positive(errors, "Worker:Intervals:PurgeExpiredCredentials", intervals.PurgeExpiredCredentials);

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}