using Microsoft.Extensions.DependencyInjection;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Infrastructure;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Data.Infrastructure.Persistence.Repositories.Scheduling;
using Shouldly;

namespace SGCM.Test.Unit.Infrastructure;

public sealed class AppointmentRepositoryRegistrationTests
{
    [Fact]
    public void AddInfrastructure_registra_AppointmentRepository_por_scope()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Serilog.ILogger>(Serilog.Core.Logger.None);
        services.Configure<DatabaseOptions>(options =>
            options.SgcmDatabase = "Host=localhost;Database=unused;Username=unused;Password=unused");
        services.AddInfrastructure();
        services.Single(descriptor => descriptor.ServiceType == typeof(IAppointmentRepository))
            .Lifetime.ShouldBe(ServiceLifetime.Scoped);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        var repository = first.ServiceProvider.GetRequiredService<IAppointmentRepository>();

        repository.ShouldBeOfType<AppointmentRepository>();
        repository.ShouldBeSameAs(first.ServiceProvider.GetRequiredService<IAppointmentRepository>());
        repository.ShouldNotBeSameAs(second.ServiceProvider.GetRequiredService<IAppointmentRepository>());
    }
}
