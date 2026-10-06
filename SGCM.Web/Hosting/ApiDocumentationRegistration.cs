using Microsoft.Extensions.Options;

namespace SGCM.Web.Hosting;

public static class ApiDocumentationRegistration
{
    public static IServiceCollection AddSgcmApiDocumentation(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();

        return services;
    }

    public static WebApplication UseSgcmApiDocumentation(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<IOptions<ApiDocumentationOptions>>().Value;
        if (options.Enabled)
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        return app;
    }
}