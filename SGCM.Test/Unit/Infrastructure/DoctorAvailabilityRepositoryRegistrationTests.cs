using Microsoft.Extensions.DependencyInjection;
using SGCM.Data.Domain.Scheduling.Availability;
using SGCM.Data.Infrastructure;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Data.Infrastructure.Persistence.Repositories.Scheduling;
using Shouldly;

namespace SGCM.Test.Unit.Infrastructure;

public sealed class DoctorAvailabilityRepositoryRegistrationTests
{
    [Fact]
    public void AddInfrastructure_registra_DoctorAvailabilitySeriesRepository_por_scope() =>
        AssertScoped<IDoctorAvailabilitySeriesRepository, DoctorAvailabilitySeriesRepository>();

    [Fact]
    public void AddInfrastructure_registra_DoctorAvailabilityRepository_por_scope() =>
        AssertScoped<IDoctorAvailabilityRepository, DoctorAvailabilityRepository>();

    private static void AssertScoped<TService, TImplementation>() where TService : notnull
    {
        var services = new ServiceCollection();
        services.AddSingleton<Serilog.ILogger>(Serilog.Core.Logger.None);
        services.Configure<DatabaseOptions>(options =>
            options.SgcmDatabase = "Host=localhost;Database=unused;Username=unused;Password=unused");
        services.AddInfrastructure();
        services.Single(descriptor => descriptor.ServiceType == typeof(TService))
            .Lifetime.ShouldBe(ServiceLifetime.Scoped);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        var repository = first.ServiceProvider.GetRequiredService<TService>();

        repository.ShouldBeOfType<TImplementation>();
        repository.ShouldBeSameAs(first.ServiceProvider.GetRequiredService<TService>());
        repository.ShouldNotBeSameAs(second.ServiceProvider.GetRequiredService<TService>());
    }
}
