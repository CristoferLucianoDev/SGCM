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
}
