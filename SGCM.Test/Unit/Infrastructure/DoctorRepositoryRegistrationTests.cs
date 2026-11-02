using Microsoft.Extensions.DependencyInjection;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Infrastructure;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Data.Infrastructure.Persistence.Repositories.People;
using Shouldly;

namespace SGCM.Test.Unit.Infrastructure;

public sealed class DoctorRepositoryRegistrationTests
{
    [Fact]
    public void AddInfrastructure_registra_DoctorRepository_por_scope()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Serilog.ILogger>(Serilog.Core.Logger.None);
        services.Configure<DatabaseOptions>(options =>
            options.SgcmDatabase = "Host=localhost;Database=unused;Username=unused;Password=unused");
        services.AddInfrastructure();
        services.Single(descriptor => descriptor.ServiceType == typeof(IDoctorRepository))
            .Lifetime.ShouldBe(ServiceLifetime.Scoped);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        var repository = first.ServiceProvider.GetRequiredService<IDoctorRepository>();

        repository.ShouldBeOfType<DoctorRepository>();
        repository.ShouldBeSameAs(first.ServiceProvider.GetRequiredService<IDoctorRepository>());
        repository.ShouldNotBeSameAs(second.ServiceProvider.GetRequiredService<IDoctorRepository>());
    }
}
