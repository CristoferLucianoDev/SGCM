using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using SGCM.Web.Jobs;

namespace SGCM.Test.Unit.Web;

public sealed class JobRegistrationTests
{
    [Fact]
    public void AddSgcmJobs_RegistersPurgeUnconfirmedAccountsJobAsHostedService()
    {
        var services = new ServiceCollection();

        services.AddSgcmJobs();

        services.ShouldContain(descriptor =>
            descriptor.ServiceType == typeof(IHostedService)
            && descriptor.ImplementationType == typeof(PurgeUnconfirmedAccountsJob));
    }

    [Fact]
    public void AddSgcmJobs_RegistersPurgeExpiredCredentialsJobAsHostedService()
    {
        var services = new ServiceCollection();

        services.AddSgcmJobs();

        services.ShouldContain(descriptor =>
            descriptor.ServiceType == typeof(IHostedService)
            && descriptor.ImplementationType == typeof(PurgeExpiredCredentialsJob));
    }

    [Fact]
    public void AddSgcmJobs_RegistersGenerateAvailabilityBlocksJobAsHostedService()
    {
        var services = new ServiceCollection();

        services.AddSgcmJobs();

        services.ShouldContain(descriptor =>
            descriptor.ServiceType == typeof(IHostedService)
            && descriptor.ImplementationType == typeof(GenerateAvailabilityBlocksJob));
    }

    [Fact]
    public void GenerateAvailabilityBlocksJob_UsesItsOwnInterval()
    {
        var intervals = new WorkerIntervalsOptions
        {
            GenerateAvailabilityBlocks = TimeSpan.FromHours(24),
            PurgeExpiredCredentials = TimeSpan.FromHours(1)
        };
        var job = new GenerateAvailabilityBlocksJob(
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            Microsoft.Extensions.Options.Options.Create(new WorkerOptions()),
            Serilog.Core.Logger.None);

        var interval = typeof(GenerateAvailabilityBlocksJob)
            .GetMethod("GetInterval", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(job, [intervals]);

        interval.ShouldBe(TimeSpan.FromHours(24));
    }

    [Fact]
    public void AddSgcmJobs_RegistersExpirePendingAppointmentsJobAsHostedService()
    {
        var services = new ServiceCollection();

        services.AddSgcmJobs();

        services.ShouldContain(descriptor =>
            descriptor.ServiceType == typeof(IHostedService)
            && descriptor.ImplementationType == typeof(ExpirePendingAppointmentsJob));
    }

    [Fact]
    public void ExpirePendingAppointmentsJob_UsesItsOwnInterval()
    {
        var intervals = new WorkerIntervalsOptions
        {
            ExpirePendingAppointments = TimeSpan.FromMinutes(5),
            GenerateAvailabilityBlocks = TimeSpan.FromHours(24)
        };
        var job = new ExpirePendingAppointmentsJob(
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            Microsoft.Extensions.Options.Options.Create(new WorkerOptions()),
            Serilog.Core.Logger.None);

        var interval = typeof(ExpirePendingAppointmentsJob)
            .GetMethod("GetInterval", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(job, [intervals]);

        interval.ShouldBe(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void AddSgcmJobs_RegistersCompleteFinishedAppointmentsJobAsHostedService()
    {
        var services = new ServiceCollection();

        services.AddSgcmJobs();

        services.ShouldContain(descriptor =>
            descriptor.ServiceType == typeof(IHostedService)
            && descriptor.ImplementationType == typeof(CompleteFinishedAppointmentsJob));
    }

    [Fact]
    public void CompleteFinishedAppointmentsJob_UsesItsOwnInterval()
    {
        var intervals = new WorkerIntervalsOptions
        {
            CompleteFinishedAppointments = TimeSpan.FromMinutes(10),
            ExpirePendingAppointments = TimeSpan.FromMinutes(5)
        };
        var job = new CompleteFinishedAppointmentsJob(
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            Microsoft.Extensions.Options.Options.Create(new WorkerOptions()),
            Serilog.Core.Logger.None);

        var interval = typeof(CompleteFinishedAppointmentsJob)
            .GetMethod("GetInterval", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(job, [intervals]);

        interval.ShouldBe(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public void AddSgcmJobs_RegistersSendDueRemindersJobAsHostedService()
    {
        var services = new ServiceCollection();

        services.AddSgcmJobs();

        services.ShouldContain(descriptor =>
            descriptor.ServiceType == typeof(IHostedService)
            && descriptor.ImplementationType == typeof(SendDueRemindersJob));
    }

    [Fact]
    public void SendDueRemindersJob_UsesItsOwnInterval()
    {
        var intervals = new WorkerIntervalsOptions
        {
            SendDueReminders = TimeSpan.FromMinutes(1),
            CompleteFinishedAppointments = TimeSpan.FromMinutes(10)
        };
        var job = new SendDueRemindersJob(
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            Microsoft.Extensions.Options.Options.Create(new WorkerOptions()),
            Serilog.Core.Logger.None);

        var interval = typeof(SendDueRemindersJob)
            .GetMethod("GetInterval", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(job, [intervals]);

        interval.ShouldBe(TimeSpan.FromMinutes(1));
    }
}
