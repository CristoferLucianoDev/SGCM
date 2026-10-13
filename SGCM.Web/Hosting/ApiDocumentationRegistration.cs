using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace SGCM.Web.Hosting;

public static class ApiDocumentationRegistration
{
    private const string BearerScheme = "bearer";
    private const string BearerFormat = "JWT";

    public static IServiceCollection AddSgcmApiDocumentation(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.AddSecurityDefinition(AuthorizeOperationFilter.SecuritySchemeName, new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = BearerScheme,
                BearerFormat = BearerFormat,
                Description = "Pega el access token sin el prefijo \"Bearer\".",
            });
            options.OperationFilter<AuthorizeOperationFilter>();
        });

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