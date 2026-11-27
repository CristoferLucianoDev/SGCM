using Microsoft.Extensions.DependencyInjection;
using SGCM.Data.Application.Abstractions.Reporting;
using SGCM.Data.Infrastructure;
using SGCM.Data.Infrastructure.Persistence;
using Shouldly;

namespace SGCM.Test.Unit.Infrastructure;

public sealed class ReportExporterRegistrationTests
{
    [Fact]
    public void AddInfrastructure_registra_un_exportador_por_formato_como_singleton()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Serilog.ILogger>(Serilog.Core.Logger.None);
        services.Configure<DatabaseOptions>(options =>
            options.SgcmDatabase = "Host=localhost;Database=unused;Username=unused;Password=unused");
        services.AddInfrastructure();
        services.Where(descriptor => descriptor.ServiceType == typeof(IReportExporter))
            .ShouldAllBe(descriptor => descriptor.Lifetime == ServiceLifetime.Singleton);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        var exporters = provider.GetServices<IReportExporter>().ToList();

        exporters.Select(exporter => exporter.Format)
            .ShouldBe(Enum.GetValues<ReportFormat>(), ignoreOrder: true);
    }
}
