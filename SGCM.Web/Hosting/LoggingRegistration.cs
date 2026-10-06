using Serilog;

namespace SGCM.Web.Hosting;

public static class LoggingRegistration
{
    public static IServiceCollection AddSgcmLogging(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSerilog((serviceProvider, logger) => logger
            .ReadFrom.Configuration(configuration)
            .ReadFrom.Services(serviceProvider));

        return services;
    }
}