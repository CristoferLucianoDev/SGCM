using Microsoft.Extensions.Options;
using SGCM.Data.Application.Identity;
using SGCM.Data.Application.Scheduling;
using SGCM.Data.Infrastructure.Notifications.Channels;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Data.Infrastructure.Security;
using SGCM.Web.Jobs;

namespace SGCM.Web.Hosting;

public static class OptionsRegistration
{
    public static IServiceCollection AddSgcmOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddValidatedOptions<SmtpOptions, SmtpOptionsValidator>(configuration, SmtpOptions.SectionName);
        services.AddValidatedOptions<DatabaseOptions, DatabaseOptionsValidator>(configuration, DatabaseOptions.SectionName);
        services.AddValidatedOptions<SchedulingOptions, SchedulingOptionsValidator>(configuration, SchedulingOptions.SectionName);
        services.AddValidatedOptions<JwtOptions, JwtOptionsValidator>(configuration, JwtOptions.SectionName);
        services.AddValidatedOptions<AccountsOptions, AccountsOptionsValidator>(configuration, AccountsOptions.SectionName);
        services.AddValidatedOptions<LoginLimitsOptions, LoginLimitsOptionsValidator>(configuration, LoginLimitsOptions.SectionName);
        services.AddValidatedOptions<PasswordResetLimitsOptions, PasswordResetLimitsOptionsValidator>(
            configuration, PasswordResetLimitsOptions.SectionName);
        services.AddValidatedOptions<WorkerOptions, WorkerOptionsValidator>(configuration, WorkerOptions.SectionName);

        services.AddOptions<ApiDocumentationOptions>()
            .Bind(configuration.GetSection(ApiDocumentationOptions.SectionName));

        return services;
    }

    private static void AddValidatedOptions<TOptions, TValidator>(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName)
        where TOptions : class
        where TValidator : class, IValidateOptions<TOptions>
    {
        services.AddOptions<TOptions>()
            .Bind(configuration.GetSection(sectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<TOptions>, TValidator>();
    }
}