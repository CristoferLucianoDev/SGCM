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
}
